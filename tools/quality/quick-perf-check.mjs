#!/usr/bin/env node
// Quick before/after benchmark check used while changing hot paths: runs the same BenchmarkDotNet filter (Short job,
// net10.0) in a baseline checkout and in this checkout, converts both reports, and prints the comparison table from
// compare-summaries.mjs. Both checkouts must already be built in Release; nothing else should run meanwhile.
//
// Usage: node tools/quality/quick-perf-check.mjs --baseline <checkout dir> [--filter '*ReadBenchmarks*' ...]
//        [--categories Impact] [--threshold 0.03] [--label <name>] [--rounds 2]
// Each side is measured `rounds` times, interleaved (before, after, before, after, ...), and the best median and
// allocation per case is kept: a transient slowdown on either side then cannot masquerade as a change.
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";

const usage = "Usage: quick-perf-check.mjs --baseline <checkout dir> [--filter ...] [--categories ...] [--threshold 0.03]";
let options;
try {
  // Filters are BenchmarkDotNet patterns that may contain commas, so each --filter value is kept whole. The threshold
  // stays text because it is passed on unchanged to compare-summaries.mjs.
  options = parseArguments(
    process.argv.slice(2),
    { baseline: "string", filter: "repeat", categories: "string", threshold: "string", label: "string", rounds: "number" },
    { defaults: { filter: [], threshold: "0.03", label: new Date().toISOString().replaceAll(/[:.]/g, "-"), rounds: 2 } },
  );
} catch (error) {
  console.error(`${error.message}\n${usage}`);
  process.exit(2);
}
const { baseline, categories, threshold, label, rounds } = options;
if (!baseline) {
  console.error(usage);
  process.exit(2);
}
const filters = options.filter.length > 0 ? options.filter : ["*"];

/** Runs the Short job with the requested filter in one checkout and returns its converted summary path. */
function measure(checkout, name) {
  const artifacts = path.join(repositoryRoot, "artifacts", "perf", "quick", label, name);
  fs.rmSync(artifacts, { recursive: true, force: true });
  const benchmarkArgs = ["run", "--project", "benchmarks/CStructSharp.Benchmarks", "-c", "Release", "-f", "net10.0", "--no-build", "--"];
  benchmarkArgs.push("--filter", ...filters);
  if (categories) benchmarkArgs.push("--anyCategories", ...categories.split(","));
  // Direct: a BenchmarkDotNet run takes minutes and its errors should appear as they happen; its verbose standard
  // output is dropped rather than buffered.
  execFileSync("dotnet", benchmarkArgs, {
    cwd: checkout,
    stdio: ["ignore", "ignore", "inherit"],
    env: { ...process.env, CSTRUCTSHARP_BENCHMARK_JOB: "Short", CSTRUCTSHARP_BENCHMARK_ARTIFACTS: artifacts },
  });
  const summary = path.join(artifacts, "summary.json");
  const conversion = runCommand(process.execPath, [path.join(repositoryRoot, "tools/quality/convert-benchmark-baseline.mjs"), path.join(artifacts, "results"), summary]);
  process.stdout.write(conversion.stdout);
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
  const merged = path.join(repositoryRoot, "artifacts", "perf", "quick", label, `${name}-best.json`);
  fs.writeFileSync(merged, JSON.stringify({ schemaVersion: 1, benchmarks: [...cases.values()] }, null, 2));
  return merged;
}

const beforeRuns = [];
const afterRuns = [];
for (let round = 1; round <= rounds; round++) {
  beforeRuns.push(measure(path.resolve(baseline), `before-${round}`));
  afterRuns.push(measure(repositoryRoot, `after-${round}`));
}
const before = best(beforeRuns, "before");
const after = best(afterRuns, "after");
const table = runCommand(process.execPath, [path.join(repositoryRoot, "tools/quality/compare-summaries.mjs"), "--before", before, "--after", after, "--threshold", threshold]).stdout;
process.stdout.write(table);
fs.writeFileSync(path.join(repositoryRoot, "artifacts", "perf", "quick", label, "comparison.md"), table);
