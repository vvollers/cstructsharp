#!/usr/bin/env node
// Cold-start harness: spawns fresh Node processes (default 5) and records per-phase timings for the first
// compile and first parse. Writes artifacts/js-bench/results/cold-<timestamp>.json (+ cold-latest.json).
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { captureEnvironment } from "./environment.mjs";
import { repositoryRoot } from "./fixtures.mjs";

const launches = Number(process.env.BENCH_COLD_LAUNCHES ?? 5);
const fixtures = (process.env.BENCH_COLD_FIXTURES ?? "real-png,prim-le-record,nested-x256").split(",");
const child = path.join(path.dirname(fileURLToPath(import.meta.url)), "cold-child.mjs");
const resultsDirectory = path.join(repositoryRoot, "artifacts/js-bench/results");
fs.mkdirSync(resultsDirectory, { recursive: true });

const median = (values) => {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = sorted.length >> 1;
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
};

const results = [];
for (const fixture of fixtures) {
  const samples = [];
  for (let i = 0; i < launches; i++) {
    const wall = performance.now();
    const run = spawnSync(process.execPath, [child, fixture], { encoding: "utf8", cwd: repositoryRoot });
    const wallMs = performance.now() - wall;
    if (run.status !== 0) throw new Error(`cold child failed: ${run.stderr}`);
    const line = run.stdout.trim().split("\n").pop();
    samples.push({ ...JSON.parse(line), processWallMs: wallMs });
  }
  const keys = ["processWallMs", "processStartToBundleReadyMs", "runtimeCreateMs", "exportsReadyMs", "firstCompileMs", "firstCoreParseMs", "firstPublicParseMs"];
  const summary = { fixture, launches, medians: {}, samples };
  for (const key of keys) summary.medians[key] = Number(median(samples.map((s) => s[key])).toFixed(3));
  results.push(summary);
  console.log(`${fixture}: ` + keys.map((k) => `${k}=${summary.medians[k].toFixed(1)}`).join(" "));
}
const report = { schemaVersion: 1, harness: "benchmarks/js/bench/cold.mjs", environment: captureEnvironment(), results };
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
fs.writeFileSync(path.join(resultsDirectory, `cold-${stamp}.json`), JSON.stringify(report, null, 2) + "\n");
fs.writeFileSync(path.join(resultsDirectory, "cold-latest.json"), JSON.stringify(report, null, 2) + "\n");
