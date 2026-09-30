#!/usr/bin/env node
// Normalizes a BenchmarkDotNet "*report-full.json" into the schemaVersion 1 summary consumed by
// quick-perf-check.mjs, compare-summaries.mjs, render-performance-table.mjs and non-web-release-budgets.mjs.
//
// Usage: node tools/quality/convert-benchmark-baseline.mjs <report-full.json | directory> <output.json>
import fs from "node:fs";
import path from "node:path";
import { parseArguments, runCommand } from "../lib/tooling.mjs";

const usage = "Usage: convert-benchmark-baseline.mjs <report-full.json | directory> <output.json>";
let inputPath;
let outputPath;
try {
  [inputPath, outputPath] = parseArguments(process.argv.slice(2), {}, { positionals: true })._;
} catch (error) {
  console.error(`${error.message}\n${usage}`);
  process.exit(2);
}
if (!inputPath || !outputPath) {
  console.error(usage);
  process.exit(2);
}

/**
 * The reports to convert: a file as-is, or every "*report-full.json" in a directory (one per benchmark class when
 * BenchmarkDotNet runs without --join).
 * @param {string} target A report file or a directory of reports.
 * @returns {string[]} Absolute report paths, sorted.
 */
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

/**
 * Rounds a nanosecond value to three decimals: far below any gate's resolution, and stable to diff.
 * @param {number | string} value The measured value.
 * @returns {number} The rounded value.
 */
function round3(value) {
  return Math.round(Number(value) * 1000) / 1000;
}

/** Runs git in the repository root and returns its output, or an empty string when git fails. */
function git(args) {
  const result = runCommand("git", args, { allowFailure: true });
  return result.status === 0 ? result.stdout : "";
}

/**
 * Converts one BenchmarkDotNet full report into summary benchmark entries.
 * @param {object} source The parsed report.
 * @param {string} sourceName The report's file name, recorded in the summary.
 * @returns {object[]} One entry per benchmark case.
 */
function normalizeReport(source, sourceName) {
  const benchmarks = Array.isArray(source.Benchmarks) ? source.Benchmarks : [];
  if (benchmarks.length === 0) throw new Error("Benchmark report contains no cases.");
  const failed = benchmarks.filter((benchmark) => benchmark.Statistics == null);
  if (failed.length > 0) {
    throw new Error(
      `Benchmark report contains cases without statistics: ${failed.map((b) => b.FullName).join(", ")}`,
    );
  }
  /** Compares two strings by code unit order, independent of locale. */
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
      // Extra fields for diagnosis; the validator ignores members it does not know.
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
