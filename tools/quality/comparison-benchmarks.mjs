#!/usr/bin/env node
/**
 * Builds, verifies, and runs the serializer comparison in benchmarks/CStructSharp.Comparison, then renders the three
 * README tables from the measurements, each sorted fastest first. One 79-byte sensor record is deserialized and
 * serialized by CStructSharp (generated view, generated code, mapped class, runtime) and by other .NET options:
 * hand-written BinaryPrimitives, MemoryMarshal, BinaryReader/BinaryWriter, Marshal, Kaitai Struct, MemoryPack,
 * MessagePack-CSharp, protobuf-net, FlatSharp, and System.Text.Json. A data-dependent packet record is handled by
 * CStructSharp's generated code and runtime and by hand-written code.
 *
 * The measured summary is committed next to the project (results.json) so that the README block can be re-rendered
 * and checked without measuring again; README.md keeps the rendered block between two HTML comment markers and
 * everything outside them is hand-written.
 *
 *   node tools/quality/comparison-benchmarks.mjs [--job short|medium|long|default|dry] [--filter <pattern>]
 *       measure: build, verify every case, run BenchmarkDotNet, write results.json, render the README block
 *   node tools/quality/comparison-benchmarks.mjs --verify      build and check every case once, without measuring
 *   node tools/quality/comparison-benchmarks.mjs --render      re-render the README block from results.json
 *   node tools/quality/comparison-benchmarks.mjs --check       fail when the README block differs from results.json
 *   node tools/quality/comparison-benchmarks.mjs --regenerate-kaitai
 *       recompile Kaitai/SensorReading.g.cs from sensor_reading.ksy with the npm build of kaitai-struct-compiler
 *   node tools/quality/comparison-benchmarks.mjs --self-test   check the renderer against a synthetic summary
 *
 * Measuring takes several minutes (--job default, the BenchmarkDotNet default) or about one minute (--job short).
 * Run nothing else on the machine while it measures.
 */
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { COMPARISON_RESULTS_PATH, formatBytes, formatTime } from "../lib/comparison-summary.mjs";
import { assertCondition, main, parseArguments, repositoryRoot, runDotnet, runNpm } from "../lib/tooling.mjs";

export { formatBytes, formatTime };

const options = parseArguments(
  process.argv.slice(2),
  {
    job: "string",
    filter: "string",
    verify: "flag",
    render: "flag",
    check: "flag",
    "regenerate-kaitai": "flag",
    "self-test": "flag",
    readme: "string",
    summary: "string",
  },
  {
    defaults: {
      job: "default",
      filter: "*",
      readme: path.join(repositoryRoot, "README.md"),
      summary: path.join(repositoryRoot, COMPARISON_RESULTS_PATH),
    },
  },
);

const projectDirectory = path.join(repositoryRoot, "benchmarks/CStructSharp.Comparison");
const projectFile = path.join(projectDirectory, "CStructSharp.Comparison.csproj");
const artifactsDirectory = path.join(repositoryRoot, "artifacts/comparison");
const KAITAI_COMPILER_VERSION = "0.11.0";
const JS_YAML_VERSION = "4.1.0";

export const START_MARKER = "<!-- comparison-benchmarks:start -->";
export const END_MARKER = "<!-- comparison-benchmarks:end -->";

/**
 * The rows of the "same bytes" table. `deserialize` and `serialize` name methods of DeserializeBenchmarks and
 * SerializeBenchmarks; null means the approach has no such operation. `package` names the NuGet package whose
 * version the label shows, read from the project file. Every table is sorted by the rendered times (see
 * `sortRows`), so the order here only breaks exact ties.
 */
export const SAME_BYTES_ROWS = [
  { label: "CStructSharp generated view", deserialize: "CStructSharp_GeneratedView", serialize: null },
  { label: "CStructSharp generated `Parse` / `Serialize`", deserialize: "CStructSharp_GeneratedParse", serialize: "CStructSharp_GeneratedSerialize" },
  { label: "CStructSharp runtime `ReadValue<T>` / `Serialize` (layout-bound mapped class)", deserialize: "CStructSharp_RuntimeReadValue", serialize: "CStructSharp_RuntimeSerializeMapped" },
  { label: "CStructSharp runtime view (`CreateView` + accessors)", deserialize: "CStructSharp_RuntimeView", serialize: null },
  { label: "CStructSharp runtime `Parse` + accessors", deserialize: "CStructSharp_RuntimeParseAccessors", serialize: null },
  { label: "CStructSharp runtime `Parse` / `Serialize` (`StructValue`, path strings)", deserialize: "CStructSharp_RuntimeParse", serialize: "CStructSharp_RuntimeSerializeStructValue" },
  { label: "Hand-written `BinaryPrimitives`", deserialize: "HandWritten_BinaryPrimitives", serialize: "HandWritten_BinaryPrimitives" },
  { label: ".NET `MemoryMarshal.Read` / `Write`", deserialize: "Stock_MemoryMarshal", serialize: "Stock_MemoryMarshal" },
  { label: ".NET `BinaryReader` / `BinaryWriter`", deserialize: "Stock_BinaryReader", serialize: "Stock_BinaryWriter" },
  { label: ".NET `Marshal.PtrToStructure` / `StructureToPtr`", deserialize: "Stock_MarshalPtrToStructure", serialize: "Stock_MarshalStructureToPtr" },
  { label: "Kaitai Struct", package: "KaitaiStruct.Runtime.CSharp", deserialize: "Kaitai_Struct", serialize: null },
];

/**
 * The rows of the "own format" table. `size` names an entry of the sizes the verification pass records, and
 * `suffix` follows the package version in the label. CStructSharp's generated code is repeated as the reference
 * point.
 */
export const OWN_FORMAT_ROWS = [
  { label: "CStructSharp generated `Parse` / `Serialize`", format: "C layout", size: "Layout", deserialize: "CStructSharp_GeneratedParse", serialize: "CStructSharp_GeneratedSerialize" },
  { label: "MemoryPack", package: "MemoryPack", format: "MemoryPack", size: "MemoryPack", deserialize: "Own_MemoryPack", serialize: "Own_MemoryPack" },
  { label: "MessagePack-CSharp", package: "MessagePack", format: "MessagePack", size: "MessagePack", deserialize: "Own_MessagePack", serialize: "Own_MessagePack" },
  { label: "protobuf-net", package: "protobuf-net", format: "Protocol Buffers", size: "Protobuf", deserialize: "Own_ProtobufNet", serialize: "Own_ProtobufNet" },
  { label: "FlatSharp", suffix: " (lazy)", package: "FlatSharp.Runtime", format: "FlatBuffers", size: "FlatBuffers", deserialize: "Own_FlatSharp", serialize: "Own_FlatSharp" },
  { label: "System.Text.Json (source-generated)", format: "JSON", size: "Json", deserialize: "Own_SystemTextJson", serialize: "Own_SystemTextJson" },
];

/**
 * The rows of the "data-dependent record" table: methods of VariableBenchmarks, which read and write the packet
 * layout whose array length, text length, and conditional member depend on earlier fields.
 */
export const VARIABLE_ROWS = [
  { label: "CStructSharp generated `Parse` / `Serialize`", deserialize: "Generated_Parse", serialize: "Generated_Serialize" },
  { label: "CStructSharp runtime `ReadValue<T>` / `Serialize` (mapped class)", deserialize: "Runtime_ReadValueMapped", serialize: "Runtime_SerializeMapped" },
  { label: "CStructSharp runtime `Parse` + accessors / `Serialize` (`StructValue`)", deserialize: "Runtime_Parse", serialize: "Runtime_SerializeStructValue" },
  { label: "CStructSharp runtime `Parse` from a `MemoryStream` + accessors", deserialize: "Runtime_ParseStream", serialize: null },
  { label: "Hand-written `BinaryPrimitives`", deserialize: "HandWritten_Read", serialize: "HandWritten_Write" },
];

/**
 * Orders table rows fastest first: by deserialize median, then by serialize median; a missing operation sorts after
 * every measured one, and exact ties keep their declared order (Array.prototype.sort is stable).
 */
export function sortRows(rows, median) {
  /** The median of one operation, or infinity when the approach does not have it. */
  const key = (type, method) => (method === null ? Number.POSITIVE_INFINITY : median(type, method));
  /** Compares two keys without subtracting, so two missing operations (two infinities) compare equal. */
  const compare = (left, right) => (left < right ? -1 : left > right ? 1 : 0);
  return [...rows].sort(
    (a, b) =>
      compare(key(a.readType, a.deserialize), key(b.readType, b.deserialize)) ||
      compare(key(a.writeType, a.serialize), key(b.writeType, b.serialize)),
  );
}

/** Reads the `PackageReference` versions of the comparison project, keyed by package id. */
function packageVersions() {
  const text = fs.readFileSync(projectFile, "utf8");
  return Object.fromEntries([...text.matchAll(/<PackageReference Include="([^"]+)" Version="([^"]+)"/g)].map((match) => [match[1], match[2]]));
}

/** Returns a row's label with its package version appended, when the row names a package. */
function rowLabel(row, versions) {
  if (!row.package) return `${row.label}${row.suffix ?? ""}`;
  const version = versions[row.package];
  assertCondition(version, `The comparison project does not reference ${row.package}.`);
  return `${row.label} ${version}${row.suffix ?? ""}`;
}

/**
 * Renders a Markdown table with its columns padded to a common width, the way Prettier formats README.md, so a
 * rendered block needs no reformatting and the `--check` comparison is exact.
 * @param {string[]} headers The column headings.
 * @param {("left" | "right")[]} alignments Each column's alignment.
 * @param {string[][]} rows The cell text of each row.
 * @returns {string[]} The table's lines: heading, separator, then one line per row.
 */
export function markdownTable(headers, alignments, rows) {
  const widths = headers.map((header, column) => Math.max(3, header.length, ...rows.map((row) => row[column].length)));
  /** Pads one cell to its column width on the side its alignment leaves open. */
  const pad = (text, column) => (alignments[column] === "right" ? text.padStart(widths[column]) : text.padEnd(widths[column]));
  /** Joins padded cells into one table line. */
  const line = (cells) => `| ${cells.join(" | ")} |`;
  const separator = widths.map((width, column) => (alignments[column] === "right" ? `${"-".repeat(width - 1)}:` : "-".repeat(width)));
  return [line(headers.map(pad)), line(separator), ...rows.map((row) => line(row.map(pad)))];
}

/**
 * Renders the README block (markers included) from a summary: the environment line, then the "same bytes", "own
 * format" and "data-dependent record" tables, each sorted fastest first. Throws when the summary lacks a case a row
 * needs, so a renamed benchmark cannot leave a silent gap.
 */
export function renderBlock(summary, versions) {
  /** Looks up one measured case; throws when it is missing. */
  const result = (type, method) => {
    const measured = summary.cases[`${type}.${method}`];
    assertCondition(measured, `The summary has no result for ${type}.${method}; measure again.`);
    return measured;
  };

  /** Formats one measured cell pair (time, allocation) for a method, or dashes when the approach lacks it. */
  const cells = (type, method) => {
    if (method === null) return ["—", "—"];
    const measured = result(type, method);
    return [formatTime(measured.medianNanoseconds), formatBytes(measured.allocatedBytes)];
  };

  /** Attaches the benchmark classes to a table's rows and sorts them fastest first. */
  const sorted = (rows, readType, writeType) =>
    sortRows(
      rows.map((row) => ({ ...row, readType, writeType })),
      (type, method) => result(type, method).medianNanoseconds,
    );

  const environment = summary.environment;
  const lines = [
    START_MARKER,
    "",
    `Measured on ${environment.processor}, ${environment.os}, ${environment.runtime}, with BenchmarkDotNet ` +
      `${environment.benchmarkDotNet} (\`${environment.job}\` job) on ${environment.date}. Times are medians for one record; ` +
      "each table lists the fastest deserializer first.",
    "",
    "**Same bytes: the 79-byte C layout**",
    "",
    ...markdownTable(
      ["Approach", "Deserialize", "Allocated", "Serialize", "Allocated"],
      ["left", "right", "right", "right", "right"],
      sorted(SAME_BYTES_ROWS, "DeserializeBenchmarks", "SerializeBenchmarks").map((row) => [
        rowLabel(row, versions),
        ...cells(row.readType, row.deserialize),
        ...cells(row.writeType, row.serialize),
      ]),
    ),
    "",
    "**Same record, each library's own format**",
    "",
    ...markdownTable(
      ["Library", "Format", "Size", "Deserialize", "Allocated", "Serialize", "Allocated"],
      ["left", "left", "right", "right", "right", "right", "right"],
      sorted(OWN_FORMAT_ROWS, "DeserializeBenchmarks", "SerializeBenchmarks").map((row) => {
        const size = summary.sizes[row.size];
        assertCondition(Number.isInteger(size), `The summary has no encoded size for ${row.size}; measure again.`);
        return [rowLabel(row, versions), row.format, `${size} B`, ...cells(row.readType, row.deserialize), ...cells(row.writeType, row.serialize)];
      }),
    ),
    "",
    "**A record whose shape depends on its data (the `packet` layout above)**",
    "",
    ...markdownTable(
      ["Approach", "Deserialize", "Allocated", "Serialize", "Allocated"],
      ["left", "right", "right", "right", "right"],
      sorted(VARIABLE_ROWS, "VariableBenchmarks", "VariableBenchmarks").map((row) => [
        rowLabel(row, versions),
        ...cells(row.readType, row.deserialize),
        ...cells(row.writeType, row.serialize),
      ]),
    ),
    "",
    END_MARKER,
  ];
  return lines.join("\n");
}

/** Replaces the text between the markers (inclusive) in `page` with `block`; throws when the markers are missing. */
export function replaceBlock(page, block) {
  const start = page.indexOf(START_MARKER);
  const end = page.indexOf(END_MARKER);
  assertCondition(start >= 0 && end > start, `README.md must contain ${START_MARKER} followed by ${END_MARKER}.`);
  return page.slice(0, start) + block + page.slice(end + END_MARKER.length);
}

/**
 * Builds the committed summary from BenchmarkDotNet's full JSON reports and the verification sizes: the median and
 * the allocation per case (keyed `Type.Method`), the encoded sizes, and the machine description.
 */
function summarize(resultsDirectory, sizes, job) {
  const reports = fs.readdirSync(resultsDirectory).filter((name) => name.endsWith("-report-full.json"));
  assertCondition(reports.length > 0, `No BenchmarkDotNet reports in ${resultsDirectory}.`);
  const cases = {};
  let host;
  for (const report of reports) {
    const data = JSON.parse(fs.readFileSync(path.join(resultsDirectory, report), "utf8"));
    host ??= data.HostEnvironmentInfo;
    for (const benchmark of data.Benchmarks) {
      assertCondition(benchmark.Statistics, `${benchmark.FullName} produced no statistics.`);
      cases[`${benchmark.Type}.${benchmark.Method}`] = {
        medianNanoseconds: Math.round(benchmark.Statistics.Median * 1000) / 1000,
        allocatedBytes: benchmark.Memory?.BytesAllocatedPerOperation ?? 0,
      };
    }
  }

  return {
    environment: {
      processor: host.ProcessorName.trim(),
      // "Windows 11 (10.0.26200.9457/25H2/...)" -> "Windows 11"; the build details do not help a reader.
      os: host.OsVersion.replace(/\s*\(.*\)$/, ""),
      runtime: host.RuntimeVersion.replace(/\s*\(.*\)$/, ""),
      benchmarkDotNet: host.BenchmarkDotNetVersion,
      job,
      date: new Date().toISOString().slice(0, 10),
    },
    sizes,
    cases: Object.fromEntries(Object.entries(cases).sort(([a], [b]) => a.localeCompare(b))),
  };
}

/** Builds the comparison project in Release. */
function build() {
  runDotnet(["build", projectFile, "-c", "Release"], { label: "build comparison benchmarks" });
}

/** Runs the verification pass (every case once, outputs checked) and returns the recorded encoded sizes. */
function verify() {
  const sizesPath = path.join(artifactsDirectory, "sizes.json");
  runDotnet(["run", "--project", projectFile, "-c", "Release", "--no-build", "--", "--verify", "--sizes", sizesPath], {
    label: "verify comparison cases",
  });
  return JSON.parse(fs.readFileSync(sizesPath, "utf8"));
}

/** Writes the README block rendered from `summary`. */
function renderReadme(summary) {
  const page = fs.readFileSync(options.readme, "utf8");
  const eol = page.includes("\r\n") ? "\r\n" : "\n";
  const updated = replaceBlock(page, renderBlock(summary, packageVersions()).replaceAll("\n", eol));
  fs.writeFileSync(options.readme, updated);
  console.log(`Rendered the comparison tables into ${path.relative(repositoryRoot, options.readme)}.`);
}

/** Reads the committed summary. */
function readSummary() {
  assertCondition(fs.existsSync(options.summary), `${options.summary} does not exist; measure first.`);
  return JSON.parse(fs.readFileSync(options.summary, "utf8"));
}

/**
 * Recompiles Kaitai/SensorReading.g.cs. The Kaitai compiler is a Scala program; its npm build runs on Node, so no
 * Java is needed. Both packages are installed into a temporary directory at pinned versions and removed afterwards.
 */
async function regenerateKaitai() {
  const schemaPath = path.join(projectDirectory, "Kaitai/sensor_reading.ksy");
  const outputPath = path.join(projectDirectory, "Kaitai/SensorReading.g.cs");
  const toolDirectory = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-kaitai-"));
  try {
    runNpm([
      "install", "--no-save", "--no-audit", "--no-fund", "--prefix", toolDirectory,
      `kaitai-struct-compiler@${KAITAI_COMPILER_VERSION}`, `js-yaml@${JS_YAML_VERSION}`,
    ]);

    const modules = path.join(toolDirectory, "node_modules");
    const yaml = (await import(pathToFileURL(path.join(modules, "js-yaml/index.js")).href)).default;
    const compiler = (await import(pathToFileURL(path.join(modules, "kaitai-struct-compiler/kaitai-struct-compiler.js")).href)).default;
    const files = await compiler.compile("csharp", yaml.load(fs.readFileSync(schemaPath, "utf8")), null, false);
    const source = files["SensorReading.cs"];
    assertCondition(source, `The Kaitai compiler produced ${Object.keys(files).join(", ")}, not SensorReading.cs.`);

    // The auto-generated header keeps the repository's analyzers off the file; the compiler output predates
    // nullable annotations.
    const header = [
      "// <auto-generated>",
      `// Compiled by kaitai-struct-compiler ${KAITAI_COMPILER_VERSION} from sensor_reading.ksy;`,
      "// regenerate with node tools/quality/comparison-benchmarks.mjs --regenerate-kaitai instead of editing.",
      "// </auto-generated>",
      "#nullable disable",
      "",
    ].join("\n");
    fs.writeFileSync(outputPath, `${header}${source.replaceAll("\r\n", "\n").trimEnd()}\n`);
    console.log(`Wrote ${path.relative(repositoryRoot, outputPath)}.`);
  } finally {
    fs.rmSync(toolDirectory, { recursive: true, force: true });
  }
}

/**
 * Checks the renderer against a synthetic summary: every row renders, formats hold, rows are sorted fastest first,
 * and a missing case fails.
 */
function selfTest() {
  const cases = {};
  for (const row of [...SAME_BYTES_ROWS, ...OWN_FORMAT_ROWS]) {
    if (row.deserialize) cases[`DeserializeBenchmarks.${row.deserialize}`] = { medianNanoseconds: 12.345, allocatedBytes: 0 };
    if (row.serialize) cases[`SerializeBenchmarks.${row.serialize}`] = { medianNanoseconds: 1234.5, allocatedBytes: 96 };
  }
  // Declared order reversed in time, so a renderer that kept the declared order would fail the order check below.
  VARIABLE_ROWS.forEach((row, index) => {
    const median = 1000 - index * 100;
    cases[`VariableBenchmarks.${row.deserialize}`] = { medianNanoseconds: median, allocatedBytes: 8 };
    if (row.serialize) cases[`VariableBenchmarks.${row.serialize}`] = { medianNanoseconds: median, allocatedBytes: 8 };
  });
  const summary = {
    environment: { processor: "CPU", os: "OS", runtime: ".NET 10.0.0", benchmarkDotNet: "0.15.8", job: "short", date: "2026-01-01" },
    sizes: Object.fromEntries(OWN_FORMAT_ROWS.map((row) => [row.size, 79])),
    cases,
  };
  const versions = packageVersions();
  const block = renderBlock(summary, versions).replace(/ {2,}/g, " ");
  assertCondition(block.startsWith(START_MARKER) && block.endsWith(END_MARKER), "The block must be wrapped in its markers.");
  assertCondition(block.includes("| 12.3 ns | 0 B | 1,235 ns | 96 B |"), "Times and allocations must use the documented formats.");
  assertCondition(block.includes(`| Kaitai Struct ${versions["KaitaiStruct.Runtime.CSharp"]} | 12.3 ns | 0 B | — | — |`), "A missing operation must render as dashes.");
  assertCondition(formatTime(15_000) === "15.0 µs", "Times of 10 µs and more must render in microseconds.");
  const lastVariable = rowLabel(VARIABLE_ROWS.at(-1), versions);
  const firstVariable = rowLabel(VARIABLE_ROWS[0], versions);
  assertCondition(block.indexOf(`| ${lastVariable} |`, block.indexOf("depends on its data")) < block.indexOf(`| ${firstVariable} |`, block.indexOf("depends on its data")), "Rows must be sorted fastest first.");
  const tied = sortRows(
    [
      { label: "a", deserialize: "x", serialize: null, readType: "T", writeType: "T" },
      { label: "b", deserialize: "x", serialize: "y", readType: "T", writeType: "T" },
    ],
    () => 5,
  );
  assertCondition(tied[0].label === "b", "A missing operation must sort after a measured one when the other column ties.");
  assertCondition(
    markdownTable(["A", "Time"], ["left", "right"], [["long label", "1 ns"]]).join("\n") ===
      "| A          | Time |\n| ---------- | ---: |\n| long label | 1 ns |",
    "Tables must pad every column to its widest cell, with right-aligned columns padded on the left.",
  );
  assertCondition(replaceBlock(`a\n${START_MARKER}\nold\n${END_MARKER}\nb`, "new") === "a\nnew\nb", "Only the marked block may change.");

  delete summary.cases["DeserializeBenchmarks.Kaitai_Struct"];
  let failed = false;
  try {
    renderBlock(summary, versions);
  } catch {
    failed = true;
  }
  assertCondition(failed, "A summary without a row's case must fail to render.");
  console.log("comparison-benchmarks self-test passed.");
}

await main(async () => {
  if (options["self-test"]) return selfTest();
  if (options["regenerate-kaitai"]) return regenerateKaitai();
  if (options.render) return renderReadme(readSummary());
  if (options.check) {
    const page = fs.readFileSync(options.readme, "utf8").replaceAll("\r\n", "\n");
    const expected = replaceBlock(page, renderBlock(readSummary(), packageVersions()));
    assertCondition(page === expected, "The README comparison tables differ from benchmarks/CStructSharp.Comparison/results.json; run with --render.");
    console.log("The README comparison tables match results.json.");
    return;
  }

  build();
  const sizes = verify();
  if (options.verify) return;

  // BenchmarkDotNet writes one full JSON report per benchmark class under <artifacts>/results; clear old ones first
  // so that a filtered run cannot mix with an earlier run's results.
  const resultsDirectory = path.join(artifactsDirectory, "results");
  fs.rmSync(resultsDirectory, { recursive: true, force: true });
  runDotnet(
    ["run", "--project", projectFile, "-c", "Release", "--no-build", "--", "--filter", options.filter, "--job", options.job, "--artifacts", artifactsDirectory],
    { label: "run comparison benchmarks" },
  );

  // Render before writing, so that a filtered run that lacks a table's case fails without replacing results.json.
  const summary = summarize(resultsDirectory, sizes, options.job);
  renderBlock(summary, packageVersions());
  fs.writeFileSync(options.summary, `${JSON.stringify(summary, null, 2)}\n`);
  console.log(`Wrote ${path.relative(repositoryRoot, options.summary)}.`);
  renderReadme(summary);
});
