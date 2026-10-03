/** Checks development/full test selection, result integrity, and bounded failure handling. Usage: node --test tools/quality/test-managed.test.mjs */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { runBounded, testEvidence, testPlan } from "../lib/managed-tests.mjs";
import { findAll, parseXml } from "../lib/xml.mjs";
import { repositoryRoot } from "../lib/tooling.mjs";

// Development selection is explicit, while the full command restores every normal suite and framework.
test("development and full plans preserve their promised coverage", () => {
  const dev = testPlan([], 32);
  assert.deepEqual(
    dev.runs.map((run) => run.name),
    ["runtime-net10.0"],
  );
  assert.match(dev.runs[0].filter, /TestCategory!=Extended/);
  const full = testPlan(["--full"], 32);
  assert.deepEqual(full.runs.map((run) => run.name).sort(), [
    "compiler-net10.0",
    "generator-net10.0",
    "parity-net10.0",
    "parity-net8.0",
    "runtime-net10.0",
    "runtime-net8.0",
  ]);
  for (const run of full.runs) assert.equal(run.filter, "TestCategory!=OptIn");
  const generator = testPlan(["--suite", "generator"], 32);
  assert.deepEqual(
    generator.suites.map((suite) => suite.name),
    ["generator", "compiler", "parity"],
  );
});

// A caller's OR expression must not escape the development exclusions through operator precedence.
test("focused filters compose with the profile and concurrency is bounded", () => {
  const plan = testPlan(["--filter", "FullyQualifiedName~Reader|FullyQualifiedName~Writer"], 2);
  assert.equal(
    plan.runs[0].filter,
    "(TestCategory!=OptIn&TestCategory!=Extended)&(FullyQualifiedName~Reader|FullyQualifiedName~Writer)",
  );
  assert.equal(plan.options.jobs, 1);
  assert.equal(plan.options.workers, 2);
  assert.equal(testPlan([], 32).options.workers, 8);
  assert.throws(() => testPlan(["--jobs", "0"]), /Jobs/);
  assert.throws(() => testPlan(["--workers", "33"]), /Workers/);
  assert.throws(() => testPlan(["--suite", "unknown"]), /Suite/);
  assert.throws(() => testPlan(["--filter", ""]), /Filter/);
});

// The native settings offer the same development selection without changing ordinary dotnet test defaults.
test("runsettings match the development profile and default runs still include Extended", () => {
  const settings = parseXml(
    fs.readFileSync(path.join(repositoryRoot, "tests/development.runsettings"), "utf8"),
  );
  assert.equal(findAll(settings, "TestCaseFilter")[0].text, testPlan([], 32).runs[0].filter);
  const defaults = parseXml(
    fs.readFileSync(
      path.join(repositoryRoot, "tests/CStructSharpTests/default.runsettings"),
      "utf8",
    ),
  );
  assert.equal(findAll(defaults, "TestCaseFilter")[0].text, "TestCategory!=OptIn");
});

// A success exit code alone is insufficient when a mistyped filter selects no tests or results are incomplete.
test("TRX evidence rejects empty, skipped, failed and missing cases", (t) => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-test-evidence-"));
  // Delete only the exact temporary directory allocated for this test.
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const file = path.join(directory, "tests.trx");
  fs.writeFileSync(
    file,
    '<TestRun><ResultSummary><Counters total="1" passed="1" /></ResultSummary><Results><UnitTestResult testName="Example" outcome="Passed" duration="00:00:01.25" /></Results></TestRun>',
  );
  assert.deepEqual(testEvidence(file), { count: 1, slowest: [{ name: "Example", seconds: 1.25 }] });
  for (const contents of [
    '<TestRun><Counters total="0" passed="0" /></TestRun>',
    '<TestRun><Counters total="2" passed="2" /><UnitTestResult outcome="Passed" /></TestRun>',
    '<TestRun><Counters total="1" passed="0" /><UnitTestResult outcome="NotExecuted" /></TestRun>',
    '<TestRun><Counters total="1" passed="0" /><UnitTestResult outcome="Failed" /></TestRun>',
  ]) {
    fs.writeFileSync(file, contents);
    assert.throws(() => testEvidence(file), /Incomplete/);
  }
  fs.writeFileSync(
    file,
    '<TestRun><Counters total="1" passed="1" /><UnitTestResult outcome="Passed" duration="invalid" /></TestRun>',
  );
  assert.throws(() => testEvidence(file), /Invalid test duration/);
});

// Failure must finish already-started hosts but must not launch more queued tests or claim all work completed.
test("bounded execution stops queued work and waits for active work after failure", async () => {
  let release;
  const blocked = new Promise((resolve) => {
    release = resolve;
  });
  const started = [];
  // One active task is released by the failure of its peer, reproducing overlapping test hosts deterministically.
  const outcome = await runBounded(
    [{ name: "first" }, { name: "failure" }, { name: "must-not-start" }],
    2,
    async (task) => {
      started.push(task.name);
      if (task.name === "failure") {
        release();
        throw new Error("test failure");
      }
      await blocked;
      return { name: task.name };
    },
  );
  assert.deepEqual(started, ["first", "failure"]);
  assert.equal(outcome.failed, true);
  assert.equal(outcome.completed, 2);
  assert.equal(outcome.results.find((result) => result.name === "failure").error, "test failure");
});
