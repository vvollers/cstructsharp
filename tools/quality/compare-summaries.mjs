#!/usr/bin/env node
// Compares two schemaVersion 1 benchmark summaries (as written by convert-benchmark-baseline.mjs) case by case and
// prints a Markdown table of median and allocation deltas. It is the quick before/after check used while changing
// hot paths: run the affected benchmark classes with the Short job, convert, and compare against a summary recorded
// from the unchanged tree. Cases present on only one side are listed, never counted as regressions.
//
// Usage: node tools/quality/compare-summaries.mjs --before <summary.json> --after <summary.json>
//        [--threshold 0.03] [--strict]
// Exit code 1 with --strict when any case's median or allocation grew beyond the threshold.
import fs from "node:fs";

const args = process.argv.slice(2);
const option = (name, fallback) => {
  const index = args.indexOf(name);
  return index >= 0 ? args[index + 1] : fallback;
};
const beforePath = option("--before");
const afterPath = option("--after");
if (!beforePath || !afterPath) {
  console.error("Usage: compare-summaries.mjs --before <summary.json> --after <summary.json> [--threshold 0.03] [--strict]");
  process.exit(2);
}
const threshold = Number(option("--threshold", "0.03"));
const strict = args.includes("--strict");

/** Reads a summary and indexes its cases by type, method, parameters, and runtime. */
function load(file) {
  const summary = JSON.parse(fs.readFileSync(file, "utf8"));
  const map = new Map();
  for (const c of summary.benchmarks) {
    const runtime = c.runtime ?? (c.displayInfo?.match(/Runtime=([^,)]+)/)?.[1] ?? "").trim();
    map.set(`${c.type}|${c.method}|${c.parameters ?? ""}|${runtime}`, c);
  }
  return map;
}

const fmtNs = (v) => (v >= 1e6 ? `${(v / 1e6).toFixed(2)} ms` : v >= 1e3 ? `${(v / 1e3).toFixed(1)} µs` : `${Math.round(v)} ns`);
const pct = (v) => (Number.isFinite(v) ? `${v >= 0 ? "+" : ""}${(v * 100).toFixed(1)}%` : "n/a");

const before = load(beforePath);
const after = load(afterPath);
const lines = ["| Case | Before | After | Δ median | Alloc before | Alloc after | Δ alloc | Status |", "| --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |"];
let regressed = 0;
let improved = 0;
for (const [key, a] of after) {
  const b = before.get(key);
  if (!b) {
    lines.push(`| \`${key}\` | — | ${fmtNs(a.medianNanoseconds)} | | | ${a.allocatedBytes} | | new |`);
    continue;
  }
  const medianDelta = a.medianNanoseconds / b.medianNanoseconds - 1;
  const allocDelta = b.allocatedBytes > 0 ? a.allocatedBytes / b.allocatedBytes - 1 : a.allocatedBytes > 0 ? Infinity : 0;
  let status = "ok";
  if (medianDelta > threshold || allocDelta > threshold) {
    status = "regressed";
    regressed++;
  } else if (medianDelta < -threshold || allocDelta < -threshold) {
    status = "improved";
    improved++;
  }
  lines.push(`| \`${key}\` | ${fmtNs(b.medianNanoseconds)} | ${fmtNs(a.medianNanoseconds)} | ${pct(medianDelta)} | ${b.allocatedBytes} | ${a.allocatedBytes} | ${pct(allocDelta)} | ${status} |`);
}
for (const key of before.keys()) {
  if (!after.has(key)) lines.push(`| \`${key}\` | | | | | | | missing after |`);
}
lines.push("", `${regressed} regressed, ${improved} improved (threshold ±${(threshold * 100).toFixed(0)}%).`);
console.log(lines.join("\n"));
if (strict && regressed > 0) process.exit(1);
