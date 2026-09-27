/** Tests exact equivalent-mutation qualification. Usage: node --test tools/quality/mutation-equivalents.test.mjs. */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { loadEquivalentMutations, qualifyEquivalentMutations } from "../lib/mutation-equivalents.mjs";
import { mutationSource } from "../lib/mutation-partitions.mjs";
import { repositoryRoot } from "../lib/tooling.mjs";

// A reviewed proof from the real policy: the first file under the runtime root that still has one, so dropping one
// file's proofs during a refactor does not break these fixtures.
const SAMPLE = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "contracts/quality/mutation-equivalents.json"), "utf8"))
  .files.find((file) => file.mutants.length > 0 && !file.pattern.includes("/")).pattern;
const SAMPLE_SOURCE = mutationSource(SAMPLE);

/** Copies only the policy and its exact source/tool dependencies into a disposable isolated fixture. */
function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-mutation-equivalents-"));
  // Remove only this test's uniquely allocated fixture directory.
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  for (const filename of ["contracts/quality/mutation-equivalents.json", ".config/dotnet-tools.json", SAMPLE_SOURCE]) {
    fs.mkdirSync(path.dirname(path.join(root, filename)), { recursive: true });
    fs.copyFileSync(path.join(repositoryRoot, filename), path.join(root, filename));
  }
  const policyPath = path.join(root, "contracts/quality/mutation-equivalents.json");
  const policy = JSON.parse(fs.readFileSync(policyPath, "utf8"));
  // Isolate the sample file's proofs so later production policy additions do not broaden this unit fixture.
  policy.files = policy.files.filter((file) => file.pattern === SAMPLE);
  fs.writeFileSync(policyPath, JSON.stringify(policy));
  const sourcePath = path.join(root, SAMPLE_SOURCE);
  const source = fs.readFileSync(sourcePath, "utf8");
  return { root, policyPath, policy, sourcePath, source, report: { source,
    mutants: [{ ...policy.files[0].mutants[0], id: "run-local-id", status: "Survived" }] } };
}

// Qualification is metadata only: the input report and raw surviving status stay intact.
test("exact reviewed survivors qualify without mutating the report or depending on numeric IDs", (t) => {
  const f = fixture(t);
  const files = loadEquivalentMutations(f.root, [SAMPLE]);
  const before = structuredClone(f.report);
  assert.equal(qualifyEquivalentMutations(files, SAMPLE, f.report).length, 1);
  assert.deepEqual(f.report, before);
  f.report.mutants[0].id = "another-partition:123";
  assert.equal(qualifyEquivalentMutations(files, SAMPLE, f.report).length, 1);
  assert.deepEqual(qualifyEquivalentMutations(files, "Other.cs", f.report), []);
});

// A similar operator or nearby location cannot inherit another mutation's proof.
test("changed replacements, locations and operators remain unexplained", (t) => {
  const f = fixture(t);
  const files = loadEquivalentMutations(f.root, [SAMPLE]);
  for (const change of [
    { replacement: "false" }, { mutatorName: "Different operator" },
    { location: { start: { line: 50, column: 14 }, end: { line: 50, column: 52 } } },
  ]) {
    const report = structuredClone(f.report);
    Object.assign(report.mutants[0], change);
    assert.deepEqual(qualifyEquivalentMutations(files, SAMPLE, report), []);
  }
});

// No coverage, runtime errors and incomplete/invalid runs are not evidence of equivalent behavior.
test("only survived status qualifies and duplicate report mutations fail closed", (t) => {
  const f = fixture(t);
  const files = loadEquivalentMutations(f.root, [SAMPLE]);
  for (const status of ["Killed", "Timeout", "NoCoverage", "RuntimeError", "CompileError", "Ignored", "NotRun"]) {
    const report = structuredClone(f.report);
    report.mutants[0].status = status;
    assert.deepEqual(qualifyEquivalentMutations(files, SAMPLE, report), []);
  }
  f.report.mutants.push({ ...f.report.mutants[0], id: "duplicate" });
  assert.throws(() => qualifyEquivalentMutations(files, SAMPLE, f.report), /Duplicate equivalent mutation in report/);
});

// Both the checkout and submitted report must match the reviewed source, normalizing checkout line endings only.
test("stale source or report cannot reuse a proof", (t) => {
  const f = fixture(t);
  fs.writeFileSync(f.sourcePath, f.source.replaceAll("\n", "\r\n"));
  const files = loadEquivalentMutations(f.root, [SAMPLE]);
  f.report.source += "// new code\n";
  assert.throws(() => qualifyEquivalentMutations(files, SAMPLE, f.report), /report source changed/);
  fs.writeFileSync(f.sourcePath, f.report.source);
  assert.throws(() => loadEquivalentMutations(f.root, [SAMPLE]), /Review changed/);
});

// Policy mistakes fail before any scores or qualifications are calculated.
test("tool changes, out-of-scope sources, duplicate entries and missing reasons fail closed", (t) => {
  const f = fixture(t);
  assert.throws(() => loadEquivalentMutations(f.root, []), /outside the configured scope/);
  for (const variant of ["version", "file", "mutant", "reason"]) {
    const policy = structuredClone(f.policy);
    if (variant === "version") policy.strykerVersion = "different";
    if (variant === "file") policy.files.push(policy.files[0]);
    if (variant === "mutant") policy.files[0].mutants.push(policy.files[0].mutants[0]);
    if (variant === "reason") policy.files[0].mutants[0].reason = " ";
    fs.writeFileSync(f.policyPath, JSON.stringify(policy));
    assert.throws(() => loadEquivalentMutations(f.root, [SAMPLE]), undefined, variant);
  }
});
