#!/usr/bin/env node
// Deterministic fixture generator for the performance scenario matrix (agentdocs/performance-improvement-plan.md §4.4).
//
// Every fixture is a JSON document under benchmarks/fixtures/cases/ with the layout source, constructor options,
// the root type, optional read options, the input bytes (inline hex, a sidecar .bin under data/, or a seeded
// generator spec that C# and JS re-materialize identically), and the expected canonical JSON result. The expected
// result is NOT produced here: run `dotnet run --project benchmarks/CStructSharp.FixtureTool -c Release -f net10.0 -- fill`
// afterwards so the managed library is the single source of truth for expectations.
//
// Real-format fixtures are imported from apps/inspector/src/formats.ts, which is itself verified byte-for-byte by
// tests/CStructSharpTests/WellKnownFormatFixtures.cs. Conditional fixtures reuse the existing ConditionalComparison
// harness definitions. Everything else is synthetic and seeded, so re-running this script is a no-op diff.
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(here, "../..");
const casesDirectory = path.join(here, "cases");
const dataDirectory = path.join(here, "data");
const INLINE_HEX_LIMIT = 16 * 1024;

// ---------------------------------------------------------------- deterministic randomness ------------------------

function xorshift32(seed) {
  let x = seed >>> 0 || 1;
  return () => {
    x ^= x << 13;
    x >>>= 0;
    x ^= x >>> 17;
    x ^= x << 5;
    x >>>= 0;
    return x;
  };
}

// Seeded byte stream shared with the C# tool and the JS harness: one xorshift32 step per byte, low byte taken.
export function xorshiftBytes(seed, size) {
  const next = xorshift32(seed);
  const bytes = new Uint8Array(size);
  for (let i = 0; i < size; i++) bytes[i] = next() & 0xff;
  return bytes;
}

// ---------------------------------------------------------------- byte builders --------------------------------------

class ByteBuilder {
  constructor() {
    this.chunks = [];
    this.length = 0;
  }
  push(bytes) {
    this.chunks.push(bytes);
    this.length += bytes.length;
    return this;
  }
  scalar(size, write, littleEndian = true) {
    const buffer = new Uint8Array(size);
    write(new DataView(buffer.buffer), littleEndian);
    return this.push(buffer);
  }
  u8(v) { return this.push(Uint8Array.of(v & 0xff)); }
  i8(v) { return this.u8(v); }
  u16(v, le = true) { return this.scalar(2, (d) => d.setUint16(0, v, le)); }
  i16(v, le = true) { return this.scalar(2, (d) => d.setInt16(0, v, le)); }
  u32(v, le = true) { return this.scalar(4, (d) => d.setUint32(0, v >>> 0, le)); }
  i32(v, le = true) { return this.scalar(4, (d) => d.setInt32(0, v | 0, le)); }
  u64(v, le = true) { return this.scalar(8, (d) => d.setBigUint64(0, BigInt.asUintN(64, BigInt(v)), le)); }
  i64(v, le = true) { return this.scalar(8, (d) => d.setBigInt64(0, BigInt.asIntN(64, BigInt(v)), le)); }
  f32(v, le = true) { return this.scalar(4, (d) => d.setFloat32(0, v, le)); }
  f64(v, le = true) { return this.scalar(8, (d) => d.setFloat64(0, v, le)); }
  ascii(text, fixedLength) {
    const bytes = new Uint8Array(fixedLength ?? text.length);
    for (let i = 0; i < text.length && i < bytes.length; i++) bytes[i] = text.charCodeAt(i) & 0x7f;
    return this.push(bytes);
  }
  utf8(text) { return this.push(new TextEncoder().encode(text)); }
  utf16(text, le = true) {
    const bytes = new Uint8Array(text.length * 2);
    const view = new DataView(bytes.buffer);
    for (let i = 0; i < text.length; i++) view.setUint16(i * 2, text.charCodeAt(i), le);
    return this.push(bytes);
  }
  zeros(n) { return this.push(new Uint8Array(n)); }
  align(n) {
    const pad = (n - (this.length % n)) % n;
    return pad ? this.zeros(pad) : this;
  }
  toBytes() {
    const out = new Uint8Array(this.length);
    let offset = 0;
    for (const chunk of this.chunks) {
      out.set(chunk, offset);
      offset += chunk.length;
    }
    return out;
  }
}

const hex = (bytes) => Array.from(bytes, (b) => b.toString(16).padStart(2, "0")).join("");

// Finite, JSON-representable floats only: NaN/Infinity cannot be written by Utf8JsonWriter.
function finiteFloat(next, scale = 1000) {
  return Math.round(((next() / 0xffffffff) * 2 - 1) * scale * 1000) / 1000;
}

// ---------------------------------------------------------------- fixture registry ----------------------------------

const fixtures = [];
const defaultOptions = { pointerSize: 8, aligned: false, littleEndian: true };

/**
 * @param {object} spec
 * @param {string} spec.id
 * @param {string} spec.scenario
 * @param {string} spec.definition
 * @param {Uint8Array | {kind: "xorshift", seed: number, size: number} | null} spec.bytes
 * @param {string} [spec.root]
 * @param {object} [spec.options]
 * @param {object} [spec.readOptions]
 * @param {object} [spec.variables]
 * @param {string} [spec.expectedError] exception type name expected from Parse
 * @param {string[]} [spec.tags]
 * @param {string} [spec.notes]
 */
function add(spec) {
  const fixture = {
    id: spec.id,
    scenario: spec.scenario,
    tags: spec.tags ?? [],
    notes: spec.notes,
    definition: spec.definition,
    options: { ...defaultOptions, ...(spec.options ?? {}) },
    root: spec.root ?? "root",
    variables: spec.variables ?? {},
    readOptions: spec.readOptions ?? null,
    bytes: null,
    byteLength: null,
    expectedError: spec.expectedError ?? null,
    expected: null,
    expectedSha256: null,
    expectedJsonLength: null,
  };
  if (spec.bytes instanceof Uint8Array) {
    fixture.byteLength = spec.bytes.length;
    if (spec.bytes.length <= INLINE_HEX_LIMIT) {
      fixture.bytes = { kind: "hex", hex: hex(spec.bytes) };
    } else {
      const file = `${spec.id}.bin`;
      fs.writeFileSync(path.join(dataDirectory, file), spec.bytes);
      fixture.bytes = { kind: "file", file: `data/${file}` };
    }
  } else if (spec.bytes && spec.bytes.kind === "xorshift") {
    fixture.byteLength = spec.bytes.size;
    fixture.bytes = spec.bytes;
  }
  fixtures.push(fixture);
  return fixture;
}

// ---------------------------------------------------------------- S-PRIM / S-PRIM-BE / S-MIXED-ENDIAN -------------

const primFields = (suffix) =>
  `uint8${suffix} a; int16${suffix} b; uint32${suffix} c; int64${suffix} d; float32${suffix} e; float64${suffix} f; bool g;`;
// `uint8`/`bool` carry no endianness suffix in the language; keep the suffix only on multi-byte primitives.
const primFieldsBE = "uint8 a; int16> b; uint32> c; int64> d; float32> e; float64> f; bool g;";
const PRIM_RECORD_SIZE = 1 + 2 + 4 + 8 + 4 + 8 + 1; // 28 bytes packed

function primRecordBytes(builder, next, le) {
  builder.u8(next() & 0xff);
  builder.i16((next() & 0xffff) - 0x8000, le);
  builder.u32(next(), le);
  // Keep int64 within JavaScript's safe-integer range so JSON compares as numbers on both sides.
  builder.i64(BigInt(next()) * 1000n - 2_000_000_000_000n, le);
  builder.f32(finiteFloat(next), le);
  builder.f64(finiteFloat(next, 1e6), le);
  builder.u8(next() & 1);
}

for (const [variant, fieldText, le, scenario] of [
  ["le", primFields(""), true, "S-PRIM"],
  ["be", primFieldsBE, false, "S-PRIM-BE"],
]) {
  const one = new ByteBuilder();
  primRecordBytes(one, xorshift32(0x5eed0001), le);
  add({
    id: `prim-${variant}-record`,
    scenario,
    tags: ["warm", "cold", "write", "update", "boundary"],
    definition: `struct root { ${fieldText} };`,
    bytes: one.toBytes(),
  });
  const many = new ByteBuilder();
  const next = xorshift32(0x5eed0002);
  for (let i = 0; i < 1024; i++) primRecordBytes(many, next, le);
  add({
    id: `prim-${variant}-x1k`,
    scenario,
    tags: ["warm", "debug", "stream", "batch"],
    definition: `struct rec { ${fieldText} }; struct root { rec items[1024]; };`,
    bytes: many.toBytes(),
  });
}

{
  const b = new ByteBuilder();
  b.u16(0x1234, true).u16(0x1234, false).u32(0xdeadbeef, true).u32(0xdeadbeef, false).u64(0x0102030405060708n, false);
  add({
    id: "mixed-endian-record",
    scenario: "S-MIXED-ENDIAN",
    tags: ["warm"],
    definition: "struct root { uint16< a; uint16> b; uint32< c; uint32> d; uint64> e; };",
    bytes: b.toBytes(),
  });
}

// ---------------------------------------------------------------- S-NESTED / S-ALIGNED --------------------------------

const nestedDefinition = (count) =>
  "struct leaf { uint8 kind; uint32 value; }; " +
  "struct mid { leaf first; leaf second; uint16 tail; }; " +
  "struct top { mid left; mid right; uint8 mark; }; " +
  `struct root { top items[${count}]; };`;

function nestedBytes(count, aligned, seed) {
  const next = xorshift32(seed);
  const b = new ByteBuilder();
  const leaf = () => { if (aligned) b.align(4); b.u8(next() & 0xff); if (aligned) b.align(4); b.u32(next()); };
  const mid = () => { if (aligned) b.align(4); leaf(); leaf(); b.u16(next() & 0xffff); if (aligned) b.align(4); };
  for (let i = 0; i < count; i++) {
    if (aligned) b.align(4);
    mid(); mid(); b.u8(next() & 0xff);
    if (aligned) b.align(4);
  }
  return b.toBytes();
}

add({ id: "nested-x1", scenario: "S-NESTED", tags: ["warm", "write"], definition: nestedDefinition(1), bytes: nestedBytes(1, false, 0x5eed0010) });
add({ id: "nested-x256", scenario: "S-NESTED", tags: ["warm", "cold", "boundary", "write", "debug"], definition: nestedDefinition(256), bytes: nestedBytes(256, false, 0x5eed0011) });
add({
  id: "aligned-x256",
  scenario: "S-ALIGNED",
  tags: ["warm"],
  definition: nestedDefinition(256),
  options: { aligned: true },
  bytes: nestedBytes(256, true, 0x5eed0012),
  notes: "Portable aligned mode: leaf=8 B (align 4), mid=20 B, top=44 B; verified by the FixtureTool consumption check.",
});

// ---------------------------------------------------------------- S-ARRAY-* -----------------------------------------------

for (const size of [1024, 65536, 1048576]) {
  add({
    id: `array-u8-${size}`,
    scenario: "S-ARRAY-U8",
    tags: ["warm", "boundary", "stream", ...(size === 1024 ? ["debug"] : [])],
    definition: `struct root { uint8 values[${size}]; };`,
    bytes: { kind: "xorshift", seed: 0x5eed0020 + size, size },
    readOptions: size > 65536 ? { maxArrayElements: 2 * size, maxTotalBytesRead: 4 * size } : null,
  });
}
add({
  id: "array-u8-16m-stream",
  scenario: "S-STREAM",
  tags: ["stream"],
  definition: "struct root { uint8 values[16777216]; };",
  bytes: { kind: "xorshift", seed: 0x5eed0021, size: 16777216 },
  readOptions: { maxArrayElements: 33554432, maxTotalBytesRead: 67108864 },
  notes: "Stream-only scenario; the expected JSON is recorded as a SHA-256 of the canonical text.",
});
// 8 MB of uint64 elements: the largest payload the browser bridge accepts (its MaxArrayElements cap is 1,000,000,
// which is why the 1 MiB uint8 and 16 MiB stream fixtures cannot be parsed through the JS API at all).
add({
  id: "array-u64-le-1000000",
  scenario: "S-STREAM",
  tags: ["stream", "boundary"],
  definition: "struct root { uint64 values[1000000]; };",
  bytes: { kind: "xorshift", seed: 0x5eed0022, size: 8000000 },
  readOptions: { maxArrayElements: 1000000, maxTotalBytesRead: 16000000 },
  notes: "JS-parseable large payload; expected JSON recorded as SHA-256 (values above 2^53 serialize as strings).",
});
for (const [suffix, tag] of [["<", "le"], [">", "be"]]) {
  for (const count of [256, 16384, 262144]) {
    add({
      id: `array-u32-${tag}-${count}`,
      scenario: "S-ARRAY-U32",
      tags: ["warm", "vectorize"],
      definition: `struct root { uint32${suffix} values[${count}]; };`,
      bytes: { kind: "xorshift", seed: 0x5eed0030 + count + (tag === "be" ? 1 : 0), size: count * 4 },
      readOptions: count * 4 > 65536 ? { maxArrayElements: 2 * count, maxTotalBytesRead: 8 * count } : null,
    });
  }
}
// Neutral spelling (no endianness suffix): the common way to declare an integer array; E1.5 made it take the same
// bulk path as the suffixed spellings.
add({
  id: "array-u32-neutral-262144",
  scenario: "S-ARRAY-U32",
  tags: ["warm", "vectorize"],
  definition: "struct root { uint32 values[262144]; };",
  bytes: { kind: "xorshift", seed: 0x5eed0030 + 262144, size: 262144 * 4 },
  readOptions: { maxArrayElements: 524288, maxTotalBytesRead: 2097152 },
});
for (const count of [100, 10000]) {
  const next = xorshift32(0x5eed0040 + count);
  const b = new ByteBuilder();
  for (let i = 0; i < count; i++) { b.u32(next()); b.u16(next() & 0xffff); }
  add({
    id: `array-struct-${count}`,
    scenario: "S-ARRAY-STRUCT",
    tags: ["warm", "path"],
    definition: `struct record { uint32 id; uint16 tag; }; struct root { record items[${count}]; };`,
    bytes: b.toBytes(),
  });
}

// ---------------------------------------------------------------- S-DYNAMIC / S-MULTIDIM ---------------------------------

for (const count of [1, 64, 1024]) {
  const next = xorshift32(0x5eed0050 + count);
  const b = new ByteBuilder();
  b.u16(count);
  for (let i = 0; i < count; i++) b.u16(next() & 0xffff);
  b.u32(0xcafebabe);
  add({
    id: `dynamic-${count}`,
    scenario: "S-DYNAMIC",
    tags: ["warm", "expression"],
    definition:
      "#define TRAILER_WORDS 1\n" +
      "struct child { uint16 value; }; " +
      "struct root { uint16 count; child children[count]; uint32 trailer[TRAILER_WORDS]; };",
    bytes: b.toBytes(),
  });
}
{
  const next = xorshift32(0x5eed0060);
  const b = new ByteBuilder();
  for (let i = 0; i < 256; i++) b.u16(next() & 0xffff);
  add({ id: "multidim-16x16", scenario: "S-MULTIDIM", tags: ["warm"], definition: "struct root { uint16 grid[16][16]; };", bytes: b.toBytes() });
}

// ---------------------------------------------------------------- S-BITFIELD / S-ENUM / S-UNION ---------------------------

{
  const next = xorshift32(0x5eed0070);
  const b = new ByteBuilder();
  for (let i = 0; i < 1024; i++) { b.u8(next() & 0xff); b.u16(next() & 0xffff); b.u32(next()); }
  add({
    id: "bitfield-x1k",
    scenario: "S-BITFIELD",
    tags: ["warm", "update"],
    definition:
      "struct rec { uint8 a:3; uint8 b:5; uint16 c:4; uint16 :4; uint16 d:8; uint32 e:12; uint32 f:20; }; " +
      "struct root { rec items[1024]; };",
    bytes: b.toBytes(),
  });
}
{
  const next = xorshift32(0x5eed0080);
  const b = new ByteBuilder();
  for (let i = 0; i < 1024; i++) { b.u8(next() % 3); b.u16(next() % 3 + 10); b.u32(next() % 3 + 100); }
  add({
    id: "enum-x1k",
    scenario: "S-ENUM",
    tags: ["warm"],
    definition:
      "enum small : uint8 { Zero = 0, One = 1, Two = 2 }; " +
      "enum medium : uint16 { Ten = 10, Eleven = 11, Twelve = 12 }; " +
      "enum wide : uint32 { Hundred = 100, HundredOne = 101, HundredTwo = 102 }; " +
      "struct rec { small x; medium y; wide z; }; struct root { rec items[1024]; };",
    bytes: b.toBytes(),
  });
}
{
  const next = xorshift32(0x5eed0090);
  const b = new ByteBuilder();
  for (let i = 0; i < 1024; i++) { b.u8(next() & 3); b.u32(next()); }
  add({
    id: "union-x1k",
    scenario: "S-UNION",
    tags: ["warm", "update"],
    definition:
      "union choice { uint8 small; uint16 medium; uint32 large; int32 signed; }; " +
      "struct rec { uint8 tag; choice value; }; struct root { rec items[1024]; };",
    bytes: b.toBytes(),
  });
}

// ---------------------------------------------------------------- S-STRINGS ----------------------------------------------------

for (const length of [8, 1024, 65536]) {
  const b = new ByteBuilder();
  const text = (seed) => Array.from({ length }, (_, i) => String.fromCharCode(0x41 + ((i * 7 + seed) % 26))).join("");
  const wide = (seed) => Array.from({ length }, (_, i) => String.fromCharCode(i % 5 === 0 ? 0x4e16 + ((i + seed) % 40) : 0x61 + ((i + seed) % 26))).join("");
  b.ascii("fixed-name", 32);
  b.ascii(text(1)).u8(0);
  b.utf8(wide(2)).u8(0);
  b.utf16(text(3), true).u16(0, true);
  b.utf16(wide(4), true).u16(0, true);
  add({
    id: `strings-${length}`,
    scenario: "S-STRINGS",
    tags: ["warm", "write", ...(length === 1024 ? ["boundary"] : [])],
    definition:
      "struct root { char name[32]; char cstr[]; utf8_string_zero utf8_text; wchar< wide[]; unicode_string_zero< utf16_text; };",
    bytes: b.toBytes(),
    readOptions: length > 16384 ? { maxStringBytes: 4 * length * 3 } : null,
  });
}

// ---------------------------------------------------------------- S-POINTER ------------------------------------------------------

for (const depth of [1, 8, 64]) {
  // Node layout with pointerSize 4: [next:u32][value:u32] = 8 bytes; root = [head:u32] at 0; nodes follow at 4 + 8*i.
  const b = new ByteBuilder();
  b.u32(4);
  for (let i = 0; i < depth; i++) {
    const nextAddress = i + 1 < depth ? 4 + 8 * (i + 1) : 0;
    b.u32(nextAddress).u32(1000 + i);
  }
  add({
    id: `pointer-depth-${depth}`,
    scenario: "S-POINTER",
    tags: ["warm", "update"],
    definition: "struct node { node *next; uint32 value; }; struct root { node *head; };",
    options: { pointerSize: 4 },
    readOptions: { maxPointerDepth: 64 },
    bytes: b.toBytes(),
    notes: "Absolute addressing (default). Address 0 terminates the chain as a null pointer.",
  });
}

// ---------------------------------------------------------------- S-COND -----------------------------------------------------------

{
  const conditional = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "benchmarks/ConditionalComparison/cases.json"), "utf8"));
  for (const name of ["plain128", "if128", "switch128"]) {
    const source = conditional.find((c) => c.name === name);
    if (!source) throw new Error(`ConditionalComparison case ${name} not found.`);
    // Mixed tags so both arms are exercised: tag 1 → uint32 value, otherwise uint16 small; tail always uint16.
    const next = xorshift32(0x5eed00a0);
    const b = new ByteBuilder();
    for (let i = 0; i < 128; i++) {
      const tag = name === "plain128" ? 1 : (i % 3 === 0 ? 2 : 1);
      b.u8(tag);
      if (name === "plain128" || tag === 1) b.u32(next()); else b.u16(next() & 0xffff);
      b.u16(next() & 0xffff);
    }
    add({
      id: `cond-${name}`,
      scenario: "S-COND",
      tags: ["warm"],
      definition: source.definition,
      bytes: b.toBytes(),
      notes: "Definition reused from benchmarks/ConditionalComparison/cases.json; bytes regenerated with mixed tags.",
    });
  }
}

// ---------------------------------------------------------------- S-REAL ---------------------------------------------------------------

{
  const { formats } = await import(pathToFileURL(path.join(repositoryRoot, "apps/inspector/src/formats.ts")).href);
  for (const format of formats) {
    if (format.schemaOnly) continue;
    const bytes = Uint8Array.from(format.binaryHex.trim().split(/\s+/), (h) => parseInt(h, 16));
    add({
      id: `real-${format.id}`,
      scenario: "S-REAL",
      tags: ["warm", "cold", "boundary", "debug", "compile", "typed", ...(format.parserOptions.addressingMode ? ["partial-consume"] : [])],
      definition: format.definition,
      root: format.rootType,
      options: {
        pointerSize: format.parserOptions.pointerSize,
        aligned: format.parserOptions.aligned,
        littleEndian: format.parserOptions.littleEndian,
      },
      readOptions: format.parserOptions.addressingMode ? { addressingMode: format.parserOptions.addressingMode } : null,
      bytes,
      notes: `Imported from apps/inspector/src/formats.ts (${format.sourceFixture}).`,
    });
  }
}

// ---------------------------------------------------------------- S-MALFORMED ------------------------------------------------------

{
  const one = new ByteBuilder();
  primRecordBytes(one, xorshift32(0x5eed0001), true);
  add({
    id: "malformed-truncated",
    scenario: "S-MALFORMED",
    tags: ["error"],
    definition: `struct root { ${primFields("")} };`,
    bytes: one.toBytes().subarray(0, 10),
    expectedError: "CStructReadException",
  });
  add({
    id: "malformed-negative-count",
    scenario: "S-MALFORMED",
    tags: ["error"],
    definition: "struct root { int16 count; uint8 values[count]; };",
    bytes: new ByteBuilder().i16(-1).zeros(16).toBytes(),
    expectedError: "CStructReadException",
  });
  add({
    id: "malformed-budget-exceeded",
    scenario: "S-MALFORMED",
    tags: ["error"],
    definition: "struct root { uint8 values[100000]; };",
    bytes: { kind: "xorshift", seed: 0x5eed00b0, size: 100000 },
    readOptions: { maxArrayElements: 1000 },
    expectedError: "CStructReadLimitException",
  });
  add({
    id: "malformed-dangling-pointer",
    scenario: "S-MALFORMED",
    tags: ["error"],
    definition: "struct root { uint32 *target; };",
    options: { pointerSize: 4 },
    bytes: new ByteBuilder().u32(0x7fffff00).toBytes(),
    expectedError: "CStructReadException",
  });
}

// ---------------------------------------------------------------- S-COMPILE ----------------------------------------------------------

function wideDefinition(fieldCount, seed) {
  const types = ["uint8", "int16", "uint32", "int64", "float32", "float64", "uint16", "int32"];
  const next = xorshift32(seed);
  const fields = Array.from({ length: fieldCount }, (_, i) => `${types[next() % types.length]} f${i};`).join(" ");
  return `struct root { ${fields} };`;
}

const compileFixtures = [
  { id: "compile-small", definition: "struct root { uint8 kind; uint32 value; };" },
  { id: "compile-medium-128", definition: wideDefinition(128, 0x5eed00c0) },
  { id: "compile-large-512", definition: wideDefinition(512, 0x5eed00c1) },
  { id: "compile-nested", definition: nestedDefinition(16) },
  {
    id: "compile-k100",
    notes: "100 distinct schemas for the repeated-schema workload; compile round-robin.",
    definitions: Array.from({ length: 100 }, (_, i) => wideDefinition(8 + (i % 24), 0x5eed0100 + i).replace("struct root", `struct schema${i}`).replace(/ f(\d+);/g, ` s${i}_f$1;`)),
  },
];
for (const compile of compileFixtures) {
  add({
    id: compile.id,
    scenario: "S-COMPILE",
    tags: ["compile"],
    definition: compile.definition ?? compile.definitions[0],
    bytes: null,
    notes: compile.notes,
  });
  if (compile.definitions) fixtures[fixtures.length - 1].definitions = compile.definitions;
}

// ---------------------------------------------------------------- write ----------------------------------------------------------------------

fs.mkdirSync(casesDirectory, { recursive: true });
fs.mkdirSync(dataDirectory, { recursive: true });
const existing = new Map();
for (const name of fs.readdirSync(casesDirectory)) {
  if (name.endsWith(".json")) existing.set(name, JSON.parse(fs.readFileSync(path.join(casesDirectory, name), "utf8")));
}
let written = 0;
for (const fixture of fixtures) {
  const file = `${fixture.id}.json`;
  const previous = existing.get(file);
  // Preserve expectations already filled by the FixtureTool when the inputs are unchanged. The tool re-serializes
  // documents with every property present (nulls included), so compare a normalized view of the inputs.
  const stripNulls = (value) =>
    value && typeof value === "object" && !Array.isArray(value)
      ? Object.fromEntries(Object.entries(value).filter(([, v]) => v !== null && v !== undefined).sort())
      : value;
  const inputs = (f) =>
    JSON.stringify({
      definition: f.definition,
      definitions: f.definitions ?? null,
      root: f.root,
      options: stripNulls(f.options),
      readOptions: stripNulls(f.readOptions),
      variables: stripNulls(f.variables),
      bytes: stripNulls(f.bytes),
    });
  if (previous && inputs(previous) === inputs(fixture)) {
    fixture.expectedError = previous.expectedError ?? null;
    fixture.expected = previous.expected ?? null;
    fixture.expectedSha256 = previous.expectedSha256 ?? null;
    fixture.expectedJsonLength = previous.expectedJsonLength ?? null;
  }
  fs.writeFileSync(path.join(casesDirectory, file), JSON.stringify(fixture, null, 2) + "\n");
  written++;
}
fs.writeFileSync(
  path.join(here, "manifest.json"),
  JSON.stringify(
    {
      schemaVersion: 1,
      generator: "benchmarks/fixtures/generate-fixtures.mjs",
      fixtures: fixtures.map((f) => ({ id: f.id, scenario: f.scenario, tags: f.tags, byteLength: f.byteLength, file: `cases/${f.id}.json` })),
    },
    null,
    2,
  ) + "\n",
);
console.log(`Wrote ${written} fixtures to ${path.relative(repositoryRoot, casesDirectory)}`);
