#!/usr/bin/env node
// Records contracts/performance/web-benchmark-rc1.json from the latest Node, browser, and cold-start results.
// Usage: node bench/record.mjs [--output <contract>] [--name web-benchmark-rc1]
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { repositoryRoot } from "./fixtures.mjs";

const args = process.argv.slice(2);
const option = (name, fallback) => { const i = args.indexOf(name); return i >= 0 ? args[i + 1] : fallback; };
const output = option("--output", path.join(repositoryRoot, "contracts/performance/web-benchmark-rc1.json"));
const results = path.join(repositoryRoot, "artifacts/js-bench/results");
const read = (file) => (fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, "utf8")) : null);
const sha = (file) => crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex").toUpperCase();

const node = read(path.join(results, "node-latest.json"));
const browser = read(path.join(results, "browser-latest.json"));
const cold = read(path.join(results, "cold-latest.json"));
if (!node && !browser) { console.error("No results found under artifacts/js-bench/results."); process.exit(1); }

const toCase = (r) => ({
  name: r.name,
  group: r.group,
  tags: r.tags,
  batches: r.batches,
  batchSize: r.batchSize,
  medianNanoseconds: r.medianNanoseconds,
  meanNanoseconds: r.meanNanoseconds,
  relativeStandardDeviation: r.relativeStandardDeviation,
  unstable: r.unstable,
  latency: r.latency,
  managedAllocatedBytesPerOp: r.managedAllocatedBytesPerOp,
  copies: r.copies ?? null,
});

const contract = {
  schemaVersion: 1,
  name: option("--name", "web-benchmark-rc1"),
  status: "baseline",
  description: "Phase 0 baseline of the JS/WASM bridge (benchmarks/js): boundary micro-cases, retained-layout core parse and JSON projection, public API, compiled handle, stream sources, DataView comparators, and cold start in Node and headless Chromium. Consumed by benchmarks/js/bench/check.mjs as a soft drift report.",
  date: new Date().toISOString().slice(0, 10),
  softPolicy: { medianGrowthRatio: 0.10, allocationGrowthRatio: 0.05, maximumRelativeStandardDeviation: 0.35 },
  baselineEvidence: {
    node: node ? { file: "artifacts/js-bench/results/node-latest.json", sha256: sha(path.join(results, "node-latest.json")), environment: node.environment, settings: node.settings } : null,
    browser: browser ? { file: "artifacts/js-bench/results/browser-latest.json", sha256: sha(path.join(results, "browser-latest.json")), environment: browser.environment, settings: browser.settings } : null,
    cold: cold ? { file: "artifacts/js-bench/results/cold-latest.json", sha256: sha(path.join(results, "cold-latest.json")), environment: cold.environment } : null,
  },
  hosts: {
    ...(node ? { node: { cases: node.results.map(toCase) } } : {}),
    ...(browser ? { browser: { cases: browser.results.map(toCase) } } : {}),
  },
  coldStart: {
    ...(cold ? { node: cold.results.map((r) => ({ fixture: r.fixture, launches: r.launches, medians: r.medians })) } : {}),
    ...(browser?.coldStart ? { browser: browser.coldStart.map((r) => ({ fixture: r.fixture, launches: r.launches, medians: r.medians })) } : {}),
  },
};
if (args.includes("--merge") && fs.existsSync(output)) {
  // Partial re-baseline: replace re-measured cases per host, keep everything else, append a dated note.
  const existing = JSON.parse(fs.readFileSync(output, "utf8"));
  let replaced = 0;
  for (const [host, value] of Object.entries(contract.hosts)) {
    const current = existing.hosts[host]?.cases ?? [];
    const incoming = new Map(value.cases.map((c) => [c.name, c]));
    const merged = current.map((c) => (incoming.has(c.name) ? (replaced++, incoming.get(c.name)) : c));
    for (const c of value.cases) if (!current.some((e) => e.name === c.name)) merged.push(c);
    existing.hosts[host] = { cases: merged };
  }
  if (cold) existing.coldStart.node = contract.coldStart.node;
  if (browser?.coldStart) existing.coldStart.browser = contract.coldStart.browser;
  existing.updates ??= [];
  existing.updates.push({ date: contract.date, note: option("--note", "partial re-baseline"), replacedCases: replaced, evidence: contract.baselineEvidence });
  fs.writeFileSync(output, JSON.stringify(existing, null, 2) + "\n");
  console.log(`Merged ${replaced} re-measured cases into ${path.relative(repositoryRoot, output)}`);
} else {
  fs.writeFileSync(output, JSON.stringify(contract, null, 2) + "\n");
  console.log(`Recorded ${Object.entries(contract.hosts).map(([h, v]) => `${h}: ${v.cases.length}`).join(", ")} cases to ${path.relative(repositoryRoot, output)}`);
}
