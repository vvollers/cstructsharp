// Copied into an isolated installed consumer. No repository imports or mock managed exports.
import assert from "node:assert/strict";
import { compile, parse, parseWithDebug, serialize, update, getVersion, loadCStructSharpWasm } from "cstructsharp";
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
const metadataDefinition = "struct root { uint8 kind; int24< delta; uleb128_64 count; fixed16_16> revision; uuid network; guid windows; latin1 western[1]; cp437 dos[1]; utf16le little[4]; utf16be big[4]; if(kind == 1) { utf8 label[3]; } else { uint8 raw[3]; } };";
const metadataOptions = { rootTypeName: "root", aligned: false };
const identifier = "00112233-4455-6677-8899-aabbccddeeff";
const metadata = {
  kind: 1, delta: -2, count: "18446744073709551615", revision: -1.5,
  network: identifier, windows: identifier, western: "é", dos: "é",
  little: "😀", big: "😀", label: "€",
};
const metadataBytes = await serialize(metadataDefinition, metadata, metadataOptions);
assert.equal(metadataBytes.Success, true);
assert.deepEqual(Array.from(metadataBytes.Data.slice(0, 4)), [1, 254, 255, 255]);
const metadataRead = await parseWithDebug(metadataDefinition, metadataBytes.Data, metadataOptions);
assert.equal(metadataRead.Success, true);
assert.deepEqual(JSON.parse(metadataRead.Data).root, metadata);
const metadataRoundTrip = await serialize(metadataDefinition, JSON.parse(metadataRead.Data).root, metadataOptions);
assert.deepEqual(metadataRoundTrip.Data, metadataBytes.Data);
const metadataUpdate = await update(metadataDefinition, metadataBytes.Data, "root.label", "£", metadataOptions);
assert.equal(metadataUpdate.Success, true);
const metadataUpdatedRead = await parse(metadataDefinition, metadataUpdate.Data, metadataOptions);
assert.equal(JSON.parse(metadataUpdatedRead.Data).root.label, "£\0");
const changedBranch = await update(metadataDefinition, metadataBytes.Data, "root.kind", 0, metadataOptions);
assert.equal(changedBranch.Success, false);
assert.equal(changedBranch.Error.Code, "write-failed");
assert.equal(metadataBytes.Data[0], 1);
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

const compiled = await compile(metadataDefinition, metadataOptions);
try {
  const [a, b] = await Promise.all([compiled.parse(metadataBytes.Data), compiled.parseWithDebug(metadataBytes.Data)]);
  assert.deepEqual(JSON.parse(a.Data).root, metadata);
  assert.deepEqual(b, metadataRead);
} finally { await compiled.dispose(); }
await assert.rejects(compiled.parse(input), /disposed/);
