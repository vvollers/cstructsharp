#!/usr/bin/env node
// Quick before/after benchmark check used while changing hot paths: runs the same BenchmarkDotNet filter (net10.0) in a
// baseline checkout and in this checkout, converts both reports, and prints the comparison table from
// compare-summaries.mjs. Both checkouts must already be built in Release; nothing else should run meanwhile.
//
// Usage: node tools/quality/quick-perf-check.mjs --baseline <checkout dir> [--filter '*ReadBenchmarks*' ...]
//        [--categories Impact] [--threshold 0.03] [--label <name>] [--rounds 2] [--job Short|Quick]
// --job Quick runs the benchmarks in-process with short iterations (a few seconds per case instead of about ten); the
// baseline checkout's benchmark host must know the Quick job, so copy Program.cs and DevelopmentEnvironment.cs
// from benchmarks/CStructSharp.Benchmarks/ into an older baseline and build it there.
// Each side is measured `rounds` times, interleaved (before, after, before, after, ...), and its smallest median is
// kept. This optimistic selection does not establish confidence. Prefer perf-check.mjs for development screens.
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";

const usage = "Usage: quick-perf-check.mjs --baseline <checkout dir> [--filter ...] [--categories ...] [--threshold 0.03] [--rounds 2] [--job Short|Quick]";
let options;
try {
  // Filters are BenchmarkDotNet patterns that may contain commas, so each --filter value is kept whole. The threshold
  // stays text because it is passed on unchanged to compare-summaries.mjs.
  options = parseArguments(
    process.argv.slice(2),
    { baseline: "string", filter: "repeat", categories: "string", threshold: "string", label: "string", rounds: "number", job: "string" },
    { defaults: { filter: [], threshold: "0.03", label: new Date().toISOString().replaceAll(/[:.]/g, "-"), rounds: 2, job: "Short" } },
  );
} catch (error) {
  console.error(`${error.message}\n${usage}`);
  process.exit(2);
}
const { baseline, categories, threshold, label, rounds, job } = options;
if (!baseline) {
  console.error(usage);
  process.exit(2);
}
const filters = options.filter.length > 0 ? options.filter : ["*"];

/** Runs the requested job and filter in one checkout and returns its converted summary path. */
function measure(checkout, name) {
  const artifacts = path.join(repositoryRoot, "artifacts", "perf", "quick", label, name);
  fs.rmSync(artifacts, { recursive: true, force: true });
  // Start the built host directly when it exists: `dotnet run` evaluates the project first, which costs seconds per run.
  const host = path.join(checkout, "benchmarks/CStructSharp.Benchmarks/bin/Release/net10.0/CStructSharp.Benchmarks.dll");
  const benchmarkArgs = fs.existsSync(host)
    ? [host]
    : ["run", "--project", "benchmarks/CStructSharp.Benchmarks", "-c", "Release", "-f", "net10.0", "--no-build", "--"];
  benchmarkArgs.push("--filter", ...filters);
  if (categories) benchmarkArgs.push("--anyCategories", ...categories.split(","));
  // Direct: a BenchmarkDotNet run takes minutes and its errors should appear as they happen; its verbose standard
  // output is dropped rather than buffered.
  execFileSync("dotnet", benchmarkArgs, {
    cwd: checkout,
    stdio: ["ignore", "ignore", "inherit"],
    // The Quick job runs in-process with short warmups, too short for tiered compilation to promote the measured code
    // to optimized code at a predictable moment. Starting the host without tiering compiles every method fully
    // optimized on first use (no dynamic PGO) on both sides. This removes one source of variation, not all drift.
    env: {
      ...process.env,
      CSTRUCTSHARP_BENCHMARK_JOB: job,
      CSTRUCTSHARP_BENCHMARK_ARTIFACTS: artifacts,
      ...(job.toLowerCase() === "quick" ? { DOTNET_TieredCompilation: "0" } : {}),
    },
  });
  const summary = path.join(artifacts, "summary.json");
  const conversion = runCommand(process.execPath, [path.join(repositoryRoot, "tools/quality/convert-benchmark-baseline.mjs"), path.join(artifacts, "results"), summary]);
  process.stdout.write(conversion.stdout);
  return summary;
}

/** Keeps each smallest timing median; allocation updates only when that timing record replaces its predecessor. */
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

const started = Date.now();
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
console.log(`Measured ${rounds} round(s) per side with the ${job} job in ${Math.round((Date.now() - started) / 1000)} s.`);
