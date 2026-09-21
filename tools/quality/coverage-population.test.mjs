/** Tests exact compile-time population qualification and the runtime risk gate. Run: node --test tools/quality/coverage-population.test.mjs. */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { coverageFileHash, declarationSourceHash, qualifyCompileTimeDeclarations } from "../lib/coverage-population.mjs";
import { repositoryRoot } from "../lib/tooling.mjs";

/** Creates isolated evidence with three passing suites and one explicitly qualified metadata declaration. */
function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-coverage-policy-"));
  // Each test owns this exact temporary directory and removes it after assertions finish.
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.mkdirSync(path.join(root, "src/Library"), { recursive: true });
  fs.writeFileSync(path.join(root, "src/Library/Metadata.cs"), "reviewed declaration");
  const coverage = path.join(root, "coverage.cobertura.xml");
  fs.writeFileSync(coverage, "merged report bytes");
  const suites = [];
  for (const name of ["runtime", "parity", "generator"]) {
    fs.writeFileSync(path.join(root, `${name}.json`), "{}");
    fs.writeFileSync(path.join(root, `${name}.trx`), '<TestRun><UnitTestResult testName="MetadataWorks (inline)" outcome="Passed" /></TestRun>');
    suites.push({ name, report: `${name}.json`, sha256: coverageFileHash(path.join(root, `${name}.json`)),
      tests: `${name}.trx`, testsSha256: coverageFileHash(path.join(root, `${name}.trx`)) });
  }
  const collection = { schemaVersion: 1, coverageSha256: coverageFileHash(coverage), suites };
  const policy = { schemaVersion: 1, compileTimeDeclarations: [{ source: "src/Library/Metadata.cs",
    sha256: coverageFileHash(path.join(root, "src/Library/Metadata.cs")), requiredTests: ["MetadataWorks"], reason: "Metadata, not an executing algorithm" }] };
  const collectionPath = path.join(root, "collection.json");
  const policyPath = path.join(root, "policy.json");
  fs.writeFileSync(collectionPath, JSON.stringify(collection));
  fs.writeFileSync(policyPath, JSON.stringify(policy));
  return { root, coverage, collection, collectionPath, policy, policyPath };
}

/** Qualifies the isolated fixture without altering its measured filename list. */
function qualify(f, filenames = ["Library/Metadata.cs", "Library/Runtime.cs"]) {
  return qualifyCompileTimeDeclarations(f.root, f.policyPath, f.collectionPath, f.coverage, filenames);
}

// Only the exact reviewed declaration receives the separate coverage role.
test("compile-time qualification leaves runtime files in the runtime population", (t) => {
  const qualified = qualify(fixture(t));
  assert.deepEqual([...qualified.keys()], ["Library/Metadata.cs"]);
  assert.deepEqual(qualified.get("Library/Metadata.cs").requiredTests, ["MetadataWorks"]);
});

// New executable code cannot silently inherit a previous metadata-only classification.
test("changed source requires a fresh classification review", (t) => {
  const f = fixture(t);
  fs.appendFileSync(path.join(f.root, "src/Library/Metadata.cs"), "new logic");
  assert.throws(() => qualify(f), /review its compile-time-only classification/);
});

// Git's checkout newline conversion must not turn unchanged declaration text into a different population.
test("declaration identity normalizes checkout line endings only", (t) => {
  const f = fixture(t);
  const source = path.join(f.root, "src/Library/Metadata.cs");
  fs.writeFileSync(source, "first\nsecond\n");
  const lf = declarationSourceHash(source);
  fs.writeFileSync(source, "first\r\nsecond\r\n");
  assert.equal(declarationSourceHash(source), lf);
});

// A present but empty, skipped or failed test report is not successful execution evidence.
test("empty and non-passing suites fail qualification", (t) => {
  const f = fixture(t);
  for (const result of ["", '<UnitTestResult testName="MetadataWorks" outcome="Failed" />', '<UnitTestResult testName="MetadataWorks" outcome="NotExecuted" />']) {
    fs.writeFileSync(path.join(f.root, "generator.trx"), `<TestRun>${result}</TestRun>`);
    f.collection.suites[2].testsSha256 = coverageFileHash(path.join(f.root, "generator.trx"));
    fs.writeFileSync(f.collectionPath, JSON.stringify(f.collection));
    assert.throws(() => qualify(f), /No executed tests|Incomplete or failed tests/);
  }
});

// A generator-only or runtime-only report is not accepted as combined evidence.
test("every suite and its unchanged reports are mandatory", (t) => {
  const f = fixture(t);
  f.collection.suites.splice(1, 1);
  fs.writeFileSync(f.collectionPath, JSON.stringify(f.collection));
  assert.throws(() => qualify(f), /All three suites/);
});

// Passing unrelated generator tests cannot stand in for the required metadata-consumer tests.
test("specific generator cases must be present and passing", (t) => {
  const f = fixture(t);
  fs.writeFileSync(path.join(f.root, "generator.trx"), '<TestRun><UnitTestResult testName="OtherTest" outcome="Passed" /></TestRun>');
  f.collection.suites[2].testsSha256 = coverageFileHash(path.join(f.root, "generator.trx"));
  fs.writeFileSync(f.collectionPath, JSON.stringify(f.collection));
  assert.throws(() => qualify(f), /Missing passing generator evidence/);
});

// Reports copied from another run or modified after collection fail identity checks.
test("changed merged reports cannot reuse qualifications", (t) => {
  const f = fixture(t);
  fs.appendFileSync(f.coverage, "different report");
  assert.throws(() => qualify(f), /Merged coverage differs/);
});

// A missing declaration in coverage signals an incorrect population, not an opportunity to hide it.
test("missing or ambiguous measured files fail qualification", (t) => {
  const f = fixture(t);
  assert.throws(() => qualify(f, ["Library/Runtime.cs"]), /Expected one measured file/);
  assert.throws(() => qualify(f, ["Library/Metadata.cs", "/other/Library/Metadata.cs"]), /Expected one measured file/);
});

// Exercise the actual command: one uncovered runtime line must fail the zero-critical-files gate.
test("an uncovered critical runtime path fails the executable gate", (t) => {
  const f = fixture(t);
  fs.writeFileSync(f.coverage, '<coverage><packages><package><classes><class filename="Library/Runtime.cs"><lines><line number="1" hits="0" /></lines></class></classes></package></packages></coverage>');
  const result = spawnSync(process.execPath, ["tools/quality/coverage-risk.mjs", "--coverage-path", f.coverage,
    "--output-directory", path.join(f.root, "risk"), "--maximum-critical-risk-files", "0"], { cwd: repositoryRoot, encoding: "utf8" });
  assert.notEqual(result.status, 0);
  assert.match(result.stderr + result.stdout, /1 critical-risk files/);
  const report = JSON.parse(fs.readFileSync(path.join(f.root, "risk/coverage-risk.json")));
  assert.equal(report.summary.criticalRiskFiles, 1);
  assert.equal(report.files[0].coverageRole, "runtime");
  assert.equal(report.files[0].linesCovered, 0);
});
