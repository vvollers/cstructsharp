#!/usr/bin/env node
// Soft drift report for BenchmarkDotNet summaries (schemaVersion 1, as produced by convert-benchmark-baseline.mjs
// or Convert-BenchmarkBaseline.ps1) against a recorded baseline contract (contracts/performance/non-web-rc*.json).
// Reports cases whose median grew more than the policy's soft ratio (default +10 %) or whose allocation grew more
// than the soft allocation ratio (default +5 %), plus unstable cases (RSD above the limit). Exit code is 0 unless
// --strict is given, so CI can surface drift without blocking merges until runner variance is characterized.
//
// Usage: node tools/quality/compare-benchmark-baseline.mjs --baseline <contract.json> --summary <summary.json>
//        [--markdown <out.md>] [--strict] [--median-ratio 0.10] [--allocation-ratio 0.05]
import fs from "node:fs";

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : fallback;
};
const flag = (name) => args.includes(name);
const baselinePath = option("--baseline");
const summaryPath = option("--summary");
if (!baselinePath || !summaryPath) {
  console.error("Usage: compare-benchmark-baseline.mjs --baseline <contract.json> --summary <summary.json> [--markdown out.md] [--strict]");
  process.exit(2);
}

const baseline = JSON.parse(fs.readFileSync(baselinePath, "utf8"));
const summary = JSON.parse(fs.readFileSync(summaryPath, "utf8"));
const policy = baseline.benchmark?.softPolicy ?? {};
const medianRatio = Number(option("--median-ratio", policy.medianGrowthRatio ?? 0.10));
const allocationRatio = Number(option("--allocation-ratio", policy.allocationGrowthRatio ?? 0.05));
const rsdLimit = Number(baseline.benchmark?.maximumRelativeStandardDeviation ?? 0.35);
const minimumMedianNs = Number(policy.minimumMedianNanoseconds ?? 200);
const minimumAllocationBytes = Number(policy.minimumAllocationBytes ?? 256);

// Baselines recorded per runtime (non-web-rc2) key on the runtime as well; single-runtime gates (non-web-rc1) do not.
const useRuntime = baseline.benchmark.cases.some((c) => c.runtime);
const runtimeOf = (c) => c.runtime ?? (c.displayInfo?.match(/Runtime=([^,)]+)/)?.[1] ?? "").trim();
const key = (c) => `${c.type}|${c.method}|${c.parameters ?? ""}${useRuntime ? `|${c.runtime ?? ""}` : ""}`;
const current = new Map();
for (const c of summary.benchmarks) {
  const entry = { ...c, runtime: runtimeOf(c) };
  const k = key(entry);
  if (current.has(k) && !useRuntime) continue; // first runtime wins when the baseline is runtime-agnostic
  current.set(k, entry);
}

const rows = [];
let regressions = 0;
let improvements = 0;
let unstable = 0;
let missing = 0;
for (const expected of baseline.benchmark.cases) {
  const k = key(expected);
  const actual = current.get(k);
  if (!actual) {
    missing++;
    rows.push({ key: k, status: "missing" });
    continue;
  }
  const medianDelta = actual.medianNanoseconds / expected.baselineMedianNanoseconds - 1;
  const allocDelta = expected.baselineAllocatedBytes > 0 ? actual.allocatedBytes / expected.baselineAllocatedBytes - 1 : actual.allocatedBytes > 0 ? Infinity : 0;
  const rsd = actual.medianNanoseconds > 0 ? actual.standardDeviationNanoseconds / actual.medianNanoseconds : 0;
  const medianRegressed = medianDelta > medianRatio && actual.medianNanoseconds - expected.baselineMedianNanoseconds > minimumMedianNs;
  const allocRegressed = allocDelta > allocationRatio && actual.allocatedBytes - expected.baselineAllocatedBytes > minimumAllocationBytes;
  const isUnstable = rsd > rsdLimit;
  let status = "ok";
  if (isUnstable) { status = "unstable"; unstable++; }
  else if (medianRegressed || allocRegressed) { status = "regressed"; regressions++; }
  else if (medianDelta < -medianRatio) { status = "improved"; improvements++; }
  rows.push({ key: k, status, baselineMedian: expected.baselineMedianNanoseconds, median: actual.medianNanoseconds, medianDelta, baselineAlloc: expected.baselineAllocatedBytes, alloc: actual.allocatedBytes, allocDelta, rsd });
}

const fmtNs = (v) => (v >= 1e6 ? `${(v / 1e6).toFixed(2)} ms` : v >= 1e3 ? `${(v / 1e3).toFixed(1)} µs` : `${Math.round(v)} ns`);
const pct = (v) => (Number.isFinite(v) ? `${v >= 0 ? "+" : ""}${(v * 100).toFixed(1)}%` : "n/a");
const lines = [];
lines.push(`### Benchmark drift vs ${baseline.budgetId ?? baseline.name ?? baselinePath}`);
lines.push("");
lines.push(`Soft thresholds: median +${(medianRatio * 100).toFixed(0)}%, allocation +${(allocationRatio * 100).toFixed(0)}%, RSD limit ${rsdLimit}. ` +
  `${rows.length} cases: ${regressions} regressed, ${improvements} improved, ${unstable} unstable, ${missing} missing.`);
lines.push("");
lines.push("| Case | Status | Baseline median | Median | Δ | Baseline alloc | Alloc | Δ | RSD |");
lines.push("| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
for (const row of rows.sort((a, b) => (b.medianDelta ?? 0) - (a.medianDelta ?? 0))) {
  if (row.status === "missing") {
    lines.push(`| \`${row.key}\` | missing | | | | | | | |`);
    continue;
  }
  lines.push(`| \`${row.key}\` | ${row.status} | ${fmtNs(row.baselineMedian)} | ${fmtNs(row.median)} | ${pct(row.medianDelta)} | ${row.baselineAlloc} B | ${row.alloc} B | ${pct(row.allocDelta)} | ${(row.rsd * 100).toFixed(1)}% |`);
}
const markdown = lines.join("\n") + "\n";
process.stdout.write(markdown);
const markdownPath = option("--markdown");
if (markdownPath) fs.writeFileSync(markdownPath, markdown);
if (flag("--strict") && (regressions > 0 || missing > 0)) process.exit(1);
