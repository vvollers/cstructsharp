/** Tests development performance reporting, stale input protection and bounded CLI options. Usage: node --test tools/quality/perf-check.test.mjs */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import test from "node:test";
import { optionsFor } from "./perf-check.mjs";
import {
  inventory,
  sourceIdentity,
  validateBundle,
  validateFixtures,
} from "../lib/perf-bundles.mjs";
import { median, compareRuns, readRun, renderReport } from "../lib/perf-report.mjs";

/** Creates an automatically removed temporary directory for a test's own files. */
function temporary(t) {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-perf-test-"));
  // The path is the exact directory just returned by mkdtemp, not an externally supplied deletion target.
  t.after(() => fs.rmSync(dir, { recursive: true, force: true }));
  return dir;
}

/** Constructs independent synthetic run records to exercise decisions without a machine-dependent timing gate. */
function run(value, environment = { protocol: 1, selectedCpu: 4 }, id = "Fixture.Parse") {
  return {
    environment,
    cases: [
      {
        id,
        type: "Fixture",
        method: "Parse",
        median: value,
        samples: [value, value, value],
        spread: 0,
        allocated: 64,
      },
    ],
  };
}

/** Writes a minimal complete full BDN report with a deliberately filtered Statistics median. */
function writeReport(dir) {
  fs.mkdirSync(path.join(dir, "results"));
  fs.writeFileSync(path.join(dir, "development-environment.json"), JSON.stringify({ protocol: 1 }));
  const c = {
    FullName: "Fixture.Parse",
    Type: "Fixture",
    Method: "Parse",
    Statistics: { Median: 1 },
    Memory: { BytesAllocatedPerOperation: 0 },
    Measurements: [
      { IterationMode: "Workload", IterationStage: "Actual", Nanoseconds: 100, Operations: 10 },
      { IterationMode: "Workload", IterationStage: "Actual", Nanoseconds: 300, Operations: 10 },
      { IterationMode: "Workload", IterationStage: "Result", Nanoseconds: 10, Operations: 10 },
    ],
  };
  fs.writeFileSync(
    path.join(dir, "results/case-report-full.json"),
    JSON.stringify({ Benchmarks: [c] }),
  );
  return c;
}

// Short timing samples must never be presented as proof of an improvement, regression, or equivalence.
test("screening retains a possible change and calls no signal inconclusive", () => {
  const report = compareRuns([run(100)], [run(110)], 0.03, false);
  assert.equal(report.rows[0].timing, "possible slowdown; confirm");
  assert.equal(
    compareRuns([run(100)], [run(100)], 0.03, false).rows[0].timing,
    "inconclusive (within margin)",
  );
  assert.throws(() => median([]), /Missing/);
  const text = renderReport({
    ...report,
    mode: "Screen",
    rounds: 1,
    threshold: 0.03,
    environment: { selectedCpu: 4 },
    setupSeconds: 0,
    measureSeconds: 1,
  });
  assert.match(text, /not a regression verdict/);
  assert.doesNotMatch(text, /\| regressed \||\| improved \||\| ok \|/);
});

// Taking a smallest median would hide a contradictory launch; every launch must influence interpretation.
test("confirmation preserves launch variation and contradictory directions", () => {
  const report = compareRuns(
    [run(100), run(100), run(100)],
    [run(110), run(90), run(115)],
    0.03,
    true,
  );
  assert.match(report.rows[0].timing, /inconclusive/);
  assert.equal(report.rows[0].deltas.length, 3);
  assert.equal(report.rows[0].afterMedians[1], 90);
  assert.match(
    compareRuns([run(100), run(100), run(100)], [run(110), run(110), run(110)], 0.03, true).rows[0]
      .timing,
    /repeatable slowdown; confirm/,
  );
});

// Process bias can move an unchanged control while other operations appear to change consistently.
test("canary drift blocks all timing direction labels", () => {
  const before = run(100),
    after = run(110);
  before.cases[0].method = after.cases[0].method = "HandWritten_PrimRecord";
  const report = compareRuns([before], [after], 0.03, false);
  assert.equal(report.canary, "drift");
  assert.equal(report.rows[0].timing, "inconclusive (canary drift)");
});

// A new/missing case or different CPU is not an equivalent-work comparison.
test("coverage and runtime mismatches fail closed", () => {
  assert.throws(
    () => compareRuns([run(100)], [run(100, undefined, "Fixture.Other")], 0.03, false),
    /coverage/,
  );
  assert.throws(
    () => compareRuns([run(100)], [run(100, { protocol: 1, selectedCpu: 5 })], 0.03, false),
    /affinity/,
  );
  assert.throws(() => compareRuns([], [], 0.03, false), /launch count/);
});

// Reporting must consume Actual samples, not filtered Statistics values or duplicate Result iterations.
test("full reports retain slow actual samples and reject absent allocation diagnostics", (t) => {
  const dir = temporary(t);
  const c = writeReport(dir);
  assert.equal(readRun(dir, 2).cases[0].median, 20);
  assert.equal(readRun(dir, 2).cases[0].allocated, 0);
  assert.throws(() => readRun(dir, 3), /Incomplete timing/);
  delete c.Memory;
  fs.writeFileSync(
    path.join(dir, "results/case-report-full.json"),
    JSON.stringify({ Benchmarks: [c] }),
  );
  assert.throws(() => readRun(dir, 2), /allocation diagnostics/);
});

// Allocations are a separate dimension, including when timing stays inside the practical margin.
test("allocation changes remain visible when CPU time does not change", () => {
  const a = run(100),
    b = run(100);
  b.cases[0].allocated = 120;
  const row = compareRuns([a], [b], 0.03, false).rows[0];
  assert.equal(row.allocationSignal, true);
  assert.deepEqual(row.allocationDeltas, [56]);
  assert.match(row.timing, /inconclusive/);
});

// A binary or fixture can be stale even if its filename and timestamps look unchanged.
test("bundle validation rejects replaced or additional files and differing fixture inputs", (t) => {
  const dir = temporary(t);
  fs.mkdirSync(path.join(dir, "host"));
  fs.mkdirSync(path.join(dir, "fixtures"));
  fs.writeFileSync(path.join(dir, "host/CStructSharp.Benchmarks.dll"), "test payload");
  fs.writeFileSync(path.join(dir, "fixtures/manifest.json"), "{}");
  // The test manifest follows the same content-addressed format without invoking a compiler.
  const files = inventory(dir).map((name) => ({
    name,
    sha256: crypto
      .createHash("sha256")
      .update(fs.readFileSync(path.join(dir, name)))
      .digest("hex"),
  }));
  const manifest = {
    schemaVersion: 1,
    source: { digest: "test" },
    fixtureVerification: "passed",
    files,
  };
  fs.writeFileSync(path.join(dir, "bundle.json"), JSON.stringify(manifest));
  validateBundle(dir);
  fs.writeFileSync(path.join(dir, "host/extra.dll"), "unexpected");
  assert.throws(() => validateBundle(dir), /file list/);
  fs.unlinkSync(path.join(dir, "host/extra.dll"));
  fs.writeFileSync(path.join(dir, "host/CStructSharp.Benchmarks.dll"), "different");
  assert.throws(() => validateBundle(dir), /content changed/);
  const changed = structuredClone(manifest);
  changed.files[0].sha256 = "different fixture hash";
  assert.throws(() => validateFixtures(manifest, changed), /Fixture inputs differ/);
});

// Source hashing must see untracked changes, deletions, SDK changes and changes with restored mtimes.
test("source identity detects edits independently of file timestamps", (t) => {
  const dir = temporary(t);
  execFileSync("git", ["init", "--quiet", dir]);
  fs.mkdirSync(path.join(dir, "src"));
  const file = path.join(dir, "src/Example.cs");
  fs.writeFileSync(file, "before");
  execFileSync("git", ["-C", dir, "add", "."]);
  execFileSync("git", [
    "-C",
    dir,
    "-c",
    "user.name=Perf Test",
    "-c",
    "user.email=perf@example.invalid",
    "commit",
    "--quiet",
    "-m",
    "fixture",
  ]);
  const initial = sourceIdentity(dir, "sdk-a");
  const stat = fs.statSync(file);
  fs.writeFileSync(file, "after");
  fs.utimesSync(file, stat.atime, stat.mtime);
  assert.notEqual(sourceIdentity(dir, "sdk-a").digest, initial.digest);
  fs.writeFileSync(file, "before");
  assert.equal(sourceIdentity(dir, "sdk-a").digest, initial.digest);
  assert.notEqual(sourceIdentity(dir, "sdk-b").digest, initial.digest);
  fs.writeFileSync(path.join(dir, "src/Added.cs"), "new input");
  assert.notEqual(sourceIdentity(dir, "sdk-a").digest, initial.digest);
  fs.unlinkSync(file);
  // A tracked deletion remains represented as a missing input in the manifest.
  assert.equal(
    sourceIdentity(dir, "sdk-a").files.find((entry) => entry.name === "src/Example.cs").sha256,
    null,
  );
});

// Invalid options must fail before spending minutes on an accidental unfiltered confirmation or creating paths.
test("CLI rejects unbounded work and unsafe output labels", () => {
  assert.throws(() => optionsFor(["--baseline", "before", "--confirm"]), /explicit --filter/);
  assert.throws(() => optionsFor(["--baseline", "before", "--rounds", "0"]), /Rounds/);
  assert.throws(() => optionsFor(["--capture", "../escape"]), /simple name/);
  assert.throws(() => optionsFor(["--baseline", "before", "--cpu", "64"]), /CPU/);
  assert.equal(optionsFor(["--baseline", "before"]).cpu, "auto");
  assert.equal(optionsFor(["--baseline", "before", "--confirm", "--filter", "*Parse*"]).rounds, 3);
});
