#!/usr/bin/env node
// Quick before/after benchmark check used while changing hot paths: runs the same BenchmarkDotNet filter (Short job,
// net10.0) in a baseline checkout and in this checkout, converts both reports, and prints the comparison table from
// compare-summaries.mjs. Both checkouts must already be built in Release; nothing else should run meanwhile.
//
// Usage: node tools/quality/quick-perf-check.mjs --baseline <checkout dir> [--filter '*ReadBenchmarks*' ...]
//        [--categories ReleaseGate,Baseline0] [--threshold 0.03] [--label <name>] [--rounds 2]
// Each side is measured `rounds` times, interleaved (before, after, before, after, ...), and the best median and
// allocation per case is kept: a transient slowdown on either side then cannot masquerade as a change.
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
const rounds = Number(option("--rounds", "2"));

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

/** Merges converter summaries by keeping, per case, the smallest median and allocation seen across rounds. */
function best(summaries, name) {
  const cases = new Map();
  for (const file of summaries) {
    for (const c of JSON.parse(fs.readFileSync(file, "utf8")).benchmarks) {
      const key = `${c.type}|${c.method}|${c.parameters ?? ""}|${c.displayInfo ?? ""}`;
      const seen = cases.get(key);
      if (!seen || c.medianNanoseconds < seen.medianNanoseconds) cases.set(key, { ...c, allocatedBytes: Math.min(c.allocatedBytes, seen?.allocatedBytes ?? c.allocatedBytes) });
    }
  }
  const merged = path.join(root, "artifacts", "perf", "quick", label, `${name}-best.json`);
  fs.writeFileSync(merged, JSON.stringify({ schemaVersion: 1, benchmarks: [...cases.values()] }, null, 2));
  return merged;
}

const beforeRuns = [];
const afterRuns = [];
for (let round = 1; round <= rounds; round++) {
  beforeRuns.push(measure(path.resolve(baseline), `before-${round}`));
  afterRuns.push(measure(root, `after-${round}`));
}
const before = best(beforeRuns, "before");
const after = best(afterRuns, "after");
const table = execFileSync(process.execPath, [path.join(root, "tools/quality/compare-summaries.mjs"), "--before", before, "--after", after, "--threshold", threshold], { encoding: "utf8" });
process.stdout.write(table);
fs.writeFileSync(path.join(root, "artifacts", "perf", "quick", label, "comparison.md"), table);
