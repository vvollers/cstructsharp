/** Reviews exact behavior-equivalent mutations without altering their raw Stryker status or score. */
import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { mutationSource } from "./mutation-partitions.mjs";

/** Identifies a mutation by its operator, source span and replacement, never by a run-local numeric identifier. */
export function equivalentMutationKey(mutant) {
  assert.ok(typeof mutant.mutatorName === "string" && mutant.mutatorName.trim(), "Missing mutation operator");
  assert.equal(typeof mutant.replacement, "string", "Missing mutation replacement");
  const { start, end } = mutant.location ?? {};
  for (const point of [start, end]) {
    assert.ok(Number.isInteger(point?.line) && point.line > 0 && Number.isInteger(point?.column) && point.column >= 0, "Invalid mutation source location");
  }
  assert.ok(end.line > start.line || (end.line === start.line && end.column >= start.column), "Reversed mutation source location");
  return JSON.stringify([mutant.mutatorName, start.line, start.column, end.line, end.column, mutant.replacement]);
}

/** Loads individually justified mutations pinned to the current source and Stryker version, retaining every file. */
export function loadEquivalentMutations(root, configuredFiles) {
  const policy = JSON.parse(fs.readFileSync(path.join(root, "contracts/quality/mutation-equivalents.json"), "utf8"));
  const tools = JSON.parse(fs.readFileSync(path.join(root, ".config/dotnet-tools.json"), "utf8"));
  assert.equal(policy.schemaVersion, 1);
  assert.equal(policy.strykerVersion, tools.tools["dotnet-stryker"].version, "Review equivalent mutations after a Stryker version change");
  assert.ok(Array.isArray(policy.files));
  const files = new Map();
  for (const entry of policy.files) {
    assert.ok(configuredFiles.includes(entry.pattern), `Equivalent mutation is outside the configured scope: ${entry.pattern}`);
    assert.ok(!files.has(entry.pattern), `Duplicate equivalent mutation file: ${entry.pattern}`);
    const source = fs.readFileSync(path.join(root, mutationSource(entry.pattern)), "utf8").replaceAll("\r\n", "\n");
    assert.equal(crypto.createHash("sha256").update(source).digest("hex"), entry.sourceSha256, `Review changed equivalent-mutation source: ${entry.pattern}`);
    assert.ok(Array.isArray(entry.mutants) && entry.mutants.length > 0, "An equivalence file requires individual reviewed mutations");
    const mutants = new Map();
    for (const mutant of entry.mutants) {
      assert.ok(typeof mutant.reason === "string" && mutant.reason.trim(), "An equivalent mutation requires a reviewed reason");
      const key = equivalentMutationKey(mutant);
      assert.ok(!mutants.has(key), `Duplicate equivalent mutation: ${entry.pattern}`);
      mutants.set(key, mutant.reason);
    }
    files.set(entry.pattern, { source, mutants });
  }
  return files;
}

/** Returns reviewed surviving mutations only; missing coverage, runtime errors and compile failures never qualify. */
export function qualifyEquivalentMutations(files, pattern, reportedFile) {
  const reviewed = files.get(pattern);
  if (!reviewed) return [];
  assert.equal(typeof reportedFile?.source, "string", `Missing equivalent-mutation source: ${pattern}`);
  assert.equal(reportedFile.source.replaceAll("\r\n", "\n"), reviewed.source, `Equivalent-mutation report source changed: ${pattern}`);
  assert.ok(Array.isArray(reportedFile.mutants), `Missing mutation array: ${pattern}`);
  const qualified = [];
  const seen = new Set();
  for (const mutant of reportedFile.mutants) {
    if (mutant.status !== "Survived") continue;
    const key = equivalentMutationKey(mutant);
    const reason = reviewed.mutants.get(key);
    if (!reason) continue;
    assert.ok(!seen.has(key), `Duplicate equivalent mutation in report: ${pattern}`);
    seen.add(key);
    qualified.push({ pattern, id: mutant.id, reason });
  }
  return qualified;
}
