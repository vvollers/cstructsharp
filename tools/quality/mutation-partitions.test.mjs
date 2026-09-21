/** Checks complete mutation partitioning and lossless aggregation. Usage: node --test tools/quality/mutation-partitions.test.mjs. */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { aggregateMutationPartitions, planMutationPartitions } from "../lib/mutation-partitions.mjs";
import { repositoryRoot } from "../lib/tooling.mjs";

// A step timeout must leave ordinary setup/upload headroom; incomplete evidence must still fail aggregation.
test("mutation jobs reserve time to upload diagnostics after the execution limit", () => {
  const lines = fs.readFileSync(path.join(repositoryRoot, ".github/workflows/mutation.yml"), "utf8").split(/\r?\n/);
  for (const job of ["permanent", "memory"]) {
    const start = lines.indexOf(`  ${job}:`);
    assert.ok(start >= 0, `${job} job is required`);
    // Only a two-space key begins the next job; nested steps and matrices belong to the current body.
    const next = lines.findIndex((line, index) => index > start && /^ {2}\S/.test(line));
    const body = lines.slice(start, next < 0 ? lines.length : next).join("\n");
    const jobMinutes = Number(body.match(/^ {4}timeout-minutes: (\d+)$/m)?.[1]);
    const stepMinutes = Number(body.match(/^ {6}- name: Mutate[^\n]*\n {8}timeout-minutes: (\d+)$/m)?.[1]);
    assert.equal(stepMinutes, 180, "Keep the complete mutation execution budget");
    assert.ok(jobMinutes >= stepMinutes + 15, `${job} needs setup/upload headroom`);
    assert.match(body, /^ {6}- name: Retain[^\n]*\n {8}if: always\(\)$/m);
  }
});

/** Creates two independent synthetic Stryker reports whose local test and mutant IDs deliberately collide. */
function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-mutation-partitions-"));
  // Remove only this test's uniquely created fixture directory after all assertions finish.
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.mkdirSync(path.join(root, "src/CStructSharp"), { recursive: true });
  fs.writeFileSync(path.join(root, "src/CStructSharp/A.cs"), "source A");
  fs.writeFileSync(path.join(root, "src/CStructSharp/B.cs"), "source B");
  const plan = planMutationPartitions(root, { mutate: ["A.cs", "B.cs"] }, 2);
  // Each partition has one real mutant and its own test; aggregation must retain both relationships.
  const reports = plan.map((partition) => ({ id: partition.id, report: {
    schemaVersion: "2", thresholds: { high: 75, low: 75 },
    files: { [partition.files[0].source]: { source: fs.readFileSync(path.join(root, partition.files[0].source), "utf8"),
      mutants: [{ id: "0", status: "Killed", killedBy: ["1"], coveredBy: ["1"] }] } },
    testFiles: { "test.cs": { tests: [{ id: "1", name: "Assertion" }] } },
  } }));
  return { root, plan, reports };
}

/** Returns the single selected source entry of a synthetic report. */
function selectedFile(report) {
  return Object.values(report.report.files)[0];
}

// Test against the real allowlist, so new scheduling code cannot silently trim any of the reviewed 71 files.
test("the repository plan includes every permanent source exactly once", () => {
  const config = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "stryker-config.json")))["stryker-config"];
  const plan = planMutationPartitions(repositoryRoot, config);
  assert.equal(plan.length, 16);
  const patterns = plan.flatMap((partition) => partition.files.map((file) => file.pattern));
  assert.equal(patterns.length, 71);
  assert.deepEqual([...patterns].sort(), [...config.mutate].sort());
  assert.equal(new Set(patterns).size, 71);
  assert.ok(plan.every((partition) => partition.files.length > 0));
});

// Local IDs are reused by independent Stryker processes, so both sides of test references need namespacing.
test("aggregation preserves unique mutant IDs and killedBy/coveredBy evidence", (t) => {
  const f = fixture(t);
  const report = aggregateMutationPartitions(f.root, f.plan, f.reports);
  assert.equal(Object.keys(report.files).length, 2);
  const mutants = Object.values(report.files).flatMap((file) => file.mutants);
  const tests = Object.values(report.testFiles).flatMap((file) => file.tests);
  assert.equal(new Set(mutants.map((mutant) => mutant.id)).size, 2);
  assert.equal(new Set(tests.map((entry) => entry.id)).size, 2);
  for (const mutant of mutants) {
    assert.deepEqual(mutant.killedBy, mutant.coveredBy);
    assert.ok(tests.some((entry) => entry.id === mutant.killedBy[0]));
  }
});

// All matrix jobs are mandatory even when the reports present happen to have perfect scores.
test("missing and duplicate partition reports fail", (t) => {
  const f = fixture(t);
  assert.throws(() => aggregateMutationPartitions(f.root, f.plan, f.reports.slice(1)), /Missing or extra/);
  assert.throws(() => aggregateMutationPartitions(f.root, f.plan, [f.reports[0], f.reports[0]]), /Duplicate mutation partition/);
});

// No configured source can disappear behind a complete-looking partition manifest.
test("missing sources and unexpected tested sources fail", (t) => {
  const f = fixture(t);
  f.reports[0].report.files = {};
  assert.throws(() => aggregateMutationPartitions(f.root, f.plan, f.reports), /Missing configured files/);
  f.reports[0].report.files["src/CStructSharp/Other.cs"] = { source: "other", mutants: [{ id: "5", status: "Killed" }] };
  assert.throws(() => aggregateMutationPartitions(f.root, f.plan, f.reports), /outside p00/);
});

// Source identity prevents a passing old report from qualifying newly edited code.
test("changed source and thresholds fail", (t) => {
  const f = fixture(t);
  selectedFile(f.reports[0]).source = "older source";
  assert.throws(() => aggregateMutationPartitions(f.root, f.plan, f.reports), /Report source changed/);
  f.reports[0].report.thresholds.low = 70;
  assert.throws(() => aggregateMutationPartitions(f.root, f.plan, f.reports), /Wrong thresholds/);
});

// Keep survivors for the canonical gate to reject; never convert compile errors into detected mutations.
test("aggregation retains failed and compiler-rejected outcomes unchanged", (t) => {
  const f = fixture(t);
  selectedFile(f.reports[0]).mutants = [{ id: "0", status: "Survived" }, { id: "1", status: "CompileError" }];
  const report = aggregateMutationPartitions(f.root, f.plan, f.reports);
  assert.deepEqual(Object.values(report.files)[0].mutants.map((mutant) => mutant.status), ["Survived", "CompileError"]);
});

// A cancelled report or a missing killing-test reference cannot count as finished evidence.
test("unfinished mutations and unknown test references fail", (t) => {
  const f = fixture(t);
  selectedFile(f.reports[0]).mutants[0].status = "NotRun";
  assert.throws(() => aggregateMutationPartitions(f.root, f.plan, f.reports), /Incomplete or unknown/);
  selectedFile(f.reports[0]).mutants[0].status = "Killed";
  selectedFile(f.reports[0]).mutants[0].killedBy = ["missing"];
  assert.throws(() => aggregateMutationPartitions(f.root, f.plan, f.reports), /Unknown test/);
});

// The original complete-scope score validator remains authoritative after the orchestration refactor.
test("a complete but low-scoring report fails the unchanged canonical gate", (t) => {
  const f = fixture(t);
  const config = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "stryker-config.json")))["stryker-config"];
  const plan = planMutationPartitions(repositoryRoot, config);
  const files = {};
  for (const partition of plan) {
    for (const file of partition.files) files[file.source] = { source: "test fixture", mutants: [{ id: file.source, status: "Survived" }] };
  }
  const reportPath = path.join(f.root, "low-score.json");
  fs.writeFileSync(reportPath, JSON.stringify({ schemaVersion: "2", thresholds: { high: 75, low: 75 }, files,
    testFiles: { "test.cs": { tests: [{ id: "test", name: "BehaviorAssertion" }] } } }));
  const result = spawnSync(process.execPath, ["tools/quality/mutation-report.mjs", "--report-path", reportPath], { cwd: repositoryRoot, encoding: "utf8" });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr + result.stdout, /score 0.00% is below the 75%/);
});

// A successful download command with no artifacts must not yield a vacuously successful aggregate.
test("the aggregate command rejects an empty artifact directory", (t) => {
  const f = fixture(t);
  const input = path.join(f.root, "empty-artifacts");
  fs.mkdirSync(input);
  const result = spawnSync(process.execPath, ["tools/quality/mutation-partitions.mjs", "--mode", "aggregate", "--input-directory", input,
    "--output-directory", path.join(f.root, "aggregate")], { cwd: repositoryRoot, encoding: "utf8" });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr + result.stdout, /Missing or extra mutation artifacts/);
  assert.ok(!fs.existsSync(path.join(f.root, "aggregate/mutation-report.json")));
});
