// Copied into an isolated installed consumer. No repository imports or mock managed exports.
import assert from "node:assert/strict";
import { parse, parseWithDebug, serialize, update, getVersion, loadCStructSharpWasm } from "cstructsharp";
import { Readable } from "node:stream";

globalThis.fetch = () => {
  throw new Error("Node runtime must not access the network");
};
assert.equal(globalThis.window, undefined);
const [first, second] = await Promise.all([loadCStructSharpWasm(), loadCStructSharpWasm()]);
assert.equal(first, second);
const definition = "struct header { uint16 kind; uint32 length; };";
const options = { rootTypeName: "header" };
const input = Buffer.from([2, 0, 6, 0, 0, 0]);
const read = await parseWithDebug(definition, input, options);
assert.equal(read.Success, true);
assert.deepEqual(JSON.parse(read.Data), { header: { kind: 2, length: 6 } });
const written = await serialize(definition, { kind: 3, length: 6 }, options);
assert.equal(written.Success, true);
assert.deepEqual(Array.from(written.Data), [3, 0, 6, 0, 0, 0]);
const changed = await update(definition, input, "header.kind", 4, options);
assert.equal(changed.Success, true);
assert.deepEqual(Array.from(changed.Data), [4, 0, 6, 0, 0, 0]);
assert.deepEqual(Array.from(input), [2, 0, 6, 0, 0, 0]);
const invalid = await parseWithDebug(definition, new Uint8Array(), options);
assert.equal(invalid.Success, false);
assert.equal(typeof invalid.Error.Code, "string");
const failedUpdate = await update(definition, input, "header.missing", 4, options);
assert.equal(failedUpdate.Success, false);
const large = await serialize(
  "struct large { uint64 value; };",
  { value: 18446744073709551615n },
  { rootTypeName: "large" },
);
assert.equal(large.Success, true);
assert.deepEqual(Array.from(large.Data), Array(8).fill(255));
const largeRead = await parseWithDebug("struct large { uint64 value; };", large.Data, {
  rootTypeName: "large",
});
assert.equal(JSON.parse(largeRead.Data).large.value, "18446744073709551615");
await assert.rejects(parseWithDebug(definition, [2]), /Binary chunks/);
const largeInput = new Uint8Array(8 * 1024 * 1024);
largeInput.set(input);
for (const source of [largeInput, new DataView(largeInput.buffer), new Blob([largeInput]),
  Readable.from([Buffer.from(input.subarray(0, 3)), Buffer.from(input.subarray(3))]),
  new Response(input)]) {
  const parsed = await parse(definition, source, options);
  assert.equal(parsed.Success, true);
  assert.deepEqual(JSON.parse(parsed.Data), { header: { kind: 2, length: 6 } });
  assert.deepEqual(parsed.DebugData, []);
}
const largeDebug = await parseWithDebug(definition, largeInput, options);
assert.equal(largeDebug.Success, true);
assert.equal(largeInput[0], 2);
await assert.rejects(loadCStructSharpWasm({ runtimeUrl: "/wrong/" }), /browser option/);
const version = await getVersion();
assert.ok(
  version.startsWith(`CStructSharp WASM ${process.env.EXPECTED_VERSION}+`) ||
    version === `CStructSharp WASM ${process.env.EXPECTED_VERSION}`,
);
console.log("Node consumer passed", version);
