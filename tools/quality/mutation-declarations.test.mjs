/** Tests exact non-mutable declaration accounting and unchanged full-scope gates. Usage: node --test tools/quality/mutation-declarations.test.mjs. */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { loadNonMutableDeclarations, qualifyNonMutableDeclaration } from "../lib/mutation-declarations.mjs";
import { mutationSource } from "../lib/mutation-partitions.mjs";
import { repositoryRoot } from "../lib/tooling.mjs";

/** Creates isolated policy/source files plus a complete synthetic report for testing the real validator. */
function fixture(t) {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-mutation-declarations-"));
  // Clean up only the uniquely created fixture after this test's assertions.
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  const config = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "stryker-config.json"), "utf8"))["stryker-config"];
  const declaration = "src/CStructSharp/Values/ReadAttempt.cs";
  for (const filename of ["contracts/quality/mutation-non-mutable.json", ".config/dotnet-tools.json", declaration]) {
    fs.mkdirSync(path.dirname(path.join(root, filename)), { recursive: true });
    fs.copyFileSync(path.join(repositoryRoot, filename), path.join(root, filename));
  }
  const files = {};
  for (const pattern of config.mutate) {
    const source = mutationSource(pattern);
    files[source] = { source: fs.readFileSync(path.join(repositoryRoot, source), "utf8"),
      mutants: source === declaration ? [] : [{ id: source, status: "Killed", killedBy: ["assertion"],
        mutatorName: "Test mutation", replacement: "false", location: { start: { line: 1, column: 0 }, end: { line: 1, column: 1 } } }] };
  }
  return { root, config, declaration, report: { schemaVersion: "2", thresholds: { high: 75, low: 75 }, files,
    testFiles: { "tests/CStructSharpTests/test.cs": { tests: [{ id: "assertion", name: "BehaviorAssertion" }] } } } };
}

/** Runs the actual CLI validator against this fixture's report and returns its exit status and output. */
function validate(f) {
  const reportPath = path.join(f.root, "report.json");
  fs.writeFileSync(reportPath, JSON.stringify(f.report));
  const result = spawnSync(process.execPath, ["tools/quality/mutation-report.mjs", "--report-path", reportPath], { cwd: repositoryRoot, encoding: "utf8" });
  return { status: result.status, output: result.stdout + result.stderr };
}

// The one reviewed record remains present without contributing an invented killed mutant to the score.
test("a complete report explicitly accounts for the exact non-mutable declaration", (t) => {
  const f = fixture(t);
  const result = validate(f);
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /70\/70 detected/);
  assert.match(result.output, /71 configured files; 1 reviewed non-mutable declarations/);
  assert.match(result.output, /Not applicable .*Values\/ReadAttempt.cs/);
});

// Equivalent survivors remain visible and lower the raw score instead of being relabeled as killed or ignored.
test("the real gate reports reviewed equivalent survivors with an unchanged raw denominator", (t) => {
  const f = fixture(t);
  const policy = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "contracts/quality/mutation-equivalents.json"), "utf8"));
  const reviewed = policy.files.find((file) => file.pattern === "MappedTypes.cs").mutants[0];
  f.report.files["src/CStructSharp/MappedTypes.cs"].mutants.push({ ...reviewed, id: "equivalent", status: "Survived" });
  const result = validate(f);
  assert.equal(result.status, 0, result.output);
  assert.match(result.output, /70\/71 detected \(98.59%\)/);
  assert.match(result.output, /1 survived/);
  assert.match(result.output, /Reviewed equivalent survivors: 1/);
  f.report.files["src/CStructSharp/MappedTypes.cs"].mutants[1].replacement = "false";
  assert.match(validate(f).output, /1 surviving mutants without reviewed equivalence/);
});

// An otherwise passing report cannot hide an unfinished mutation alongside completed ones.
test("the canonical gate rejects incomplete outcomes even in a measured file", (t) => {
  const f = fixture(t);
  f.report.files["src/CStructSharp/CStruct.cs"].mutants.push({ id: "unfinished", status: "NotRun" });
  assert.match(validate(f).output, /Incomplete or unknown mutation status/);
});

// Missing/duplicated sources cannot be treated as a declaration with no mutation opportunities.
test("missing and duplicate declaration report entries still fail", (t) => {
  const f = fixture(t);
  const entry = f.report.files[f.declaration];
  delete f.report.files[f.declaration];
  assert.notEqual(validate(f).status, 0);
  f.report.files[f.declaration] = entry;
  f.report.files[`/other/${f.declaration}`] = entry;
  assert.notEqual(validate(f).status, 0);
});

// Compiler errors, ignored mutants and absent arrays must not be reclassified as non-applicable declarations.
test("qualification rejects stale source and any nonempty or missing mutation array", (t) => {
  const f = fixture(t);
  const entry = f.report.files[f.declaration];
  for (const status of ["CompileError", "Ignored", "NotRun", "Survived", "NoCoverage", "RuntimeError"]) {
    entry.mutants = [{ id: "record", status }];
    assert.notEqual(validate(f).status, 0, status);
  }
  delete entry.mutants;
  assert.notEqual(validate(f).status, 0);
  entry.mutants = [];
  entry.source += "// Changed declaration\n";
  assert.match(validate(f).output, /Declaration report source changed/);
});

// Ordinary semantic files still need valid mutants, and surviving behavior fails even above the score floor.
test("ordinary empty reports, low scores and survivors retain their original failures", (t) => {
  const f = fixture(t);
  f.report.files["src/CStructSharp/CStruct.cs"].mutants = [];
  assert.match(validate(f).output, /produced no valid mutants/);
  f.report.files["src/CStructSharp/CStruct.cs"].mutants = [{ id: "one", status: "Survived" }];
  assert.match(validate(f).output, /1 surviving mutants/);
  for (const file of Object.values(f.report.files)) {
    for (const mutant of file.mutants) mutant.status = "Survived";
  }
  assert.match(validate(f).output, /score 0.00% is below the 75%/);
});

// The source and tool pins invalidate reviewed eligibility; hashes are normalized only for checkout line endings.
test("source changes, tool upgrades, duplicates and out-of-scope policy entries fail closed", (t) => {
  const f = fixture(t);
  const sourcePath = path.join(f.root, f.declaration);
  const source = fs.readFileSync(sourcePath, "utf8");
  fs.writeFileSync(sourcePath, source.replaceAll("\n", "\r\n"));
  const declarations = loadNonMutableDeclarations(f.root, f.config.mutate);
  assert.equal(qualifyNonMutableDeclaration(declarations, "Values/ReadAttempt.cs", f.report.files[f.declaration]), true);
  assert.equal(qualifyNonMutableDeclaration(declarations, "CStruct.cs", { mutants: [] }), false);
  fs.writeFileSync(sourcePath, `${source}// new code\n`);
  assert.throws(() => loadNonMutableDeclarations(f.root, f.config.mutate), /Review changed/);
  fs.writeFileSync(sourcePath, source);
  const policyPath = path.join(f.root, "contracts/quality/mutation-non-mutable.json");
  const policy = JSON.parse(fs.readFileSync(policyPath, "utf8"));
  policy.strykerVersion = "different";
  fs.writeFileSync(policyPath, JSON.stringify(policy));
  assert.throws(() => loadNonMutableDeclarations(f.root, f.config.mutate), /Stryker version change/);
  policy.strykerVersion = "5.0.0";
  policy.declarations.push(policy.declarations[0]);
  fs.writeFileSync(policyPath, JSON.stringify(policy));
  assert.throws(() => loadNonMutableDeclarations(f.root, f.config.mutate), /Duplicate non-mutable/);
  assert.throws(() => loadNonMutableDeclarations(f.root, []), /outside the configured scope/);
});
