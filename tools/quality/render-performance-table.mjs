#!/usr/bin/env node
/**
 * Records the measurements behind the "Typical costs" section of docs/guides/performance.md. It takes a real run's
 * output - the BenchmarkDotNet summary that convert-benchmark-baseline.mjs writes and/or the JS harness's Node results
 * (benchmarks/js `node-latest.json`) - keeps only the cases the tables show, writes them to the committed record
 * benchmarks/CStructSharp.Benchmarks/typical-costs.json, and re-renders the page block. Numbers are never typed by
 * hand: sync-documentation-facts.mjs --check (run by the documentation validator) fails when the page and the record
 * disagree. `--filters` prints the BenchmarkDotNet `--filter` globs that measure exactly the shown cases.
 *
 *   node tools/quality/render-performance-table.mjs [--summary <summary.json>] [--js <node-latest.json>]
 *   node tools/quality/render-performance-table.mjs --filters
 *   node tools/quality/render-performance-table.mjs --self-test
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";
import {
  ASYNC_ROWS,
  END_MARKER,
  GENERATED_ROWS,
  JS_ROWS,
  MANAGED_ROWS,
  START_MARKER,
  TYPICAL_COSTS_PATH,
  benchmarkFilters,
  formatDuration,
  renderBlock,
  replaceBlock,
  trimJavaScriptResults,
  trimManagedSummary,
} from "../lib/performance-table.mjs";

const options = parseArguments(
  process.argv.slice(2),
  { summary: "string", js: "string", filters: "flag", "self-test": "flag" },
  { defaults: { filters: false, "self-test": false } },
);

/** Reads and parses a JSON file. */
function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8"));
}

/**
 * Checks the renderer against synthetic inputs: number formatting, captions, optional sections, rejection of partial
 * benchmark sets, trimming, filters, and marker replacement. Throws on the first failed check.
 */
function selfTest() {
  const summary = {
    schemaVersion: 1,
    generatedAtUtc: "2026-09-18T06:45:01.088Z",
    hostEnvironment: { ProcessorName: "Test CPU", RuntimeVersion: ".NET 10.0.0", OsVersion: "Test OS" },
    benchmarks: [
      ...MANAGED_ROWS.map((row, index) => ({ type: row.type, method: row.method, parameters: row.parameters, medianNanoseconds: 10 ** (index % 7) * 1.5, allocatedBytes: 100 * index })),
      ...GENERATED_ROWS.map((row, index) => ({ type: "GeneratedBenchmarks", method: row.method, parameters: "", medianNanoseconds: 50 + index, allocatedBytes: index === 2 ? 0 : 48 })),
      ...ASYNC_ROWS.map((row, index) => ({ type: row.type, method: row.method, parameters: "", medianNanoseconds: 300 + index, allocatedBytes: index === ASYNC_ROWS.length - 1 ? 0 : 784 })),
    ],
  };
  const js = { schemaVersion: 1, environment: { capturedAtUtc: "2026-09-18T12:52:14.072Z", node: { node: "22.0.0" } }, results: JS_ROWS.map((row) => ({ name: row.name, medianNanoseconds: 4000 })) };
  const runtime = { files: 26, bytes: 4830389, gzipBytes: 1833911 };
  const block = renderBlock({ summary, js, runtime });
  assertCondition(block.includes("| 1.50 ns | 0 B |") && block.includes("| 1.50 ms |"), "Self-test: duration formatting is wrong.");
  assertCondition(block.includes("4.6 MiB") && block.includes("1.7 MiB"), "Self-test: byte formatting is wrong.");
  assertCondition(block.includes("Measured 2026-09-18 on Test CPU, .NET 10.0.0, Test OS."), "Self-test: the caption is wrong.");
  assertCondition(block.includes("| Generated view of the same record (every member read, nothing allocated) | 52.0 ns | 0 B |"), "Self-test: the generated table is wrong.");
  assertCondition(!renderBlock({ summary: { ...summary, benchmarks: summary.benchmarks.slice(0, MANAGED_ROWS.length) } }).includes("GeneratedBenchmarks"), "Self-test: a summary without generated cases must render no generated table.");
  assertCondition(block.includes("| Generated view enumerator (`RootView.Enumerate`) over the same 256 records | 315 ns | 0 B |"), "Self-test: the async and sequences table is wrong.");
  const withoutSequences = renderBlock({ summary: { ...summary, benchmarks: summary.benchmarks.filter((benchmark) => benchmark.type !== "SequenceBenchmarks") } });
  assertCondition(!withoutSequences.includes("AsyncBenchmarks") && withoutSequences.includes("GeneratedBenchmarks"), "Self-test: a summary without the sequence cases must render no async table.");

  /** Returns whether rendering `benchmarks` in place of the full set throws. */
  const rejects = (benchmarks) => {
    try {
      renderBlock({ summary: { ...summary, benchmarks } });
      return false;
    } catch {
      return true;
    }
  };
  assertCondition(rejects(summary.benchmarks.filter((benchmark) => benchmark.method !== ASYNC_ROWS[3].method)), "Self-test: a partial async set was not rejected.");
  assertCondition(rejects(summary.benchmarks.slice(0, MANAGED_ROWS.length + 1)), "Self-test: a partial generated set was not rejected.");
  assertCondition(rejects(summary.benchmarks.slice(1)), "Self-test: a missing case was not rejected.");
  assertCondition(block.includes("Measured 2026-09-18 in Node 22.0.0 with"), "Self-test: the JS caption is wrong.");

  // The committed record keeps only the shown cases, and renders exactly what the full inputs render.
  const extra = { ...summary, benchmarks: [...summary.benchmarks, { type: "Other", method: "Unused", parameters: "", medianNanoseconds: 1, allocatedBytes: 1 }] };
  const trimmed = trimManagedSummary(extra);
  assertCondition(trimmed.benchmarks.length === MANAGED_ROWS.length + GENERATED_ROWS.length + ASYNC_ROWS.length, "Self-test: trimming kept an unused case.");
  assertCondition(renderBlock({ summary: trimmed, js: trimJavaScriptResults(js), runtime }) === block, "Self-test: the trimmed record renders differently.");
  const filters = benchmarkFilters();
  assertCondition(filters.includes('*.CompileBenchmarks.Compile(Fixture: "real-png")') && filters.includes("*.AddressBenchmarks.ResolveFixedNestedArray(Index: 127)") && filters.includes("*.GeneratedBenchmarks.Generated_PrimRecord_View"), "Self-test: the benchmark filters are wrong.");

  const page = `# Title\n\n${START_MARKER}\nold\n${END_MARKER}\n\nAfter.\n`;
  const replaced = replaceBlock(page, block);
  assertCondition(replaced.startsWith(`# Title\n\n${START_MARKER}\nThe medians`), "Self-test: the block was not replaced.");
  assertCondition(replaced.endsWith(`${END_MARKER}\n\nAfter.\n`) && !replaced.includes("\nold\n"), "Self-test: the page outside the block changed.");
  assertCondition(replaceBlock(replaced, block) === replaced, "Self-test: rendering is not idempotent.");
  assertCondition(formatDuration(999) === "999 ns" && formatDuration(1000) === "1.00 µs" && formatDuration(123456) === "123 µs", "Self-test: unit boundaries are wrong.");
  console.log("render-performance-table self-test passed.");
}

await main(() => {
  if (options["self-test"]) return selfTest();
  if (options.filters) {
    for (const filter of benchmarkFilters()) console.log(filter);
    return;
  }

  assertCondition(options.summary || options.js, "Pass --summary <summary.json> and/or --js <node-latest.json> from a real run.");
  const recordPath = path.join(repositoryRoot, TYPICAL_COSTS_PATH);
  const record = fs.existsSync(recordPath) ? readJson(recordPath) : { schemaVersion: 1 };

  // Trim (and so validate) every input before writing, so a run that lacks a shown case leaves the record unchanged.
  if (options.summary) record.managed = trimManagedSummary(readJson(options.summary));
  if (options.js) record.javascript = trimJavaScriptResults(readJson(options.js));
  assertCondition(record.managed, "The record has no managed measurements yet; pass --summary.");
  renderBlock({ summary: record.managed, js: record.javascript });
  fs.writeFileSync(recordPath, `${JSON.stringify(record, null, 2)}\n`);
  console.log(`Recorded ${[options.summary && "the managed cases", options.js && "the JavaScript cases"].filter(Boolean).join(" and ")} in ${TYPICAL_COSTS_PATH}.`);

  // The page block is rendered by the facts tool, which also adds the runtime size from the npm package record.
  const sync = runCommand(process.execPath, [path.join(repositoryRoot, "tools/documentation/sync-documentation-facts.mjs"), "--write"]);
  process.stdout.write(sync.stdout);
});
