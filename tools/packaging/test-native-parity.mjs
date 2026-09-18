// Differential test between the two JavaScript parse paths: the static plan executed in JavaScript for small fixed
// layouts and the managed parser behind WASM. Both must produce byte-for-byte identical envelopes for the same input.
// Runs inside an installed consumer of the packed tarball (see test-npm-package.mjs), so the argument is the
// consumer directory whose node_modules holds cstructsharp.
//
// Usage: node tools/packaging/test-native-parity.mjs <consumer directory> [--trials 1000] [--seed 20260918]
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { loadFixture, manifest, xorshiftBytes } from "../../benchmarks/js/bench/fixtures.mjs";

const args = process.argv.slice(2);
const consumer = args[0];
assert.ok(consumer, "Pass the consumer directory that has cstructsharp installed.");
const option = (name, fallback) => {
  const index = args.indexOf(name);
  return index >= 0 ? Number(args[index + 1]) : fallback;
};
const trials = option("--trials", 1000);
const seed = option("--seed", 20260918);

const api = await import(pathToFileURL(path.join(consumer, "node_modules", "cstructsharp", "node.js")).href);

/**
 * The fast path only runs for byte inputs ≤ 64 KiB with no read options; any read option - here the default array
 * budget, which changes nothing - sends the same bytes through WASM. Both results must agree exactly.
 */
async function both(definition, bytes, options) {
  const native = await api.parse(definition, bytes, options);
  const wasm = await api.parse(definition, bytes, { ...options, maxArrayElements: 1_000_000 });
  return [native, wasm];
}

let compared = 0;
let nativeRuns = 0;

// 1. Every benchmark fixture whose input fits the fast path. A fixture that WASM rejects (malformed inputs) must be
//    rejected identically; a fixture the static plan cannot describe simply runs WASM twice, which is still a valid
//    equality check of the managed path with itself.
for (const entry of manifest().fixtures) {
  const id = typeof entry === "string" ? entry : entry.id;
  const { document, bytes } = loadFixture(id);
  if (bytes.byteLength > 64 * 1024 || bytes.byteLength === 0 || document.readOptions) continue;
  const options = { ...document.options, rootTypeName: document.root };
  const [native, wasm] = await both(document.definition, bytes, options);
  assert.deepStrictEqual(native, wasm, `fixture ${id}`);
  compared++;
}

// 2. Seeded random bytes over a layout that exercises every codec the static plan implements, in both byte orders,
//    including nested structs, struct arrays, numeric arrays, enums, character buffers, and non-finite floats.
const definition = [
  "enum m : int8 { off = -1, on = 2 };",
  "enum w : uint16 { a = 1, b = 65535 };",
  "struct leaf { uint8 k; uint32 v; int16> s; };",
  "struct s {",
  "  int24 a; uint24 b; int24> c; uint24> d;",
  "  float32 f; float32> g; float64 h; float64> i;",
  "  m mode; w wide; bool flag; int8 neg;",
  "  char name[3]; char wideName[5];",
  "  uint16> pairs[2]; int32 quad[3]; uint64 big[2]; int64> signed[2];",
  "  leaf l; leaf arr[2];",
  "};",
].join("\n");
const size = 3 * 4 + 4 * 2 + 8 * 2 + 1 + 2 + 1 + 1 + 3 + 5 + 4 + 12 + 16 + 16 + 7 + 14;
const random = xorshiftBytes(seed, size * trials);
for (let trial = 0; trial < trials; trial++) {
  const bytes = random.subarray(trial * size, (trial + 1) * size);
  const [native, wasm] = await both(definition, bytes, { rootTypeName: "s" });
  assert.deepStrictEqual(native, wasm, `random trial ${trial} (seed ${seed})`);
  assert.equal(native.Success, true, `random trial ${trial} must parse`);
  compared++;
  nativeRuns++;
}

// 3. The float32 tie cases the review found, plus NaN and both infinities in both widths.
const floats = "struct s { float32 a; float32> b; float64 c; float64> d; };";
const cases = [
  [0x40004000, 0x40004000, 0x4000080000000000n, 0x4000080000000000n],
  [0x49b4345a, 0x49b4345a, 0x7ff8000000000000n, 0x7ff0000000000000n],
  [0x7fc00000, 0x7f800000, 0xfff0000000000000n, 0x0000000000000001n],
  [0xff800000, 0x00000001, 0x3fb999999999999an, 0xbff0000000000000n],
];
for (const [a, b, c, d] of cases) {
  const bytes = new Uint8Array(24);
  const view = new DataView(bytes.buffer);
  view.setUint32(0, a, true);
  view.setUint32(4, b, false);
  view.setBigUint64(8, c, true);
  view.setBigUint64(16, d, false);
  const [native, wasm] = await both(floats, bytes, { rootTypeName: "s" });
  assert.deepStrictEqual(native, wasm, `float case ${a.toString(16)}`);
  assert.equal(native.Success, true);
  compared++;
}

const nan = await api.parse(floats, new Uint8Array([0, 0, 0xc0, 0x7f, 0x7f, 0x80, 0, 0, 0, 0, 0, 0, 0, 0, 0xf0, 0xff, 0x7f, 0xf0, 0, 0, 0, 0, 0, 0]), { rootTypeName: "s" });
assert.deepStrictEqual(nan.Data.s, { a: "NaN", b: "Infinity", c: "-Infinity", d: "Infinity" });

// 4. The same strings are accepted back by the write path, so a parsed value round-trips through serialize.
const written = await api.serialize(floats, { a: "NaN", b: "Infinity", c: "-Infinity", d: 1.5 }, { rootTypeName: "s" });
assert.equal(written.Success, true, JSON.stringify(written.Error));
const reread = await api.parse(floats, written.Data, { rootTypeName: "s" });
assert.deepStrictEqual(reread.Data.s, { a: "NaN", b: "Infinity", c: "-Infinity", d: 1.5 });

console.log(`Native/WASM parity: ${compared} inputs compared (${nativeRuns} random trials, seed ${seed}).`);
fs.writeFileSync(path.join(consumer, "parity.txt"), `${compared}\n`);
