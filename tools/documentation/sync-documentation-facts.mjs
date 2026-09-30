#!/usr/bin/env node
/**
 * Keeps the measured and counted facts in the documentation equal to their one source. Each fact is text between a
 * start and an end HTML comment marker in a page; this tool renders the text from committed data and either checks
 * it (`--check`, run by validate-documentation.mjs) or rewrites it (`--write`). A number is never typed by hand: it
 * changes when its source changes.
 *
 * | Fact                  | Page                                     | Source                                           |
 * | benchmark-fixture     | benchmarks/js/README.md                  | benchmarks/fixtures manifest + JS selection rules |
 * | typical-costs         | docs/guides/performance.md               | typical-costs.json + npm-package.json (runtime)  |
 * | accessor-costs        | docs/guides/performance.md               | CStructSharp.Comparison/results.json             |
 * | mapped-direct-costs   | docs/guides/generated/mapped-classes.md  | CStructSharp.Comparison/results.json             |
 * | npm-package-size      | packages/cstructsharp/README.md          | benchmarks/npm-package.json                      |
 * | recipe-count          | docs/examples/index.md                   | tools/lib/documentation-recipes.mjs              |
 * | scenario-count        | docs/examples/index.md                   | docs/examples/Program.cs scenario table          |
 *
 * The sources are refreshed only from real runs: comparison-benchmarks.mjs writes results.json,
 * render-performance-table.mjs records typical-costs.json from a BenchmarkDotNet summary and the JS harness results,
 * and `--record-package` records benchmarks/npm-package.json from `npm run pack:npm` output (the tarball metadata in
 * artifacts/npm/package-info.json and the packed runtime in artifacts/wasm).
 *
 *   node tools/documentation/sync-documentation-facts.mjs --check [--documents <directory>]
 *   node tools/documentation/sync-documentation-facts.mjs --write [--documents <directory>]
 *   node tools/documentation/sync-documentation-facts.mjs --record-package [--package-info <package-info.json>]
 *
 * `--documents` reads and writes the pages below another directory (same relative paths) while the sources stay in
 * the repository; the tests use it to check a stale copy without touching the repository.
 */
import fs from "node:fs";
import path from "node:path";
import { browserVerificationFixtures, canVerifyPublicFixture } from "../../benchmarks/js/bench/fixture-eligibility.mjs";
import { comparisonCase, formatTime, readComparisonSummary } from "../lib/comparison-summary.mjs";
import { recipes, scenarioNames } from "../lib/documentation-recipes.mjs";
import { measureDirectory } from "../lib/files.mjs";
import { END_MARKER, START_MARKER, TYPICAL_COSTS_PATH, formatBytes, renderBlock } from "../lib/performance-table.mjs";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { validateWasmPublication } from "../packaging/wasm-publication.mjs";

/** The committed record of the packed npm package, relative to the repository root. */
const NPM_PACKAGE_PATH = "benchmarks/npm-package.json";

/** Reads a repository-relative JSON source. */
function readSource(relative) {
  const file = path.join(repositoryRoot, relative);
  assertCondition(fs.existsSync(file), `${relative} does not exist; record it from a real run first.`);
  return JSON.parse(fs.readFileSync(file, "utf8"));
}

/** Builds the start and end markers of a `facts:<name>` fact. */
const factMarkers = (name) => ({ start: `<!-- facts:${name}:start -->`, end: `<!-- facts:${name}:end -->` });

/**
 * Counts the benchmark fixtures the Node and browser correctness gates verify, from fixture metadata only (deriving
 * documentation must not allocate the large benchmark inputs or run timings).
 * @returns {string} The sentence between the markers, with its surrounding newlines.
 */
function benchmarkFixtureFacts() {
  const directory = path.join(repositoryRoot, "benchmarks/fixtures");
  const manifest = JSON.parse(fs.readFileSync(path.join(directory, "manifest.json"), "utf8"));
  const documents = new Map();
  for (const entry of manifest.fixtures) {
    assertCondition(!documents.has(entry.id), `Duplicate fixture ${entry.id}`);
    const document = JSON.parse(fs.readFileSync(path.join(directory, entry.file), "utf8"));
    assertCondition(document.id === entry.id && document.byteLength === entry.byteLength, `Fixture ${entry.id} does not match its manifest entry.`);
    documents.set(entry.id, document);
  }
  for (const id of browserVerificationFixtures) assertCondition(documents.has(id) && canVerifyPublicFixture(documents.get(id)), `Browser verification fixture is missing or ineligible: ${id}`);
  assertCondition(new Set(browserVerificationFixtures).size === browserVerificationFixtures.length, "The browser verification fixtures repeat an id.");

  // Use the exact selection function imported by the Node harness, not a parallel implementation of its limits.
  const count = [...documents.values()].filter(canVerifyPublicFixture).length;
  return `\nThe Node correctness gate verifies ${count} of ${documents.size} fixtures; the browser gate verifies ${browserVerificationFixtures.length} representative fixtures.\n`;
}

/**
 * Describes an allocation for prose: "without allocating" for zero, otherwise the byte count.
 * @param {number} bytes Allocated bytes per operation.
 * @returns {string} The phrase.
 */
const allocationPhrase = (bytes) => (bytes === 0 ? "without allocating" : `allocating ${Math.round(bytes).toLocaleString("en-US")} bytes`);

/**
 * The accessor and view costs quoted in the performance guide: the same record parsed and read by path strings, by
 * accessors, and through a view, from the serializer comparison's `DeserializeBenchmarks`.
 * @returns {string} The sentence between the markers.
 */
function accessorCosts() {
  const summary = readComparisonSummary();
  /** The formatted median of one deserialize case. */
  const time = (method) => formatTime(comparisonCase(summary, `DeserializeBenchmarks.${method}`).medianNanoseconds);
  const view = comparisonCase(summary, "DeserializeBenchmarks.CStructSharp_RuntimeView");
  return (
    `In the repository's serializer comparison (a 79-byte record, every member read), \`Parse\` followed by path-string reads took ${time("CStructSharp_RuntimeParse")}, ` +
    `\`Parse\` followed by accessor reads took ${time("CStructSharp_RuntimeParseAccessors")}, and \`CreateView\` with accessors took ${formatTime(view.medianNanoseconds)} ${allocationPhrase(view.allocatedBytes)}.`
  );
}

/**
 * The direct mapped-class costs quoted in the mapped-classes guide, next to the `StructValue` route of the same
 * record, from the serializer comparison.
 * @returns {string} The sentence between the markers.
 */
function mappedDirectCosts() {
  const summary = readComparisonSummary();
  /** The formatted median of one comparison case, keyed `Type.Method`. */
  const time = (key) => formatTime(comparisonCase(summary, key).medianNanoseconds);
  return (
    `In the repository's serializer comparison (a 79-byte record), \`ReadValue<T>\` into a layout-bound mapped class took ${time("DeserializeBenchmarks.CStructSharp_RuntimeReadValue")} ` +
    `and \`Serialize\` of one took ${time("SerializeBenchmarks.CStructSharp_RuntimeSerializeMapped")}; for comparison, \`Parse\` into a \`StructValue\` with every member read by path ` +
    `took ${time("DeserializeBenchmarks.CStructSharp_RuntimeParse")}, and \`Serialize\` of a \`StructValue\` took ${time("SerializeBenchmarks.CStructSharp_RuntimeSerializeStructValue")}.`
  );
}

/**
 * The npm tarball and runtime sizes quoted in the package README.
 * @returns {string} The clause between the markers.
 */
function npmPackageSize() {
  const record = readSource(NPM_PACKAGE_PATH);
  return (
    `the ${record.version} package is about ${formatBytes(record.size)} compressed and ${formatBytes(record.unpackedSize)} unpacked, ` +
    `of which the runtime is ${record.runtime.files} files and ${formatBytes(record.runtime.bytes)} (about ${formatBytes(record.runtime.gzipBytes)} over gzip)`
  );
}

/**
 * The "Typical costs" block of the performance guide: the recorded benchmark cases plus the packed runtime size.
 * @returns {string} The block between the markers, with its surrounding newlines.
 */
function typicalCosts() {
  const record = readSource(TYPICAL_COSTS_PATH);
  const runtime = readSource(NPM_PACKAGE_PATH).runtime;
  return `\n${renderBlock({ summary: record.managed, js: record.javascript, runtime })}`;
}

/**
 * The number of tested recipes, counted from the recipe table.
 * @returns {string} The count as decimal text.
 */
function recipeCount() {
  return String(recipes.length);
}

/**
 * The line a run of every documentation scenario ends with, counted from the scenario table in the example runner.
 * @returns {string} The inline-code text, such as `PASS all 58 scenarios` in backticks.
 */
function scenarioCount() {
  return `\`PASS all ${scenarioNames().length} scenarios\``;
}

/** Every fact: its name, the page (repository-relative), its markers, and the renderer of the text between them. */
const FACTS = [
  { name: "benchmark-fixture", page: "benchmarks/js/README.md", start: "<!-- benchmark-fixture-facts:start -->", end: "<!-- benchmark-fixture-facts:end -->", render: benchmarkFixtureFacts },
  { name: "typical-costs", page: "docs/guides/performance.md", start: START_MARKER, end: END_MARKER, render: typicalCosts },
  { name: "accessor-costs", page: "docs/guides/performance.md", ...factMarkers("accessor-costs"), render: accessorCosts },
  { name: "mapped-direct-costs", page: "docs/guides/generated/mapped-classes.md", ...factMarkers("mapped-direct-costs"), render: mappedDirectCosts },
  { name: "npm-package-size", page: "packages/cstructsharp/README.md", ...factMarkers("npm-package-size"), render: npmPackageSize },
  { name: "recipe-count", page: "docs/examples/index.md", ...factMarkers("recipe-count"), render: recipeCount },
  { name: "scenario-count", page: "docs/examples/index.md", ...factMarkers("scenario-count"), render: scenarioCount },
];

/**
 * Finds the text between a fact's markers.
 * @param {string} text The page.
 * @param {{ name: string, page: string, start: string, end: string }} fact The fact.
 * @returns {{ from: number, to: number }} The offsets of the text between the markers.
 * @throws {Error} Unless the page holds exactly one start marker followed by one end marker.
 */
function locate(text, fact) {
  const from = text.indexOf(fact.start);
  const to = text.indexOf(fact.end);
  assertCondition(from >= 0 && to > from, `${fact.page}: fact "${fact.name}" needs ${fact.start} followed by ${fact.end}.`);
  assertCondition(text.indexOf(fact.start, from + 1) < 0 && text.indexOf(fact.end, to + 1) < 0, `${fact.page}: fact "${fact.name}" has more than one marker pair.`);
  return { from: from + fact.start.length, to };
}

/**
 * Describes where a stale fact first differs, so the failure names the old and the new text without printing a
 * whole table.
 * @returns {string} The first differing line of each side.
 */
function firstDifference(found, expected) {
  const foundLines = found.split("\n");
  const expectedLines = expected.split("\n");
  const index = expectedLines.findIndex((line, position) => line !== foundLines[position]);
  const at = index < 0 ? expectedLines.length : index;
  return `found "${foundLines[at] ?? ""}", expected "${expectedLines[at] ?? ""}"`;
}

/**
 * Checks or rewrites every fact.
 * @param {{ documents: string, write: boolean }} options The directory the pages are read from, and whether to write.
 * @returns {string[]} The stale facts, one message each (empty when every fact is current or was rewritten).
 */
function syncFacts({ documents, write }) {
  const stale = [];
  const pages = new Map();
  for (const fact of FACTS) {
    const file = path.join(documents, fact.page);
    // A page may hold several facts; later facts see the text earlier ones rewrote.
    const text = pages.get(file) ?? fs.readFileSync(file, "utf8").replaceAll("\r\n", "\n");
    const { from, to } = locate(text, fact);
    const expected = fact.render();
    const found = text.slice(from, to);
    if (found !== expected) stale.push(`${fact.page}: fact "${fact.name}" is stale: ${firstDifference(found, expected)}.`);
    pages.set(file, text.slice(0, from) + expected + text.slice(to));
  }

  if (write) for (const [file, text] of pages) if (fs.readFileSync(file, "utf8").replaceAll("\r\n", "\n") !== text) fs.writeFileSync(file, text);
  return write ? [] : stale;
}

/**
 * Records the packed npm package in benchmarks/npm-package.json: the tarball's compressed and unpacked sizes from
 * `npm pack` (package-info.json), and the runtime's file count, bytes and per-file gzip bytes measured from
 * artifacts/wasm after checking that it is the runtime that was packed.
 * @param {string} packageInfoPath The package-info.json that create-wasm-npm-package.mjs wrote.
 * @throws {Error} When the package metadata or the runtime directory does not match the packed runtime.
 */
function recordPackage(packageInfoPath) {
  assertCondition(fs.existsSync(packageInfoPath), `${packageInfoPath} does not exist; run npm run pack:npm first.`);
  const info = JSON.parse(fs.readFileSync(packageInfoPath, "utf8"));
  assertCondition(Number.isInteger(info.size) && Number.isInteger(info.unpackedSize) && info.runtime?.totals, "package-info.json lacks the pack sizes or the runtime manifest.");

  // The gzip sizes come from the files on disk, so they must be the files the tarball holds.
  const runtimeDirectory = path.join(repositoryRoot, "artifacts/wasm");
  const manifest = validateWasmPublication(runtimeDirectory);
  const packedFiles = JSON.stringify(info.runtime.files.map(({ path: file, sha256 }) => [file, sha256]));
  assertCondition(JSON.stringify(manifest.files.map(({ path: file, sha256 }) => [file, sha256])) === packedFiles, "artifacts/wasm is not the runtime in the packed tarball; pack again.");
  const measured = measureDirectory(runtimeDirectory);
  assertCondition(measured.files === info.runtime.totals.files && measured.bytes === info.runtime.totals.bytes, "artifacts/wasm holds files outside the packed runtime manifest.");

  const record = {
    schemaVersion: 1,
    name: info.name,
    version: info.version,
    sourceSha: info.sourceSha,
    size: info.size,
    unpackedSize: info.unpackedSize,
    runtime: { files: measured.files, bytes: measured.bytes, gzipBytes: measured.gzipBytes },
  };
  fs.writeFileSync(path.join(repositoryRoot, NPM_PACKAGE_PATH), `${JSON.stringify(record, null, 2)}\n`);
  console.log(`Recorded ${info.filename}: ${info.size} compressed / ${info.unpackedSize} unpacked bytes, runtime ${measured.files} files, ${measured.bytes} bytes, ${measured.gzipBytes} gzip.`);
}

const options = parseArguments(
  process.argv.slice(2),
  { check: "flag", write: "flag", "record-package": "flag", "package-info": "string", documents: "string" },
  { defaults: { documents: repositoryRoot, "package-info": path.join(repositoryRoot, "artifacts/npm/package-info.json") } },
);

await main(() => {
  const modes = [options.check, options.write, options["record-package"]].filter(Boolean).length;
  assertCondition(modes === 1, "Choose exactly one of --check, --write or --record-package.");
  const documents = path.resolve(options.documents);
  if (options["record-package"]) {
    recordPackage(path.resolve(options["package-info"]));
    syncFacts({ documents, write: true });
    console.log("Documentation facts updated.");
    return;
  }

  const stale = syncFacts({ documents, write: Boolean(options.write) });
  assertCondition(stale.length === 0, `${stale.join("\n")}\nRun node tools/documentation/sync-documentation-facts.mjs --write and review the change.`);
  console.log(`Documentation facts ${options.write ? "updated" : "verified"}: ${FACTS.map((fact) => fact.name).join(", ")}.`);
});
