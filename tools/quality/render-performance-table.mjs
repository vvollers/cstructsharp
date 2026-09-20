#!/usr/bin/env node
/**
 * Renders the "Typical costs" section of docs/guides/performance.md from measured data: the BenchmarkDotNet
 * summary that convert-benchmark-baseline.mjs writes (a curated subset of its cases, and the generated-code
 * comparison when the summary holds GeneratedBenchmarks), the JS harness's Node
 * results (benchmarks/js), and the web artifact measurement (tools/quality/measure-web-artifacts.mjs) for the
 * runtime size. The page keeps the rendered block between two HTML comment markers; everything outside them is
 * hand-written. `--check` fails when the page's block differs from what the inputs render, so a stale table is
 * caught the same way a stale snapshot is.
 *
 *   node tools/quality/render-performance-table.mjs --summary <summary.json> [--js <node results.json>]
 *     [--web <web.json>] [--page docs/guides/performance.md] [--check] [--self-test]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";

const options = parseArguments(
  process.argv.slice(2),
  { summary: "string", js: "string", web: "string", page: "string", check: "flag", "self-test": "flag" },
  { defaults: { page: path.join(repositoryRoot, "docs/guides/performance.md"), check: false, "self-test": false } },
);

export const START_MARKER = "<!-- typical-costs:start -->";
export const END_MARKER = "<!-- typical-costs:end -->";

/**
 * The managed rows, in page order. Each names one BenchmarkDotNet case (type, method, parameters) and says in
 * user terms what it measures; the summary supplies the median and the allocation.
 */
export const MANAGED_ROWS = [
  { type: "CompilationBenchmarks", method: "CompileSmall", parameters: "", operation: "Compile a two-field struct (`struct root { uint8 kind; uint32 value; };`)" },
  { type: "CompileBenchmarks", method: "Compile", parameters: "Fixture=real-png", operation: "Compile the PNG header fixture (an enum and two structs)" },
  { type: "CompileBenchmarks", method: "GetOrCompile_Hit", parameters: "Fixture=real-png", operation: "`GetOrCompile` hit for the same source (cache lookup)" },
  { type: "ReadBenchmarks", method: "ParseSmallRootMemory", parameters: "", operation: "`Parse` a five-byte record with a `count`-sized array from memory" },
  { type: "ReadBenchmarks", method: "ReadTypedSmallRootMemory", parameters: "", operation: "`ReadValue<T>` of the same record into a mapped class" },
  { type: "ReadBenchmarks", method: "ReadSelectedScalarTypedMemory", parameters: "", operation: "`ReadValue<ushort>` of one selected field" },
  { type: "ReadBenchmarks", method: "ParsePrimitiveArray1KiB", parameters: "", operation: "`Parse` a 1 KiB `uint8[1024]` (one `PrimitiveArray`)" },
  { type: "AddressBenchmarks", method: "ResolveFixedNestedArray", parameters: "Index=127", operation: "`ResolveAddress` of `items[127]` in a fixed nested array" },
  { type: "DebugBenchmarks", method: "ParseWithDebug", parameters: "Fixture=real-png", operation: "`ParseWithDebug` of the PNG fixture (byte ranges for every value)" },
  { type: "MalformedBenchmarks", method: "ParseAndCatch", parameters: "Fixture=malformed-truncated", operation: "Truncated input: `Parse` throws and the caller catches" },
  { type: "WriteAndUpdateBenchmarks", method: "SerializePocoToSpan", parameters: "", operation: "`Serialize` a mapped class into a caller-provided span" },
  { type: "WriteAndUpdateBenchmarks", method: "UpdatePointerTarget", parameters: "", operation: "`Update` one value behind a pointer in place" },
  { type: "LargeStreamBenchmarks", method: "Parse16M_MemoryStream", parameters: "", operation: "`Parse` a 16 MiB record from a `MemoryStream`" },
];

/**
 * The generated-code rows, in page order: the same bytes read four ways (runtime `Parse`, generated `Parse`, a
 * generated view, hand-written `BinaryPrimitives` code) for the reference record and the nested fixture, then the
 * runtime/generated pairs for a write, an in-place update, and a debug read. The table is rendered only when the
 * summary contains `GeneratedBenchmarks` (a whole-suite conversion does).
 */
export const GENERATED_ROWS = [
  { method: "Runtime_PrimRecord_Parse", operation: "Runtime `Parse` of the 28-byte primitives record (`StructValue`)" },
  { method: "Generated_PrimRecord_Parse", operation: "Generated `Parse` of the same record (the typed class)" },
  { method: "Generated_PrimRecord_View", operation: "Generated view of the same record (every member read, nothing allocated)" },
  { method: "HandWritten_PrimRecord", operation: "Hand-written `BinaryPrimitives` reader of the same record" },
  { method: "Runtime_Nested256_Parse", operation: "Runtime `Parse` of 256 nested records (6,400 bytes)" },
  { method: "Generated_Nested256_Parse", operation: "Generated `Parse` of the 256 nested records" },
  { method: "Generated_Nested256_View", operation: "Generated view over the 256 nested records (one view per element by offset)" },
  { method: "HandWritten_Nested256", operation: "Hand-written reader of the 256 nested records" },
  { method: "Runtime_PrimRecord_Serialize", operation: "Runtime `Serialize` of the record from a `StructValue`" },
  { method: "Generated_PrimRecord_Serialize", operation: "Generated `Serialize` of the record from the typed class" },
  { method: "Runtime_PrimRecord_Update", operation: "Runtime `Update` of one field by path" },
  { method: "Generated_PrimRecord_Update", operation: "Generated typed setter for the same field (`Update.C`)" },
  { method: "Runtime_PrimRecord_ParseWithDebug", operation: "Runtime `ParseWithDebug` of the record" },
  { method: "Generated_PrimRecord_ParseWithDebug", operation: "Generated `ParseWithDebug` (the generated value plus the runtime's ranges)" },
];

/** The JavaScript rows, in page order; the Node results supply the median. */
export const JS_ROWS = [
  { name: "public.parse.prim-le-record", operation: "`parse` of a fixed 28-byte record (JavaScript fast path, no WebAssembly call)" },
  { name: "public.parse.real-png", operation: "`parse` of the PNG header fixture, 33 bytes (fast path)" },
  { name: "public.parse.nested-x256", operation: "`parse` of 256 nested records, 6,400 bytes (fast path)" },
  { name: "public.parse.strings-1024", operation: "`parse` of a record with four terminated strings, 6,592 bytes (one WebAssembly call)" },
  { name: "public.parseWithDebug.prim-le-record", operation: "`parseWithDebug` of the 28-byte record (WebAssembly)" },
  { name: "public.serialize.prim-le-record", operation: "`serialize` of the 28-byte record (WebAssembly)" },
  { name: "public.update.scalar.28B", operation: "`update` of one scalar in the 28-byte record (WebAssembly)" },
];

const caseKey = (benchmark) => `${benchmark.type ?? ""}|${benchmark.method ?? ""}|${benchmark.parameters ?? ""}`;

/** Formats nanoseconds with three significant digits in the unit that keeps the number readable. */
export function formatDuration(nanoseconds) {
  const value = Number(nanoseconds);
  assertCondition(Number.isFinite(value) && value >= 0, `Invalid duration ${nanoseconds}.`);
  const units = [
    { limit: 1e3, divisor: 1, unit: "ns" },
    { limit: 1e6, divisor: 1e3, unit: "µs" },
    { limit: 1e9, divisor: 1e6, unit: "ms" },
    { limit: Infinity, divisor: 1e9, unit: "s" },
  ];
  const { divisor, unit } = units.find((entry) => value < entry.limit);
  const scaled = value / divisor;
  const digits = scaled >= 100 ? 0 : scaled >= 10 ? 1 : 2;
  return `${scaled.toFixed(digits)} ${unit}`;
}

/** Formats a byte count with thousands separators, or KiB/MiB above 64 KiB. */
export function formatBytes(bytes) {
  const value = Number(bytes);
  assertCondition(Number.isFinite(value) && value >= 0, `Invalid byte count ${bytes}.`);
  if (value >= 1024 * 1024) return `${(value / (1024 * 1024)).toFixed(1)} MiB`;
  if (value > 64 * 1024) return `${(value / 1024).toFixed(0)} KiB`;
  return `${value.toLocaleString("en-US")} B`;
}

function isoDate(value) {
  const date = new Date(value);
  assertCondition(!Number.isNaN(date.getTime()), `Invalid timestamp ${value}.`);
  return date.toISOString().slice(0, 10);
}

/** Renders the block (without the markers) from the three inputs; `js` and `web` are optional. */
export function renderBlock({ summary, js, web }) {
  assertCondition(summary?.schemaVersion === 1 && Array.isArray(summary.benchmarks), "The benchmark summary has an unsupported shape.");
  const host = summary.hostEnvironment ?? {};
  const byKey = new Map(summary.benchmarks.map((benchmark) => [caseKey(benchmark), benchmark]));
  const lines = [];
  lines.push(
    "The medians below come from the repository's BenchmarkDotNet cases (`benchmarks/CStructSharp.Benchmarks`,",
    "`Short` job, Release build) and are regenerated by `tools/quality/render-performance-table.mjs`; they show",
    "the order of magnitude of each operation, not a guarantee. Every managed operation allocates its result plus",
    "roughly one kilobyte of per-call state (options snapshot, operation context, budget stream), so a loop over",
    "millions of records is dominated by that constant unless the records are read as one array.",
    "",
    "| Operation | Median | Allocated |",
    "| --- | ---: | ---: |",
  );
  for (const row of MANAGED_ROWS) {
    const benchmark = byKey.get(`${row.type}|${row.method}|${row.parameters}`);
    assertCondition(benchmark, `The summary has no case ${row.type}.${row.method} [${row.parameters}].`);
    lines.push(`| ${row.operation} | ${formatDuration(benchmark.medianNanoseconds)} | ${formatBytes(benchmark.allocatedBytes)} |`);
  }
  const machine = [host.ProcessorName, host.RuntimeVersion, host.OsVersion].filter(Boolean).join(", ");
  lines.push("", `Measured ${isoDate(summary.generatedAtUtc)} on ${machine || "an unrecorded machine"}.`);

  if (byKey.has(`GeneratedBenchmarks|${GENERATED_ROWS[0].method}|`)) {
    lines.push(
      "",
      "A layout on a `[CStructLayout]` class is read by generated code instead ([generated code](generated/index.md)).",
      "The same bytes four ways - the runtime `Parse`, the generated `Parse`, a generated view, and hand-written",
      "`BinaryPrimitives` code - then the runtime/generated pairs for a write, an update, and a debug read",
      "(`GeneratedBenchmarks`):",
      "",
      "| Operation | Median | Allocated |",
      "| --- | ---: | ---: |",
    );
    for (const row of GENERATED_ROWS) {
      const benchmark = byKey.get(`GeneratedBenchmarks|${row.method}|`);
      assertCondition(benchmark, `The summary has no case GeneratedBenchmarks.${row.method}.`);
      lines.push(`| ${row.operation} | ${formatDuration(benchmark.medianNanoseconds)} | ${formatBytes(benchmark.allocatedBytes)} |`);
    }
    lines.push(
      "",
      "The generated `Parse` allocates the typed class and nothing else; the view allocates nothing and sits next to",
      "the hand-written reader because it is the same code with the offsets filled in. `ParseWithDebug` costs a",
      "runtime read on top of the generated one (the ranges come from the runtime). Use the generated path when the",
      "layout is in the program's source and the read is hot; [runtime or generated?](generated/choosing-runtime-or-generated.md)",
      "has the full decision table.",
    );
  }

  if (js) {
    assertCondition(js.schemaVersion === 1 && Array.isArray(js.results), "The JS results have an unsupported shape.");
    const results = new Map(js.results.map((result) => [result.name, result]));
    const engine = js.environment?.node?.node;
    lines.push(
      "",
      "The JavaScript package pays a WebAssembly crossing per call unless the layout is fully fixed and no option",
      "is set, in which case `parse` reads it in JavaScript (see [many records in one call](browser/large-data.md#many-records-in-one-call)):",
      "",
      "| Operation | Median |",
      "| --- | ---: |",
    );
    for (const row of JS_ROWS) {
      const result = results.get(row.name);
      assertCondition(result, `The JS results have no case ${row.name}.`);
      lines.push(`| ${row.operation} | ${formatDuration(result.medianNanoseconds)} |`);
    }
    const captured = js.environment?.capturedAtUtc;
    lines.push("", `Measured ${captured ? isoDate(captured) : isoDate(summary.generatedAtUtc)} in Node${engine ? ` ${engine}` : ""} with \`benchmarks/js\` (\`npm run bench:node\`).`);
  }

  if (web) {
    const values = web.values ?? {};
    assertCondition(Number.isFinite(values.wasmBytes) && Number.isFinite(values.wasmGzipBytes), "The web measurement has no WASM sizes.");
    lines.push(
      "",
      `The browser runtime (the WASM publication the npm package and the standalone bundle ship) is ${formatBytes(values.wasmBytes)}` +
        ` across ${values.wasmFiles ?? "its"} files, ${formatBytes(values.wasmGzipBytes)} gzip-compressed; it is downloaded once and cached by the browser.`,
    );
  }

  return `${lines.join("\n")}\n`;
}

/** Replaces the block between the markers in `page`; the markers stay. */
export function replaceBlock(page, block) {
  const start = page.indexOf(START_MARKER);
  const end = page.indexOf(END_MARKER);
  assertCondition(start >= 0 && end > start, `The page has no ${START_MARKER} … ${END_MARKER} block.`);
  return `${page.slice(0, start + START_MARKER.length)}\n${block}${page.slice(end)}`;
}

function readJson(file) {
  return JSON.parse(fs.readFileSync(file, "utf8"));
}

function selfTest() {
  const summary = {
    schemaVersion: 1,
    generatedAtUtc: "2026-09-18T06:45:01.088Z",
    hostEnvironment: { ProcessorName: "Test CPU", RuntimeVersion: ".NET 10.0.0", OsVersion: "Test OS" },
    benchmarks: [
      ...MANAGED_ROWS.map((row, index) => ({ type: row.type, method: row.method, parameters: row.parameters, medianNanoseconds: 10 ** (index % 7) * 1.5, allocatedBytes: 100 * index })),
      ...GENERATED_ROWS.map((row, index) => ({ type: "GeneratedBenchmarks", method: row.method, parameters: "", medianNanoseconds: 50 + index, allocatedBytes: index === 2 ? 0 : 48 })),
    ],
  };
  const js = { schemaVersion: 1, environment: { capturedAtUtc: "2026-09-18T12:52:14.072Z", node: { node: "22.0.0" } }, results: JS_ROWS.map((row) => ({ name: row.name, medianNanoseconds: 4000 })) };
  const web = { values: { wasmFiles: 26, wasmBytes: 4830389, wasmGzipBytes: 1833911 } };
  const block = renderBlock({ summary, js, web });
  assertCondition(block.includes("| 1.50 ns | 0 B |") && block.includes("| 1.50 ms |"), "Self-test: duration formatting is wrong.");
  assertCondition(block.includes("4.6 MiB") && block.includes("1.7 MiB"), "Self-test: byte formatting is wrong.");
  assertCondition(block.includes("Measured 2026-09-18 on Test CPU, .NET 10.0.0, Test OS."), "Self-test: the caption is wrong.");
  assertCondition(block.includes("| Generated view of the same record (every member read, nothing allocated) | 52.0 ns | 0 B |"), "Self-test: the generated table is wrong.");
  assertCondition(!renderBlock({ summary: { ...summary, benchmarks: summary.benchmarks.slice(0, MANAGED_ROWS.length) } }).includes("GeneratedBenchmarks"), "Self-test: a summary without generated cases must render no generated table.");
  let partial = false;
  try {
    renderBlock({ summary: { ...summary, benchmarks: summary.benchmarks.slice(0, MANAGED_ROWS.length + 1) } });
  } catch {
    partial = true;
  }
  assertCondition(partial, "Self-test: a partial generated set was not rejected.");
  assertCondition(block.includes("Measured 2026-09-18 in Node 22.0.0 with"), "Self-test: the JS caption is wrong.");
  const page = `# Title\n\n${START_MARKER}\nold\n${END_MARKER}\n\nAfter.\n`;
  const replaced = replaceBlock(page, block);
  assertCondition(replaced.startsWith(`# Title\n\n${START_MARKER}\n| `) || replaced.startsWith(`# Title\n\n${START_MARKER}\nThe medians`), "Self-test: the block was not replaced.");
  assertCondition(replaced.endsWith(`${END_MARKER}\n\nAfter.\n`) && !replaced.includes("\nold\n"), "Self-test: the page outside the block changed.");
  assertCondition(replaceBlock(replaced, block) === replaced, "Self-test: rendering is not idempotent.");
  let rejected = false;
  try {
    renderBlock({ summary: { ...summary, benchmarks: summary.benchmarks.slice(1) } });
  } catch {
    rejected = true;
  }
  assertCondition(rejected, "Self-test: a missing case was not rejected.");
  assertCondition(formatDuration(999) === "999 ns" && formatDuration(1000) === "1.00 µs" && formatDuration(123456) === "123 µs", "Self-test: unit boundaries are wrong.");
  console.log("render-performance-table self-test passed.");
}

await main(() => {
  if (options["self-test"]) {
    selfTest();
    return;
  }

  assertCondition(options.summary, "Option --summary is required.");
  const block = renderBlock({
    summary: readJson(options.summary),
    js: options.js ? readJson(options.js) : undefined,
    web: options.web ? readJson(options.web) : undefined,
  });
  const page = fs.readFileSync(options.page, "utf8");
  const rendered = replaceBlock(page, block);
  if (options.check) {
    assertCondition(rendered === page, `${path.relative(repositoryRoot, options.page)} is stale: re-run render-performance-table.mjs with the current inputs.`);
    console.log("Typical costs table is current.");
    return;
  }

  fs.writeFileSync(options.page, rendered);
  console.log(`Rendered ${MANAGED_ROWS.length} managed rows${block.includes("GeneratedBenchmarks") ? `, ${GENERATED_ROWS.length} generated rows,` : ""}${options.js ? ` and ${JS_ROWS.length} JavaScript rows` : ""} into ${path.relative(repositoryRoot, options.page)}.`);
});
