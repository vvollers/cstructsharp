/**
 * Renders the "Typical costs" section of docs/guides/performance.md from recorded measurements. The inputs are the
 * committed record benchmarks/CStructSharp.Benchmarks/typical-costs.json (a curated subset of a converted
 * BenchmarkDotNet summary and of the JS harness's Node results; tools/quality/render-performance-table.mjs writes it)
 * and the committed npm package record benchmarks/npm-package.json (the runtime size; sync-documentation-facts.mjs
 * writes it from `npm run pack:npm` output). tools/documentation/sync-documentation-facts.mjs renders and checks the
 * page block; nothing here measures.
 */
import { assertCondition } from "./tooling.mjs";

export const START_MARKER = "<!-- typical-costs:start -->";
export const END_MARKER = "<!-- typical-costs:end -->";

/** The committed record of the measured cases the tables show, relative to the repository root. */
export const TYPICAL_COSTS_PATH = "benchmarks/CStructSharp.Benchmarks/typical-costs.json";

/**
 * The managed rows, in page order. Each names one BenchmarkDotNet case (type, method, parameters) and says in
 * user terms what it measures; the summary supplies the median and the allocation.
 */
export const MANAGED_ROWS = [
  { type: "CompileBenchmarks", method: "Compile", parameters: "Fixture=compile-small", operation: "Compile a two-field struct (`struct root { uint8 kind; uint32 value; };`)" },
  { type: "CompileBenchmarks", method: "Compile", parameters: "Fixture=real-png", operation: "Compile the PNG header fixture (an enum and two structs)" },
  { type: "CompileBenchmarks", method: "GetOrCompile_Hit", parameters: "Fixture=real-png", operation: "`GetOrCompile` hit for the same source (cache lookup)" },
  { type: "ReadBenchmarks", method: "ParseSmallRootMemory", parameters: "", operation: "`Parse` a five-byte record with a `count`-sized array from memory" },
  { type: "ReadBenchmarks", method: "ReadTypedSmallRootMemory", parameters: "", operation: "`ReadValue<T>` of the same record into a mapped class" },
  { type: "ReadBenchmarks", method: "ReadSelectedScalarTypedMemory", parameters: "", operation: "`ReadValue<ushort>` of one selected field" },
  { type: "ReadBenchmarks", method: "ParsePrimitiveArray1KiB", parameters: "", operation: "`Parse` a 1 KiB `uint8[1024]` (one `PrimitiveArray`)" },
  { type: "AddressBenchmarks", method: "ResolveFixedNestedArray", parameters: "Index=127", operation: "`ResolveAddress` of `items[127]` in a fixed nested array" },
  { type: "DebugBenchmarks", method: "ParseWithDebug", parameters: "Fixture=real-png", operation: "`ParseWithDebug` of the PNG fixture (byte ranges for every value)" },
  { type: "MalformedBenchmarks", method: "ParseAndCatch", parameters: "Fixture=malformed-truncated", operation: "Truncated input: `Parse` throws and the caller catches" },
  { type: "WriteBenchmarks", method: "Serialize_Prim_Poco_ToSpan", parameters: "", operation: "`Serialize` a mapped class into a caller-provided span" },
  { type: "UpdateBenchmarks", method: "Update_PointerTarget", parameters: "", operation: "`Update` one value behind a pointer in place" },
  { type: "LargeStreamBenchmarks", method: "Parse16M_MemoryStream", parameters: "", operation: "`Parse` a 16 MiB record from a `MemoryStream`" },
];

/**
 * The generated-code rows, in page order: the same bytes read four ways (runtime `Parse`, generated `Parse`, a
 * generated view, hand-written `BinaryPrimitives` code) for the reference record and the nested fixture, then the
 * runtime/generated pairs for a write, an in-place update, and a debug read. The table is rendered only when the
 * summary contains `GeneratedBenchmarks`.
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

/**
 * The async and record-sequence rows, in page order: the stream read against its awaitable twin (in place, through
 * a copy, from a file), the write and update pairs, then 256 records through a `Parse` loop, `ParseMany`, the
 * generated `Records`, and the view enumerator against the hand-written view loop. Rendered only when the summary
 * contains `AsyncBenchmarks` and `SequenceBenchmarks`.
 */
export const ASYNC_ROWS = [
  { type: "AsyncBenchmarks", method: "Runtime_PrimRecord_ParseStream", operation: "Runtime `Parse(Stream)` of the 28-byte record from a `MemoryStream`" },
  { type: "AsyncBenchmarks", method: "Runtime_PrimRecord_ParseAsync_MemoryStream", operation: "Runtime `ParseAsync` of the same stream (read in place, the task already complete)" },
  { type: "AsyncBenchmarks", method: "Runtime_PrimRecord_ParseAsync_HiddenBuffer", operation: "Runtime `ParseAsync` of a stream that hides its buffer (one pooled copy)" },
  { type: "AsyncBenchmarks", method: "Runtime_PrimRecord_ParseAsync_File", operation: "Runtime `ParseAsync` of a `FileStream` opened for asynchronous I/O" },
  { type: "AsyncBenchmarks", method: "Runtime_PrimRecord_WriteStream", operation: "Runtime `Write(Stream)` of the record" },
  { type: "AsyncBenchmarks", method: "Runtime_PrimRecord_WriteAsync", operation: "Runtime `WriteAsync` of the record (serialized first, one `WriteAsync`)" },
  { type: "AsyncBenchmarks", method: "Runtime_PrimRecord_UpdateStream", operation: "Runtime `Update(Stream)` of one field" },
  { type: "AsyncBenchmarks", method: "Runtime_PrimRecord_UpdateAsync", operation: "Runtime `UpdateAsync` of the same field (the region buffered, the changed run written back)" },
  { type: "AsyncBenchmarks", method: "Generated_PrimRecord_ParseStream", operation: "Generated `Parse(Stream)` of the record" },
  { type: "AsyncBenchmarks", method: "Generated_PrimRecord_ParseAsync", operation: "Generated `ParseAsync` of the same stream" },
  { type: "SequenceBenchmarks", method: "Runtime_Records256_ParseLoop", operation: "Runtime `Parse` in a loop over 256 consecutive records (7,168 bytes)" },
  { type: "SequenceBenchmarks", method: "Runtime_Records256_ParseMany", operation: "Runtime `ParseMany` over the same 256 records" },
  { type: "SequenceBenchmarks", method: "Generated_Records256_ParseLoop", operation: "Generated `Parse` in a loop over the 256 records" },
  { type: "SequenceBenchmarks", method: "Generated_Records256_Records", operation: "Generated `Records` over the same 256 records" },
  { type: "SequenceBenchmarks", method: "HandWritten_Records256_ViewLoop", operation: "Hand-written offset loop over 256 views (two members read each)" },
  { type: "SequenceBenchmarks", method: "Generated_Records256_ViewEnumerator", operation: "Generated view enumerator (`RootView.Enumerate`) over the same 256 records" },
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

/**
 * Every BenchmarkDotNet case a table row names, as `(type, method, parameters)` descriptors; the generated rows all
 * belong to `GeneratedBenchmarks`.
 * @returns {{ type: string, method: string, parameters: string }[]} The cases, in page order.
 */
export function managedCases() {
  return [
    ...MANAGED_ROWS,
    ...GENERATED_ROWS.map((row) => ({ ...row, type: "GeneratedBenchmarks", parameters: "" })),
    ...ASYNC_ROWS.map((row) => ({ ...row, parameters: "" })),
  ].map(({ type, method, parameters }) => ({ type, method, parameters }));
}

/**
 * BenchmarkDotNet `--filter` globs that select exactly the cases the tables show: `*.Type.Method` for an
 * unparameterized case and `*.Type.Method(Name: value)` for one parameter value (strings quoted, as BenchmarkDotNet
 * prints them).
 * @returns {string[]} One glob per case, without duplicates.
 */
export function benchmarkFilters() {
  const filters = managedCases().map(({ type, method, parameters }) => {
    if (!parameters) return `*.${type}.${method}`;
    const [name, value] = parameters.split("=");
    return `*.${type}.${method}(${name}: ${/^\d+$/.test(value) ? value : `"${value}"`})`;
  });
  return [...new Set(filters)];
}

/** Builds the key (type, method, parameters) that matches a table row to its BenchmarkDotNet summary case. */
const caseKey = (benchmark) => `${benchmark.type ?? ""}|${benchmark.method ?? ""}|${benchmark.parameters ?? ""}`;

/**
 * Reduces a converted BenchmarkDotNet summary (convert-benchmark-baseline.mjs) to the cases the tables show, with
 * the fields the page uses, so the committed record stays small and diffs only where a shown number changes.
 * @param {object} summary A schemaVersion 1 summary.
 * @returns {object} The same shape with only the shown cases (median and allocation).
 * @throws {Error} When the summary lacks a shown case.
 */
export function trimManagedSummary(summary) {
  assertCondition(summary?.schemaVersion === 1 && Array.isArray(summary.benchmarks), "The benchmark summary has an unsupported shape.");
  const byKey = new Map(summary.benchmarks.map((benchmark) => [caseKey(benchmark), benchmark]));
  const host = summary.hostEnvironment ?? {};
  return {
    schemaVersion: 1,
    generatedAtUtc: summary.generatedAtUtc,
    hostEnvironment: { ProcessorName: host.ProcessorName, RuntimeVersion: host.RuntimeVersion, OsVersion: host.OsVersion },
    benchmarks: managedCases().map((row) => {
      const benchmark = byKey.get(caseKey(row));
      assertCondition(benchmark, `The summary has no case ${row.type}.${row.method} [${row.parameters}].`);
      return { ...row, medianNanoseconds: benchmark.medianNanoseconds, allocatedBytes: benchmark.allocatedBytes };
    }),
  };
}

/**
 * Reduces the JS harness's Node results (benchmarks/js `node-latest.json`) to the rows the table shows.
 * @param {object} js A schemaVersion 1 results document.
 * @returns {object} The capture time, the Node version, and each shown case's median.
 * @throws {Error} When the results lack a shown case.
 */
export function trimJavaScriptResults(js) {
  assertCondition(js?.schemaVersion === 1 && Array.isArray(js.results), "The JS results have an unsupported shape.");
  const results = new Map(js.results.map((result) => [result.name, result]));
  return {
    schemaVersion: 1,
    environment: { capturedAtUtc: js.environment?.capturedAtUtc, node: { node: js.environment?.node?.node } },
    results: JS_ROWS.map((row) => {
      const result = results.get(row.name);
      assertCondition(result, `The JS results have no case ${row.name}.`);
      return { name: row.name, medianNanoseconds: result.medianNanoseconds };
    }),
  };
}

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

/**
 * Returns the `YYYY-MM-DD` UTC date of a timestamp.
 * @throws {Error} When the timestamp cannot be parsed.
 */
function isoDate(value) {
  const date = new Date(value);
  assertCondition(!Number.isNaN(date.getTime()), `Invalid timestamp ${value}.`);
  return date.toISOString().slice(0, 10);
}

/**
 * Renders the block (without the markers).
 * @param {{ summary: object, js?: object, runtime?: { files: number, bytes: number, gzipBytes: number } }} inputs
 *   The managed summary, the optional Node results, and the optional runtime size (files, raw bytes, summed
 *   per-file gzip bytes) from the npm package record.
 * @returns {string} The Markdown between the markers, ending in a newline.
 * @throws {Error} When an input lacks a row's case or has an unsupported shape.
 */
export function renderBlock({ summary, js, runtime }) {
  assertCondition(summary?.schemaVersion === 1 && Array.isArray(summary.benchmarks), "The benchmark summary has an unsupported shape.");
  const host = summary.hostEnvironment ?? {};
  const byKey = new Map(summary.benchmarks.map((benchmark) => [caseKey(benchmark), benchmark]));
  const lines = [];
  lines.push(
    "The medians below come from the repository's BenchmarkDotNet cases (`benchmarks/CStructSharp.Benchmarks`,",
    "`Short` job, Release build), recorded in `benchmarks/CStructSharp.Benchmarks/typical-costs.json`; they show",
    "the order of magnitude of each operation, not a guarantee. Most managed operations allocate their result plus",
    "some per-call state (options snapshot, variable slots, budget stream); the Allocated column shows the total. A",
    "whole fixed-layout struct read from or written to memory skips that state and allocates only its result.",
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

  if (byKey.has(`${ASYNC_ROWS[0].type}|${ASYNC_ROWS[0].method}|`) && byKey.has(`SequenceBenchmarks|${ASYNC_ROWS[ASYNC_ROWS.length - 1].method}|`)) {
    lines.push(
      "",
      "The awaitable forms read the stream with `ReadAsync` into a pooled buffer and run the same reader over it, so",
      "their cost is the synchronous read plus the buffering and the state machine; a record sequence parses one",
      "record per step (`AsyncBenchmarks`, `SequenceBenchmarks`; [async and pipelines](async-and-pipelines.md),",
      "[sequences and TryParse](generated/sequences-and-try-parse.md)):",
      "",
      "| Operation | Median | Allocated |",
      "| --- | ---: | ---: |",
    );
    for (const row of ASYNC_ROWS) {
      const benchmark = byKey.get(`${row.type}|${row.method}|`);
      assertCondition(benchmark, `The summary has no case ${row.type}.${row.method}.`);
      lines.push(`| ${row.operation} | ${formatDuration(benchmark.medianNanoseconds)} | ${formatBytes(benchmark.allocatedBytes)} |`);
    }
    lines.push(
      "",
      "A `MemoryStream` that exposes its buffer is read in place and the `ValueTask` is already complete when it is",
      "returned; a file pays the real asynchronous I/O. `ParseMany` and the generated `Records` run the loop a caller",
      "would otherwise write; compare each with the plain loop above it to see what the convenience costs. The view",
      "enumerator allocates nothing, like the hand-written offset loop.",
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

  if (runtime) {
    assertCondition(Number.isFinite(runtime.bytes) && Number.isFinite(runtime.gzipBytes), "The runtime size has no byte counts.");
    lines.push(
      "",
      `The browser runtime (the WASM publication the npm package and the standalone bundle ship) is ${formatBytes(runtime.bytes)}` +
        ` across ${runtime.files ?? "its"} files, ${formatBytes(runtime.gzipBytes)} gzip-compressed; it is downloaded once and cached by the browser.`,
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
