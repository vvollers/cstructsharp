#!/usr/bin/env node
// Node harness: boundary micro-cases, retained-layout core/projection cases, public API cases, stream cases, and
// hand-written DataView comparators. Writes artifacts/js-bench/results/node-<timestamp>.json (+ node-latest.json).
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import { runCases, formatRecord, sinkValue } from "./harness.mjs";
import { boundaryCases, coreCases, publicCases, streamCases, comparatorCases, verifyFixture } from "./cases.mjs";
import { loadFixture, repositoryRoot } from "./fixtures.mjs";
import { captureEnvironment } from "./environment.mjs";
import { loadBundle } from "./runtime.mjs";

const filter = process.env.BENCH_FILTER ? new RegExp(process.env.BENCH_FILTER) : null;
const quick = process.env.BENCH_QUICK === "1";
const resultsDirectory = path.join(repositoryRoot, "artifacts/js-bench/results");
fs.mkdirSync(resultsDirectory, { recursive: true });

const bundle = await loadBundle();
const copyCounter = { pages: 0, pageBytes: 0, bytesIn: 0, bytesOut: 0, jsonChars: 0 };
const tempFiles = [];
const env = {
  host: "node",
  loadFixture: async (id) => loadFixture(id),
  managed: bundle.managed,
  api: bundle.api,
  runtime: bundle.runtime,
  copyCounter,
  sha256: (text) => crypto.createHash("sha256").update(text, "utf8").digest("hex"),
  nodeStreamFactory: (id) => {
    const file = path.join(os.tmpdir(), `cstructsharp-js-bench-${id}.bin`);
    if (!fs.existsSync(file)) {
      fs.writeFileSync(file, loadFixture(id).bytes);
      tempFiles.push(file);
    }
    return fs.createReadStream(file);
  },
};

// Correctness gate before timing: the public JS path must reproduce the C# expected JSON for every fixture that
// has an inline expectation or a digest (large arrays are compared by SHA-256).
const verified = [];
for (const id of ["prim-le-record", "prim-le-x1k", "nested-x256", "aligned-x256", "array-u8-1024", "array-u32-be-256", "dynamic-64", "bitfield-x1k", "enum-x1k", "union-x1k", "strings-1024", "pointer-depth-8", "cond-if128", "real-bmp", "real-png", "real-pe-exe", "real-tar"]) {
  await verifyFixture(env, id);
  verified.push(id);
}
console.log(`Verified ${verified.length} fixtures against C# expectations through the public JS API.`);

const groups = [
  ["boundary", await boundaryCases(env)],
  ["core", await coreCases(env)],
  ["public", await publicCases(env)],
  ["stream", await streamCases(env)],
  ["comparator", await comparatorCases(env)],
];
const options = quick
  ? { warmupTime: 150, batchTargetMs: 30, batches: 5, latencyTime: 300 }
  : { warmupTime: 600, batchTargetMs: 100, batches: 9, latencyTime: 1500 };
const records = [];
for (const [group, cases] of groups) {
  const selected = filter ? cases.filter((c) => filter.test(c.name)) : cases;
  if (selected.length === 0) continue;
  console.log(`\n== ${group}`);
  const groupRecords = await runCases(selected, {
    ...options,
    allocatedBytes: () => bundle.managed.BenchAllocatedBytes(),
    onResult: (record) => console.log("  " + formatRecord(record)),
  });
  // Copy accounting: one instrumented call per case (counters are only visible for calls made on this thread).
  for (let i = 0; i < selected.length; i++) {
    // Re-run the case's `before` hook: cases share retained managed state (the compiled layout, retained result),
    // so a one-off call after the whole group has run would otherwise execute against the previous case's layout.
    if (selected[i].before) await selected[i].before();
    Object.keys(copyCounter).forEach((key) => (copyCounter[key] = 0));
    const value = await selected[i].fn();
    const bytesIn = selected[i].meta?.bytes ?? 0;
    groupRecords[i].copies = {
      pageReads: copyCounter.pages,
      pageBytes: copyCounter.pageBytes,
      inputBytes: bytesIn,
      outputChars: typeof value === "string" ? value.length : typeof value?.Data === "string" ? value.Data.length : value?.Data?.byteLength ?? null,
    };
    groupRecords[i].group = group;
  }
  records.push(...groupRecords);
}

const report = {
  schemaVersion: 1,
  harness: "benchmarks/js/bench/node.mjs",
  settings: options,
  environment: captureEnvironment({ runtimeTimings: bundle.timings, verifiedFixtures: verified }),
  results: records,
  sink: sinkValue(),
};
const stamp = new Date().toISOString().replace(/[:.]/g, "-");
fs.writeFileSync(path.join(resultsDirectory, `node-${stamp}.json`), JSON.stringify(report, null, 2) + "\n");
fs.writeFileSync(path.join(resultsDirectory, "node-latest.json"), JSON.stringify(report, null, 2) + "\n");
for (const file of tempFiles) fs.rmSync(file, { force: true });
console.log(`\nWrote ${records.length} results to ${path.relative(repositoryRoot, path.join(resultsDirectory, `node-${stamp}.json`))}`);
process.exit(0);
