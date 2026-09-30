#!/usr/bin/env node
/**
 * Carries the reviewed equivalent mutations in contracts/quality/mutation-equivalents.json across source edits.
 *
 * For every entry whose source hash no longer matches, the tool reads the source at `--base` (the revision the
 * entry was reviewed against, which must still match its recorded hash), maps each mutant's lines through the
 * diff to the working tree, and checks that the mutated lines are textually identical. It then prints every mutant
 * that lies within `--context` lines of a changed hunk, with its reason and the current code, for manual review.
 *
 * A mutant whose lines were changed or moved - within its file, or into another file of the mutation scope, as when
 * a method is extracted or a file is split into partial files - is looked up by its lines' text: when exactly one
 * place in the changed scope files has the same lines (indentation aside), it is carried there (columns shifted by
 * the new indentation) and always printed for review. The mutants of a deleted file are looked up the same way, since
 * its code survives only where it was moved to. A mutant found nowhere, or in several places, cannot be carried: edit
 * its entry by hand, or drop it.
 *
 *   node tools/quality/remap-mutation-equivalents.mjs --base <revision> [--context 15] [--write [--drop-blocked]]
 *
 * Without --write the tool only reports. With --write it rewrites the moved locations, the carried entries and the
 * new source hashes, and refuses when any mutant cannot be carried unless --drop-blocked deletes those entries. A file
 * left with no reviewed mutant leaves the policy. Dropping only removes a suppression: the next mutation run reports
 * the mutant again (if it still exists) for a fresh review. Carrying a location is not a proof: read every reported
 * mutant.
 */
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { mutationSource } from "../lib/mutation-partitions.mjs";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";

const options = parseArguments(process.argv.slice(2), { base: "string", context: "number", write: "flag", "drop-blocked": "flag" }, { defaults: { context: 15, write: false, "drop-blocked": false } });
const policyPath = path.join(repositoryRoot, "contracts/quality/mutation-equivalents.json");

/** The SHA-256 of source text after the line-ending normalization the mutation gate applies. */
function sourceHash(text) {
  return crypto.createHash("sha256").update(text.replaceAll("\r\n", "\n")).digest("hex");
}

/** A working-tree source with normalized line endings. */
function readCurrent(source) {
  return fs.readFileSync(path.join(repositoryRoot, source), "utf8").replaceAll("\r\n", "\n");
}

/** A working-tree source with normalized line endings, or null when the file was deleted. */
function readCurrentOrNull(source) {
  return fs.existsSync(path.join(repositoryRoot, source)) ? readCurrent(source) : null;
}

/** A source at `--base`, or null when the file did not exist there. */
function readBase(source) {
  const result = runCommand("git", ["-C", repositoryRoot, "show", `${options.base}:${source}`], { allowFailure: true });
  return result.status === 0 ? result.stdout.replaceAll("\r\n", "\n") : null;
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

/** The width of a line's leading whitespace. */
const indentOf = (line) => line.length - line.trimStart().length;

/** Whether a trimmed line says something (not blank and not a lone brace), so it can confirm a match as context. */
const meaningful = (line) => line !== "" && !/^[{}]\)?[;,]?$/.test(line);

/** The nearest meaningful trimmed line before `index` (step -1) or at and after it (step 1), or null. */
function neighbour(lines, index, step) {
  for (let at = index; at >= 0 && at < lines.length; at += step) {
    const line = lines[at].trim();
    if (meaningful(line)) return line;
  }

  return null;
}

/**
 * Finds the one place in the changed scope files whose lines equal `oldLines[first..last]` apart from indentation and
 * whose context matches: the nearest meaningful line before or after in the mutant's own file (`home`), both of them
 * in another file - a lone equal line is not enough, since the same statement often appears in several methods. Returns `{ pattern, startLine, shift, endShift }` (the start and end
 * lines' indentation changes), or null when there is none or more than one.
 */
function findUnique(oldLines, first, last, changed, home) {
  const lines = oldLines.slice(first, last + 1);
  const wanted = lines.map((line) => line.trim());
  const before = neighbour(oldLines, first - 1, -1);
  const after = neighbour(oldLines, last + 1, 1);
  let found = null;
  for (const [pattern, text] of changed) {
    const candidate = text.split("\n");
    for (let index = 0; index + wanted.length <= candidate.length; index++) {
      if (!wanted.every((line, offset) => candidate[index + offset].trim() === line)) continue;
      const beforeMatches = before !== null && neighbour(candidate, index - 1, -1) === before;
      const afterMatches = after !== null && neighbour(candidate, index + wanted.length, 1) === after;
      const confirmed = pattern === home ? beforeMatches || afterMatches : beforeMatches && afterMatches;
      if (!confirmed) continue;
      if (found) return null;
      found = { pattern, startLine: index + 1, shift: indentOf(candidate[index]) - indentOf(lines[0]), endShift: indentOf(candidate[index + wanted.length - 1]) - indentOf(lines.at(-1)) };
    }
  }

  return found;
}

/** Serializes the policy in its hand-kept compact layout: one location object per line, a mutant per three lines. */
function serialize(policy) {
  const json = JSON.stringify;
  const files = policy.files.map((file) => `  {\n   "pattern": ${json(file.pattern)},\n   "sourceSha256": ${json(file.sourceSha256)},\n   "mutants": [\n${file.mutants.map((mutant) =>
    `    { "mutatorName": ${json(mutant.mutatorName)}, "replacement": ${json(mutant.replacement)},\n     "location": ${json(mutant.location)},\n     "reason": ${json(mutant.reason)} }`).join(",\n")}\n   ]\n  }`);
  return `{\n "schemaVersion": ${json(policy.schemaVersion)},\n "strykerVersion": ${json(policy.strykerVersion)},\n "files": [\n${files.join(",\n")}\n ]\n}\n`;
}

/** Prints a carried mutant with its reason and the code around its new place. */
function printReview(kind, label, target, startLine, endLine, reason) {
  const lines = readCurrent(mutationSource(target)).split("\n");
  console.log(`\n-- ${kind} ${label} -> ${target} line ${startLine}\n   reason: ${reason}\n   code:`);
  console.log(lines.slice(Math.max(0, startLine - 4), endLine + 3).map((text, index) => `   ${String(Math.max(1, startLine - 3) + index).padStart(5)} | ${text}`).join("\n"));
}

await main(() => {
  assertCondition(options.base, "Pass --base <revision>: the revision the stale entries were reviewed against.");
  const policy = JSON.parse(fs.readFileSync(policyPath, "utf8"));
  const scope = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "stryker-config.json"), "utf8"))["stryker-config"].mutate;

  // Every scope file that differs from the base is a place a moved mutant may have gone.
  const changed = new Map();
  for (const pattern of scope) {
    const source = mutationSource(pattern);
    const current = readCurrent(source);
    if (readBase(source) !== current) changed.set(pattern, current);
  }

  const carried = new Map(policy.files.map((file) => [file.pattern, []]));
  const blocked = [];
  let moved = 0;
  let acrossFiles = 0;
  let reviewed = 0;
  for (const entry of policy.files) {
    const source = mutationSource(entry.pattern);
    const current = readCurrentOrNull(source);
    if (current !== null && sourceHash(current) === entry.sourceSha256) {
      carried.get(entry.pattern).push(...entry.mutants);
      continue;
    }

    const old = readBase(source);
    assertCondition(old !== null && sourceHash(old) === entry.sourceSha256, `${entry.pattern}: the source at ${options.base} does not match the recorded hash; choose the reviewed revision.`);
    const oldLines = old.split("\n");
    // A deleted file keeps none of its lines in place: every mutant is looked up where its code may have moved.
    const newLines = current === null ? [] : current.split("\n");
    const hunks = current === null ? [] : diffHunks(options.base, source);
    console.log(`\n== ${entry.pattern}: ${entry.mutants.length} reviewed mutants, ${current === null ? "file deleted" : `${hunks.length} changed hunks`}`);

    for (const mutant of entry.mutants) {
      const { start, end } = mutant.location;
      const newStart = mapLine(start.line, hunks);
      const newEnd = mapLine(end.line, hunks);
      const label = `${mutant.mutatorName} at ${entry.pattern}:${start.line}:${start.column} "${mutant.replacement.slice(0, 60)}"`;
      const mutatedLines = oldLines.slice(start.line - 1, end.line);
      const identical = current !== null && newStart !== null && newEnd !== null && newEnd - newStart === end.line - start.line &&
                        mutatedLines.join("\n") === newLines.slice(newStart - 1, newEnd).join("\n");
      if (identical) {
        if (newStart !== start.line) moved++;
        carried.get(entry.pattern).push({ ...mutant, location: { start: { line: newStart, column: start.column }, end: { line: newEnd, column: end.column } } });
        if (distanceToChange(start.line, hunks) <= options.context) {
          reviewed++;
          printReview("REVIEW", label, entry.pattern, newStart, newEnd, mutant.reason);
        }

        continue;
      }

      const found = findUnique(oldLines, start.line - 1, end.line - 1, changed, entry.pattern);
      if (!found) {
        blocked.push(`${label} — its lines changed and occur nowhere else exactly once; review and edit the entry by hand.`);
        continue;
      }

      acrossFiles += found.pattern === entry.pattern ? 0 : 1;
      moved++;
      reviewed++;
      const lineCount = end.line - start.line;
      const location = { start: { line: found.startLine, column: start.column + found.shift }, end: { line: found.startLine + lineCount, column: end.column + found.endShift } };
      if (!carried.has(found.pattern)) carried.set(found.pattern, []);
      carried.get(found.pattern).push({ ...mutant, location });
      printReview("CARRIED", label, found.pattern, location.start.line, location.end.line, mutant.reason);
    }
  }

  console.log(`\n${moved} moved mutants (${acrossFiles} into another file), ${reviewed} to review, ${blocked.length} that cannot be carried.`);
  for (const message of blocked) console.log(`BLOCKED ${message}`);
  if (!options.write) return;
  assertCondition(blocked.length === 0 || options["drop-blocked"], "Refusing to write while some mutants cannot be carried; pass --drop-blocked to delete them.");

  // Keep the policy's file order, append files that received their first carried mutant in scope order, and leave out
  // a file with no reviewed mutant left (an entry needs at least one).
  const order = [...policy.files.map((file) => file.pattern), ...scope.filter((pattern) => !policy.files.some((file) => file.pattern === pattern))];
  const files = order.filter((pattern) => (carried.get(pattern) ?? []).length > 0).map((pattern) => {
    const mutants = carried.get(pattern);
    const keys = new Set(mutants.map((mutant) => JSON.stringify([mutant.mutatorName, mutant.replacement, mutant.location])));
    assertCondition(keys.size === mutants.length, `${pattern}: two carried mutants share a location; edit the entries by hand.`);
    return { pattern, sourceSha256: sourceHash(readCurrent(mutationSource(pattern))), mutants };
  });
  fs.writeFileSync(policyPath, serialize({ ...policy, files }));
  console.log(`Wrote moved locations, carried entries and source hashes${blocked.length ? `; dropped ${blocked.length} entries` : ""}.`);
});
