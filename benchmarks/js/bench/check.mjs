#!/usr/bin/env node
// Soft drift report for JS harness results against contracts/performance/web-benchmark-rc1.json.
// Usage: node bench/check.mjs [--baseline <contract>] [--node <node-latest.json>] [--browser <browser-latest.json>]
//        [--markdown out.md] [--strict]
import fs from "node:fs";
import path from "node:path";
import { repositoryRoot } from "./fixtures.mjs";

const args = process.argv.slice(2);
const option = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const baselinePath = option("--baseline", path.join(repositoryRoot, "contracts/performance/web-benchmark-rc1.json"));
const baseline = JSON.parse(fs.readFileSync(baselinePath, "utf8"));
const policy = baseline.softPolicy ?? {};
const medianRatio = Number(option("--median-ratio", policy.medianGrowthRatio ?? 0.10));
const allocationRatio = Number(option("--allocation-ratio", policy.allocationGrowthRatio ?? 0.05));
const rsdLimit = Number(policy.maximumRelativeStandardDeviation ?? 0.35);
const fmtNs = (v) => (v >= 1e6 ? `${(v / 1e6).toFixed(2)} ms` : v >= 1e3 ? `${(v / 1e3).toFixed(1)} µs` : `${Math.round(v)} ns`);
const pct = (v) => (Number.isFinite(v) ? `${v >= 0 ? "+" : ""}${(v * 100).toFixed(1)}%` : "n/a");

const lines = [`### JS benchmark drift vs ${baseline.name}`, ""];
let regressions = 0;
let missing = 0;
for (const host of ["node", "browser"]) {
  const file = option(`--${host}`, path.join(repositoryRoot, `artifacts/js-bench/results/${host}-latest.json`));
  if (!fs.existsSync(file) || !baseline.hosts?.[host]) continue;
  const current = new Map(JSON.parse(fs.readFileSync(file, "utf8")).results.map((r) => [r.name, r]));
  lines.push(`#### ${host}`, "", "| Case | Status | Baseline median | Median | Δ | Baseline alloc | Alloc | Δ | RSD |", "| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |");
  for (const expected of baseline.hosts[host].cases) {
    const actual = current.get(expected.name);
    if (!actual) { missing++; lines.push(`| \`${expected.name}\` | missing | | | | | | | |`); continue; }
    const medianDelta = actual.medianNanoseconds / expected.medianNanoseconds - 1;
    const allocDelta = expected.managedAllocatedBytesPerOp > 0 && actual.managedAllocatedBytesPerOp != null ? actual.managedAllocatedBytesPerOp / expected.managedAllocatedBytesPerOp - 1 : NaN;
    const rsd = actual.relativeStandardDeviation ?? 0;
    let status = "ok";
    if (rsd > rsdLimit) status = "unstable";
    else if (medianDelta > medianRatio || allocDelta > allocationRatio) { status = "regressed"; regressions++; }
    else if (medianDelta < -medianRatio) status = "improved";
    lines.push(`| \`${expected.name}\` | ${status} | ${fmtNs(expected.medianNanoseconds)} | ${fmtNs(actual.medianNanoseconds)} | ${pct(medianDelta)} | ${expected.managedAllocatedBytesPerOp ?? "n/a"} | ${actual.managedAllocatedBytesPerOp ?? "n/a"} | ${pct(allocDelta)} | ${(rsd * 100).toFixed(1)}% |`);
  }
  lines.push("");
}
lines.push(`${regressions} regressed, ${missing} missing (soft thresholds: median +${medianRatio * 100}%, allocation +${allocationRatio * 100}%).`);
const markdown = lines.join("\n") + "\n";
process.stdout.write(markdown);
const markdownPath = option("--markdown");
if (markdownPath) fs.writeFileSync(markdownPath, markdown);
if (args.includes("--strict") && (regressions > 0 || missing > 0)) process.exit(1);
