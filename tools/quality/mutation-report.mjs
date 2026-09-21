#!/usr/bin/env node
/**
 * Validates the permanent mutation gate: the Stryker configuration (project, thresholds, the 71-file allowlist)
 * and a JSON report against it (every configured file measured or explicitly qualified as non-mutable,
 * no surviving/uncovered/runtime-error mutants,
 * score at or above 75 %).
 *
 *   node tools/quality/mutation-report.mjs --report-path <mutation-report.json> [--config-path stryker-config.json]
 */
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { loadNonMutableDeclarations, qualifyNonMutableDeclaration } from "../lib/mutation-declarations.mjs";

const options = parseArguments(process.argv.slice(2), { "config-path": "string", "report-path": "string" }, {
  defaults: { "config-path": path.join(repositoryRoot, "stryker-config.json") },
});
assertCondition(options["report-path"], "Option --report-path is required.");
const EXPORT_LIST_TEST = "CStructSharp.Tests.PublicApiSurfaceTests.ExportedTypesAndSignatures_AreDeliberateAndImplementationAgnostic";
const countStatus = (mutants, status) => mutants.filter((mutant) => String(mutant.status) === status).length;
const validCountOf = (mutants) => ["Killed", "Timeout", "Survived", "NoCoverage", "RuntimeError"].reduce((sum, status) => sum + countStatus(mutants, status), 0);

// Validate the full configured population before calculating scores; non-applicability is never a detected mutant.
await main(() => {
  const configPath = path.resolve(options["config-path"]);
  const reportPath = path.resolve(options["report-path"]);
  const config = JSON.parse(fs.readFileSync(configPath, "utf8"))["stryker-config"];
  const report = JSON.parse(fs.readFileSync(reportPath, "utf8"));

  assertCondition(String(config.project) === "src/CStructSharp/CStructSharp.csproj", "The permanent mutation gate must target only the core library project.");
  assertCondition(
    (config["test-projects"] ?? []).length === 1 && String(config["test-projects"][0]) === "tests/CStructSharpTests/CStructSharpTests.csproj",
    "The permanent mutation gate must use only the core test project.",
  );
  assertCondition(String(config["coverage-analysis"]) === "off", "Coverage-based test selection must remain disabled for the permanent mutation gate.");
  assertCondition(String(config["test-case-filter"]) === `FullyQualifiedName!=${EXPORT_LIST_TEST}`, "Only the uninstrumented assembly export-list check may be excluded from mutation runs.");
  const reporters = (config.reporters ?? []).map(String);
  for (const required of ["progress", "json", "html"]) {
    assertCondition(reporters.includes(required), `The permanent mutation gate is missing the '${required}' reporter.`);
  }
  assertCondition(Number(config.thresholds.high) === 75, "The permanent mutation high threshold must be 75%.");
  assertCondition(Number(config.thresholds.low) === 75, "The permanent mutation low threshold must be 75%.");
  assertCondition(Number(config.thresholds.break) === 75, "The permanent mutation break threshold must be 75%.");

  const configuredFiles = (config.mutate ?? []).map((file) => String(file).replaceAll("\\", "/"));
  assertCondition(configuredFiles.length === 71, `The permanent mutation allowlist must contain exactly 71 semantic files; found ${configuredFiles.length}.`);
  assertCondition(new Set(configuredFiles).size === configuredFiles.length, "The permanent mutation allowlist contains duplicate files.");
  assertCondition(configuredFiles.includes("**/CStructSharp.Core/Parsing/LayoutParser.cs"), "The layout parser must remain in the permanent mutation allowlist.");
  const nonMutableDeclarations = loadNonMutableDeclarations(repositoryRoot, configuredFiles);
  const qualifiedDeclarations = [];

  assertCondition(String(report.schemaVersion) === "2", `Unsupported Stryker report schema '${report.schemaVersion}'.`);
  assertCondition(Number(report.thresholds.high) === 75 && Number(report.thresholds.low) === 75, "The report was not produced with the final 75% mutation thresholds.");

  let testCount = 0;
  for (const testFile of Object.values(report.testFiles ?? {})) {
    const tests = testFile.tests ?? [];
    testCount += tests.length;
    assertCondition(!tests.some((test) => test.name === EXPORT_LIST_TEST), "The export-list test must not falsely kill mutants in an instrumented assembly.");
  }
  assertCondition(testCount > 0, "The mutation report contains no tests.");

  // A runtime entry is relative to src/CStructSharp; a shared compile-time source is named by its folder (`**/CStructSharp.Core/...`).
  const suffixOf = (file) => (file.startsWith("**/") ? `/${file.slice(3)}` : `/cstructsharp/${file}`).toLowerCase();
  const allMutants = [];
  const reportFiles = Object.entries(report.files ?? {}).map(([name, value]) => [name.replaceAll("\\", "/"), value]);
  for (const [reportFile, value] of reportFiles) {
    const mutants = value.mutants ?? [];
    allMutants.push(...mutants);
    if (validCountOf(mutants) === 0) continue;
    const matches = configuredFiles.filter((file) => reportFile.toLowerCase().endsWith(suffixOf(file)));
    assertCondition(matches.length === 1, `Report file '${reportFile}' has tested mutants but is outside the reviewed permanent allowlist.`);
  }
  for (const configuredFile of configuredFiles) {
    const suffix = suffixOf(configuredFile);
    const matching = reportFiles.filter(([name]) => name.toLowerCase().endsWith(suffix));
    assertCondition(matching.length === 1, `The report is missing configured file '${configuredFile}'.`);
    if (validCountOf(matching[0][1].mutants ?? []) === 0) {
      assertCondition(qualifyNonMutableDeclaration(nonMutableDeclarations, configuredFile, matching[0][1]), `Configured semantic file '${configuredFile}' produced no valid mutants.`);
      qualifiedDeclarations.push(configuredFile);
    }
  }

  const killed = countStatus(allMutants, "Killed");
  const timedOut = countStatus(allMutants, "Timeout");
  const survived = countStatus(allMutants, "Survived");
  const noCoverage = countStatus(allMutants, "NoCoverage");
  const runtimeErrors = countStatus(allMutants, "RuntimeError");
  const compileErrors = countStatus(allMutants, "CompileError");
  const ignored = countStatus(allMutants, "Ignored");
  const valid = killed + timedOut + survived + noCoverage + runtimeErrors;
  const detected = killed + timedOut;
  assertCondition(valid > 0, "The mutation report has no valid mutants.");
  const score = (100 * detected) / valid;
  const scoreText = score.toFixed(2);
  assertCondition(score >= 75, `Permanent mutation score ${scoreText}% is below the 75% release gate.`);
  assertCondition(survived === 0, `The final report contains ${survived} surviving mutants.`);
  assertCondition(noCoverage === 0, `The final report contains ${noCoverage} uncovered mutants.`);
  assertCondition(runtimeErrors === 0, `The final report contains ${runtimeErrors} runtime-error mutants.`);

  const hash = crypto.createHash("sha256").update(fs.readFileSync(reportPath)).digest("hex").toUpperCase();
  console.log(
    `Permanent mutation gate passed: ${detected}/${valid} detected (${scoreText}%), ${killed} killed, ${timedOut} timed out, ${survived} survived, ${noCoverage} uncovered, ${runtimeErrors} runtime errors; ${compileErrors} compile errors, ${ignored} ignored; ${testCount} tests; 71 configured files; ${qualifiedDeclarations.length} reviewed non-mutable declarations; SHA-256 ${hash}.`,
  );
  for (const declaration of qualifiedDeclarations) console.log(`Not applicable (no mutation opportunities): ${declaration}: ${nonMutableDeclarations.get(declaration).reason}`);
});
