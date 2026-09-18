#!/usr/bin/env node
// Quick before/after benchmark check used while changing hot paths: runs the same BenchmarkDotNet filter (Short job,
// net10.0) in a baseline checkout and in this checkout, converts both reports, and prints the comparison table from
// compare-summaries.mjs. Both checkouts must already be built in Release; nothing else should run meanwhile.
//
// Usage: node tools/quality/quick-perf-check.mjs --baseline <checkout dir> [--filter '*ReadBenchmarks*' ...]
//        [--categories ReleaseGate,Baseline0] [--threshold 0.03] [--label <name>]
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const args = process.argv.slice(2);
const option = (name, fallback) => {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : fallback;
};
const baseline = option("--baseline");
if (!baseline) {
  console.error("Usage: quick-perf-check.mjs --baseline <checkout dir> [--filter ...] [--categories ...] [--threshold 0.03]");
  process.exit(2);
}
const filters = [];
for (let index = 0; index < args.length; index++) {
  if (args[index] === "--filter") filters.push(args[++index]);
}
if (filters.length === 0) filters.push("*");
const categories = option("--categories");
const threshold = option("--threshold", "0.03");
const label = option("--label", new Date().toISOString().replaceAll(/[:.]/g, "-"));

/** Runs the Short job with the requested filter in one checkout and returns its converted summary path. */
function measure(checkout, name) {
  const artifacts = path.join(root, "artifacts", "perf", "quick", label, name);
  fs.rmSync(artifacts, { recursive: true, force: true });
  const benchmarkArgs = ["run", "--project", "benchmarks/CStructSharp.Benchmarks", "-c", "Release", "-f", "net10.0", "--no-build", "--"];
  benchmarkArgs.push("--filter", ...filters);
  if (categories) benchmarkArgs.push("--anyCategories", ...categories.split(","));
  execFileSync("dotnet", benchmarkArgs, {
    cwd: checkout,
    stdio: ["ignore", "ignore", "inherit"],
    env: { ...process.env, CSTRUCTSHARP_BENCHMARK_JOB: "Short", CSTRUCTSHARP_BENCHMARK_ARTIFACTS: artifacts },
  });
  const summary = path.join(artifacts, "summary.json");
  execFileSync(process.execPath, [path.join(root, "tools/quality/convert-benchmark-baseline.mjs"), path.join(artifacts, "results"), summary], { stdio: "inherit" });
  return summary;
}

const before = measure(path.resolve(baseline), "before");
const after = measure(root, "after");
const table = execFileSync(process.execPath, [path.join(root, "tools/quality/compare-summaries.mjs"), "--before", before, "--after", after, "--threshold", threshold], { encoding: "utf8" });
process.stdout.write(table);
fs.writeFileSync(path.join(root, "artifacts", "perf", "quick", label, "comparison.md"), table);
