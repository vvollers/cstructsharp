#!/usr/bin/env node
/**
 * Collect and merge runtime, compiled parity and generator-consumer coverage of the runtime assembly.
 * Build the non-Web solution first. Usage: node tools/quality/collect-library-coverage.mjs [--output-directory dir].
 * Tests execute sequentially because Coverlet temporarily instruments their output assemblies.
 */
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { coverageFileHash, passingTestResults } from "../lib/coverage-population.mjs";

const options = parseArguments(process.argv.slice(2), { "output-directory": "string" }, {
  defaults: { "output-directory": "artifacts/test-results/library-coverage" },
});
const output = path.resolve(repositoryRoot, options["output-directory"]);
fs.mkdirSync(output, { recursive: true });
// A fresh directory prevents an earlier successful report from hiding a missing report in this run.
const runDirectory = fs.mkdtempSync(path.join(output, "run-"));
const suites = [
  ["runtime", "tests/CStructSharpTests/CStructSharpTests.csproj"],
  ["parity", "tests/CStructSharp.Generated.Parity/CStructSharp.Generated.Parity.csproj"],
  ["generator", "tests/CStructSharp.Generators.Tests/CStructSharp.Generators.Tests.csproj"],
];
const evidence = [];
let previous;
for (const [name, project] of suites) {
  const directory = path.join(runDirectory, name);
  fs.mkdirSync(directory);
  const format = name === "generator" ? "cobertura" : "json";
  const args = [
    "test", project, "-c", "Release", "-f", "net10.0", "--no-build",
    "--logger", "trx;LogFileName=tests.trx", "--results-directory", directory,
    "-p:CollectCoverage=true", "-p:Include=[CStructSharp]*",
    `-p:CoverletOutputFormat=${format}`, `-p:CoverletOutput=${directory}${path.sep}`,
  ];
  if (previous) {
    assert.ok(fs.statSync(previous).size > 0, "The preceding coverage report must exist before merging");
    args.push(`-p:MergeWith=${previous}`);
  }
  console.log(`Collecting ${name} coverage${previous ? `, merging ${previous}` : ""}`);
  execFileSync("dotnet", args, { cwd: repositoryRoot, stdio: "inherit" });
  // Multi-target projects add a framework suffix; discover exactly one report instead of guessing its name.
  // Match only reports written by this suite, never another suite's TRX or a previous collection.
  const reports = fs.readdirSync(directory).filter((file) => file.endsWith(format === "json" ? ".json" : ".cobertura.xml"));
  assert.equal(reports.length, 1, `${name} must produce exactly one ${format} coverage report`);
  const report = path.join(directory, reports[0]);
  assert.ok(fs.statSync(report).size > 0);
  const tests = path.join(directory, "tests.trx");
  passingTestResults(tests);
  evidence.push({ name, project, report: path.relative(output, report), sha256: coverageFileHash(report),
    tests: path.relative(output, tests), testsSha256: coverageFileHash(tests) });
  previous = report;
}

// Publish the merged report only after all three suites and their reports succeeded.
fs.copyFileSync(previous, path.join(output, "coverage.cobertura.xml"));
// Badge test counts remain the core suite once on .NET 10; coverage uses the full merged library report.
const badgeInput = path.join(output, "badge-input");
fs.mkdirSync(badgeInput, { recursive: true });
fs.copyFileSync(previous, path.join(badgeInput, "coverage.cobertura.xml"));
fs.copyFileSync(path.join(output, evidence[0].tests), path.join(badgeInput, "tests.trx"));
fs.writeFileSync(path.join(output, "collection.json"), JSON.stringify({
  schemaVersion: 1,
  coverageSha256: coverageFileHash(previous),
  sourceSha: execFileSync("git", ["rev-parse", "HEAD"], { cwd: repositoryRoot, encoding: "utf8" }).trim(),
  suites: evidence,
}, null, 2) + "\n");
console.log(`Merged library coverage: ${path.join(output, "coverage.cobertura.xml")}`);
