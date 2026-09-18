#!/usr/bin/env node
// Normalizes a BenchmarkDotNet "*report-full.json" into the
// schemaVersion 1 summary consumed by non-web-release-budgets.mjs and compare-benchmark-baseline.mjs.
// The output is field-for-field identical to the PowerShell converter so either tool can feed the gate.
//
// Usage: node tools/quality/convert-benchmark-baseline.mjs <report-full.json | directory> <output.json>
import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const [inputPath, outputPath] = process.argv.slice(2);
if (!inputPath || !outputPath) {
  console.error("Usage: convert-benchmark-baseline.mjs <report-full.json | directory> <output.json>");
  process.exit(2);
}

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

// A file is used as-is. A directory merges every "*report-full.json" it contains (one per benchmark class when
// BenchmarkDotNet runs without --join); the PowerShell converter instead picks the newest single file.
function findReports(target) {
  const resolved = path.resolve(target);
  const stat = fs.statSync(resolved);
  if (!stat.isDirectory()) return [resolved];
  const files = fs
    .readdirSync(resolved)
    .filter((name) => name.endsWith("report-full.json"))
    .sort()
    .map((name) => path.join(resolved, name));
  if (files.length === 0) {
    throw new Error(`No BenchmarkDotNet full JSON report found under ${target}`);
  }
  return files;
}

function round3(value) {
  // PowerShell [Math]::Round uses banker's rounding; BenchmarkDotNet values have enough digits that the
  // difference is immaterial for gating, but keep three decimals for parity with the .ps1 output.
  return Math.round(Number(value) * 1000) / 1000;
}

function git(args) {
  try {
    return execFileSync("git", ["-C", repositoryRoot, ...args], { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] });
  } catch {
    return "";
  }
}

function normalizeReport(source, sourceName) {
  const benchmarks = Array.isArray(source.Benchmarks) ? source.Benchmarks : [];
  if (benchmarks.length === 0) throw new Error("Benchmark report contains no cases.");
  const failed = benchmarks.filter((benchmark) => benchmark.Statistics == null);
  if (failed.length > 0) {
    throw new Error(
      `Benchmark report contains cases without statistics: ${failed.map((b) => b.FullName).join(", ")}`,
    );
  }
  const compare = (a, b) => (a < b ? -1 : a > b ? 1 : 0);
  const sorted = [...benchmarks].sort(
    (a, b) =>
      compare(a.Type ?? "", b.Type ?? "") ||
      compare(a.Method ?? "", b.Method ?? "") ||
      compare(a.Parameters ?? "", b.Parameters ?? ""),
  );
  return {
    schemaVersion: 1,
    generatedAtUtc: new Date().toISOString(),
    revision: git(["rev-parse", "HEAD"]).trim() || null,
    worktreeDirty: git(["status", "--porcelain"]).trim().length > 0,
    source: sourceName,
    title: source.Title,
    hostEnvironment: source.HostEnvironmentInfo,
    benchmarks: sorted.map((benchmark) => ({
      type: benchmark.Type,
      method: benchmark.Method,
      parameters: benchmark.Parameters,
      displayInfo: benchmark.DisplayInfo,
      samples: benchmark.Statistics.N,
      meanNanoseconds: round3(benchmark.Statistics.Mean),
      medianNanoseconds: round3(benchmark.Statistics.Median),
      standardDeviationNanoseconds: round3(benchmark.Statistics.StandardDeviation),
      allocatedBytes: round3(benchmark.Memory?.BytesAllocatedPerOperation ?? 0),
      // Extra fields beyond the .ps1 output; the validator ignores unknown members.
      percentile95Nanoseconds: round3(benchmark.Statistics.Percentiles?.P95 ?? benchmark.Statistics.Median),
      relativeStandardDeviation:
        Number(benchmark.Statistics.Median) > 0
          ? round3(Number(benchmark.Statistics.StandardDeviation) / Number(benchmark.Statistics.Median))
          : null,
      gen0Collections: benchmark.Memory?.Gen0Collections ?? null,
    })),
  };
}

const reportFiles = findReports(inputPath);
const sources = reportFiles.map((file) => JSON.parse(fs.readFileSync(file, "utf8")));
const merged = {
  Title: sources.length === 1 ? sources[0].Title : sources.map((s) => s.Title).join(" + "),
  HostEnvironmentInfo: sources[0].HostEnvironmentInfo,
  Benchmarks: sources.flatMap((s) => s.Benchmarks ?? []),
};
const report = normalizeReport(merged, reportFiles.map((file) => path.basename(file)).join(";"));
fs.mkdirSync(path.dirname(path.resolve(outputPath)), { recursive: true });
fs.writeFileSync(outputPath, JSON.stringify(report, null, 2) + "\n");
console.log(`Normalized ${report.benchmarks.length} benchmark cases to ${outputPath}`);
