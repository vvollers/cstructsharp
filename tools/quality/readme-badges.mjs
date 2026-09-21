#!/usr/bin/env node
/**
 * Writes the README badge JSON files (line coverage, branch coverage, tests) from one framework's coverage and
 * TRX reports, plus a README describing their source.
 *
 *   node tools/quality/readme-badges.mjs --results-directory <dir> --output-directory <dir> --run-url <url>
 */
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments } from "../lib/tooling.mjs";
import { findAll, findFirst, parseXml } from "../lib/xml.mjs";

const options = parseArguments(process.argv.slice(2), { "results-directory": "string", "output-directory": "string", "run-url": "string" });
for (const name of ["results-directory", "output-directory", "run-url"]) {
  assertCondition(options[name], `Option --${name} is required.`);
}

function findFiles(directory, predicate) {
  const found = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) found.push(...findFiles(full, predicate));
    else if (predicate(entry.name)) found.push(full);
  }
  return found;
}

await main(() => {
  // Use one framework's reports: summing both would count the same test cases twice.
  const coverageFiles = findFiles(options["results-directory"], (name) => name === "coverage.cobertura.xml");
  const testFiles = findFiles(options["results-directory"], (name) => name.endsWith(".trx"));
  // VSTest can also copy the attachment into its In/ directory. Accept identical copies only.
  const coverageHashes = new Set(coverageFiles.map((file) => crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex")));
  assertCondition(coverageHashes.size === 1 && testFiles.length === 1, "Expected exactly one .NET 10 coverage report and one TRX report.");
  const coverage = findFirst(parseXml(fs.readFileSync(coverageFiles[0], "utf8")), "coverage");
  const tests = parseXml(fs.readFileSync(testFiles[0], "utf8"));
  const packages = findAll(coverage, "package");
  assertCondition(packages.length === 1 && packages[0].attributes.name === "CStructSharp", "Coverage must contain only the CStructSharp library.");
  const counters = findFirst(findFirst(tests, "ResultSummary") ?? { children: [] }, "Counters");
  assertCondition(counters, "TRX test counters are missing.");

  fs.mkdirSync(options["output-directory"], { recursive: true });
  const writeBadge = (name, label, message, color) =>
    fs.writeFileSync(path.join(options["output-directory"], `${name}.json`), `${JSON.stringify({ schemaVersion: 1, label, message, color }, null, 2)}\n`);

  for (const metric of ["line", "branch"]) {
    const rate = Number(coverage.attributes[`${metric}-rate`]);
    const valid = Number(coverage.attributes[metric === "line" ? "lines-valid" : "branches-valid"]);
    assertCondition(rate >= 0 && rate <= 1 && valid > 0, `Invalid ${metric} coverage totals.`);
    const percent = `${(rate * 100).toFixed(1)}%`;
    const color = rate >= 0.9 ? "brightgreen" : rate >= 0.75 ? "yellowgreen" : "orange";
    writeBadge(`${metric}-coverage`, `C# ${metric} coverage`, percent, color);
  }

  // Count the per-test outcomes rather than trusting the summary counters: MSTest reports a conditionally
  // skipped test (Assert.Inconclusive) as total-but-not-executed and in no other counter.
  const results = findAll(tests, "UnitTestResult");
  const outcomes = new Map();
  for (const result of results) outcomes.set(result.attributes.outcome, (outcomes.get(result.attributes.outcome) ?? 0) + 1);
  const count = (names) => names.reduce((sum, name) => sum + (outcomes.get(name) ?? 0), 0);
  const passed = count(["Passed"]);
  const failed = count(["Failed", "Error", "Timeout", "Aborted", "PassedButRunAborted", "Disconnected"]);
  const skipped = count(["NotExecuted", "Inconclusive", "NotRunnable"]);
  const total = Number(counters.attributes.total);
  assertCondition(total > 0 && results.length === total && passed + failed + skipped === total, "Incomplete or inconsistent test results.");
  writeBadge("tests", "C# tests", `${passed} passed / ${failed} failed / ${skipped} skipped`, failed ? "red" : "brightgreen");

  fs.writeFileSync(
    path.join(options["output-directory"], "README.md"),
    `# README quality statistics

Source: [CI run](${options["run-url"]}).

Coverage measures only the CStructSharp managed library on .NET 10, merged across the core,
compiled parity and generator-consumer suites. Test counts include the core suite's
parameterized cases from that framework once; they exclude generator, parity, Vue and browser test cases.
The website deployment refreshes these statistics from a successful main-branch CI run.
Full TRX and Cobertura reports are available in that run's test-results artifact.
`,
  );
});
