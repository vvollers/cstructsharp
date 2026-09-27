#!/usr/bin/env node
/**
 * Carries the reviewed equivalent mutations in contracts/quality/mutation-equivalents.json across source edits.
 *
 * For every entry whose source hash no longer matches, the tool reads the source at `--base` (the revision the
 * entry was reviewed against, which must still match its recorded hash), maps each mutant's lines through the
 * diff to the working tree, and checks that the mutated lines are textually identical. It then prints every mutant
 * that lies within `--context` lines of a changed hunk, with its reason and the current code, for manual review.
 * A mutant whose lines were changed or removed cannot be carried: edit its entry by hand, or drop it.
 *
 *   node tools/quality/remap-mutation-equivalents.mjs --base <revision> [--context 15] [--write [--drop-blocked]]
 *
 * Without --write the tool only reports. With --write it rewrites the moved locations and the new source hashes,
 * and refuses when any mutant cannot be carried unless --drop-blocked deletes those entries. Dropping only removes a
 * suppression: the next mutation run reports the mutant again (if it still exists) for a fresh review. Carrying a
 * location is not a proof: read every reported mutant.
 */
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { mutationSource } from "../lib/mutation-partitions.mjs";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";

const options = parseArguments(process.argv.slice(2), { base: "string", context: "number", write: "flag", "drop-blocked": "flag" }, { defaults: { context: 15, write: false, "drop-blocked": false } });
const policyPath = path.join(repositoryRoot, "contracts/quality/mutation-equivalents.json");
const DROPPED = Symbol("dropped");

/** The key that identifies one mutant of one file: its pattern and exact location. */
const mutantKey = (pattern, startLine, startColumn, endLine, endColumn) => `${pattern}\u0000${startLine}:${startColumn}:${endLine}:${endColumn}`;

/** The SHA-256 of source text after the line-ending normalization the mutation gate applies. */
function sourceHash(text) {
  return crypto.createHash("sha256").update(text.replaceAll("\r\n", "\n")).digest("hex");
}

/** The changed hunks between `base` and the working tree as `{ oldStart, oldCount, newStart, newCount }`. */
function diffHunks(base, source) {
  const diff = runCommand("git", ["-C", repositoryRoot, "diff", "--no-color", "-U0", base, "--", source]).stdout;
  const hunks = [];
  for (const match of diff.matchAll(/^@@ -(\d+)(?:,(\d+))? \+(\d+)(?:,(\d+))? @@/gm)) {
    hunks.push({ oldStart: Number(match[1]), oldCount: match[2] === undefined ? 1 : Number(match[2]), newStart: Number(match[3]), newCount: match[4] === undefined ? 1 : Number(match[4]) });
  }
  return hunks;
}

/** Maps an old line to its new line, or null when a hunk replaced or removed it. */
function mapLine(line, hunks) {
  let offset = 0;
  for (const hunk of hunks) {
    // A pure insertion (oldCount 0) sits after oldStart; every other hunk replaces oldStart..oldStart+oldCount-1.
    const firstChanged = hunk.oldCount === 0 ? hunk.oldStart + 1 : hunk.oldStart;
    if (line < firstChanged) break;
    if (hunk.oldCount > 0 && line < hunk.oldStart + hunk.oldCount) return null;
    offset += hunk.newCount - hunk.oldCount;
  }
  return line + offset;
}

/** The distance in old lines from `line` to the nearest changed hunk. */
function distanceToChange(line, hunks) {
  return Math.min(...hunks.map((hunk) => {
    const last = hunk.oldStart + Math.max(hunk.oldCount, 1) - 1;
    return line < hunk.oldStart ? hunk.oldStart - line : line > last ? line - last : 0;
  }));
}

await main(() => {
  assertCondition(options.base, "Pass --base <revision>: the revision the stale entries were reviewed against.");
  const policy = JSON.parse(fs.readFileSync(policyPath, "utf8"));
  const moves = new Map();
  const hashes = new Map();
  const blocked = [];
  const drops = new Set();
  let reviewed = 0;

  for (const entry of policy.files) {
    const source = mutationSource(entry.pattern);
    const current = fs.readFileSync(path.join(repositoryRoot, source), "utf8").replaceAll("\r\n", "\n");
    if (sourceHash(current) === entry.sourceSha256) continue;

    const old = runCommand("git", ["-C", repositoryRoot, "show", `${options.base}:${source}`]).stdout.replaceAll("\r\n", "\n");
    assertCondition(sourceHash(old) === entry.sourceSha256, `${entry.pattern}: the source at ${options.base} does not match the recorded hash; choose the reviewed revision.`);
    const oldLines = old.split("\n");
    const newLines = current.split("\n");
    const hunks = diffHunks(options.base, source);
    hashes.set(entry.pattern, sourceHash(current));
    console.log(`\n== ${entry.pattern}: ${entry.mutants.length} reviewed mutants, ${hunks.length} changed hunks`);

    for (const mutant of entry.mutants) {
      const { start, end } = mutant.location;
      const newStart = mapLine(start.line, hunks);
      const newEnd = mapLine(end.line, hunks);
      const label = `${mutant.mutatorName} at ${start.line}:${start.column} "${mutant.replacement.slice(0, 60)}"`;
      const identical = newStart !== null && newEnd !== null && newEnd - newStart === end.line - start.line &&
                        oldLines.slice(start.line - 1, end.line).join("\n") === newLines.slice(newStart - 1, newEnd).join("\n");
      if (!identical) {
        blocked.push(`${entry.pattern}: ${label} — its lines changed; review and edit the entry by hand.`);
        drops.add(mutantKey(entry.pattern, start.line, start.column, end.line, end.column));
        continue;
      }

      if (newStart !== start.line) moves.set(`${entry.pattern}\u0000${start.line}:${start.column}:${end.line}:${end.column}`, newStart - start.line);
      if (distanceToChange(start.line, hunks) <= options.context) {
        reviewed++;
        console.log(`\n-- REVIEW ${label} -> line ${newStart}\n   reason: ${mutant.reason}\n   code:`);
        console.log(newLines.slice(Math.max(0, newStart - 4), newEnd + 3).map((text, index) => `   ${String(Math.max(1, newStart - 3) + index).padStart(5)} | ${text}`).join("\n"));
      }
    }
  }

  console.log(`\n${hashes.size} stale files, ${moves.size} moved mutants, ${reviewed} near changes to review, ${blocked.length} that cannot be carried.`);
  for (const message of blocked) console.log(`BLOCKED ${message}`);
  if (!options.write) return;
  assertCondition(blocked.length === 0 || options["drop-blocked"], "Refusing to write while some mutants cannot be carried; pass --drop-blocked to delete them.");

  // Rewrite in place so the file keeps its hand-kept compact layout: one location object per line.
  let pattern = null;
  const lines = fs.readFileSync(policyPath, "utf8").split("\n").map((line) => {
    const file = /"pattern": "([^"]+)"/.exec(line);
    if (file) pattern = file[1];
    if (hashes.has(pattern) && /"sourceSha256": "/.test(line)) return line.replace(/"sourceSha256": "[0-9a-f]+"/, `"sourceSha256": "${hashes.get(pattern)}"`);
    const location = /"location": \{"start":\{"line":(\d+),"column":(\d+)\},"end":\{"line":(\d+),"column":(\d+)\}\}/.exec(line);
    const key = location && mutantKey(pattern, location[1], location[2], location[3], location[4]);
    if (key && drops.has(key)) return DROPPED;
    const delta = key && moves.get(key);
    if (!delta) return line;
    return line.replace(location[0], `"location": {"start":{"line":${Number(location[1]) + delta},"column":${location[2]}},"end":{"line":${Number(location[3]) + delta},"column":${location[4]}}}`);
  });
  // A mutant is three lines (name, location, reason): drop a blocked one whole, then the comma it may leave before "]".
  const kept = lines.filter((line, index) => line !== DROPPED && lines[index + 1] !== DROPPED && lines[index - 1] !== DROPPED);
  fs.writeFileSync(policyPath, kept.join("\n").replace(/\},\n(\s*\])/g, "}\n$1"));
  console.log(`Wrote moved locations and source hashes${drops.size ? `; dropped ${drops.size} entries` : ""}.`);
});
