// Copied into an isolated installed consumer. No repository imports or mock managed exports.
import assert from "node:assert/strict";
import { compile, parse, parseWithDebug, resolveAddress, serialize, update, getVersion, loadCStructSharpWasm } from "cstructsharp";
import { Readable } from "node:stream";

globalThis.fetch = () => {
  throw new Error("Node runtime must not access the network");
};
assert.equal(globalThis.window, undefined);
const [first, second] = await Promise.all([loadCStructSharpWasm(), loadCStructSharpWasm()]);
assert.equal(first, second);
const definition = "struct header { uint16 kind; uint32 length; };";
const options = { root: "header" };
const input = Buffer.from([2, 0, 6, 0, 0, 0]);
const read = await parseWithDebug(definition, input, options);
assert.equal(read.contractVersion, 8);
assert.equal(read.operation, "parse");
assert.equal(read.success, true);
assert.equal(read.root, "header");
assert.deepEqual(read.data, { kind: 2, length: 6 });
assert.deepEqual(read.debug, [
  { start: 0, end: 2, path: "header.kind", type: "uint16", value: "2" },
  { start: 2, end: 6, path: "header.length", type: "uint32", value: "6" },
]);
assert.equal(read.error, null);
const written = await serialize(definition, { kind: 3, length: 6 }, options);
assert.equal(written.success, true);
assert.equal(written.operation, "serialize");
assert.deepEqual(Array.from(written.data), [3, 0, 6, 0, 0, 0]);
const changed = await update(definition, input, "header.kind", 4, options);
assert.equal(changed.success, true);
assert.equal(changed.operation, "update");
assert.deepEqual(Array.from(changed.data), [4, 0, 6, 0, 0, 0]);
assert.deepEqual(Array.from(input), [2, 0, 6, 0, 0, 0]);
const invalid = await parseWithDebug(definition, new Uint8Array(), options);
assert.equal(invalid.success, false);
assert.equal(invalid.data, null);
assert.deepEqual(Object.keys(invalid.error).sort(), ["code", "column", "line", "member", "memberType", "message", "offset", "path"]);
assert.equal(invalid.error.code, "invalid-input");
const truncated = await parseWithDebug(definition, new Uint8Array([2, 0, 6]), options);
assert.equal(truncated.success, false);
assert.equal(truncated.error.code, "read-failed");
assert.equal(truncated.root, "header");
assert.equal(truncated.error.path, "header");
assert.equal(truncated.error.member, "length");
assert.equal(truncated.error.memberType, "uint32");
assert.equal(truncated.error.offset, 3);
const redacted = await parseWithDebug(definition, new Uint8Array([2, 0, 6]), { ...options, redactDiagnostics: true });
assert.equal(redacted.error.code, "read-failed");
assert.match(redacted.error.message, /^Unexpected end of binary input/);
assert.doesNotMatch(redacted.error.message, /length\b.*uint32/);
assert.equal(redacted.error.path, null);
assert.equal(redacted.error.member, null);
const failedUpdate = await update(definition, input, "header.missing", 4, options);
assert.equal(failedUpdate.success, false);
assert.equal(failedUpdate.error.code, "invalid-path");
const large = await serialize(
  "struct large { uint64 value; };",
  { value: 18446744073709551615n },
  { root: "large" },
);
assert.equal(large.success, true);
assert.deepEqual(Array.from(large.data), Array(8).fill(255));
const largeRead = await parseWithDebug("struct large { uint64 value; };", large.data, {
  root: "large",
});
assert.equal(largeRead.data.value, "18446744073709551615");
const largeAddress = await resolveAddress("struct large { uint64 value; };", large.data, "large.value");
assert.equal(largeAddress.success, true);
assert.equal(largeAddress.operation, "resolveAddress");
assert.equal(largeAddress.data, 0);
const metadataDefinition = "struct root { uint8 kind; int24< delta; uleb128_64 count; fixed16_16> revision; uuid network; guid windows; latin1 western[1]; cp437 dos[1]; utf16le little[4]; utf16be big[4]; if(kind == 1) { utf8 label[3]; } else { uint8 raw[3]; } };";
const metadataOptions = { root: "root", aligned: false };
const identifier = "00112233-4455-6677-8899-aabbccddeeff";
const metadata = {
  kind: 1, delta: -2, count: "18446744073709551615", revision: -1.5,
  network: identifier, windows: identifier, western: "é", dos: "é",
  little: "😀", big: "😀", label: "€",
};
const metadataBytes = await serialize(metadataDefinition, metadata, metadataOptions);
assert.equal(metadataBytes.success, true);
assert.deepEqual(Array.from(metadataBytes.data.slice(0, 4)), [1, 254, 255, 255]);
const metadataRead = await parseWithDebug(metadataDefinition, metadataBytes.data, metadataOptions);
assert.equal(metadataRead.success, true);
assert.deepEqual(metadataRead.data, metadata);
const metadataRoundTrip = await serialize(metadataDefinition, metadataRead.data, metadataOptions);
assert.deepEqual(metadataRoundTrip.data, metadataBytes.data);
const metadataUpdate = await update(metadataDefinition, metadataBytes.data, "root.label", "£", metadataOptions);
assert.equal(metadataUpdate.success, true);
const metadataUpdatedRead = await parse(metadataDefinition, metadataUpdate.data, metadataOptions);
assert.equal(metadataUpdatedRead.data.label, "£\0");
const trimmedRead = await parse(metadataDefinition, metadataUpdate.data, { ...metadataOptions, trimFixedText: true });
assert.equal(trimmedRead.data.label, "£");
const changedBranch = await update(metadataDefinition, metadataBytes.data, "root.kind", 0, metadataOptions);
assert.equal(changedBranch.success, false);
assert.equal(changedBranch.error.code, "write-failed");
assert.equal(metadataBytes.data[0], 1);
const rejected = await serialize(definition, { kind: 3, length: 6, extra: 1 }, { ...options, unknownMembers: "reject" });
assert.equal(rejected.success, false);
assert.equal(rejected.error.code, "write-failed");
assert.match(rejected.error.message, /extra/);
await assert.rejects(parseWithDebug(definition, [2]), /Binary chunks/);
const largeInput = new Uint8Array(8 * 1024 * 1024);
largeInput.set(input);
for (const source of [largeInput, new DataView(largeInput.buffer), new Blob([largeInput]),
  Readable.from([Buffer.from(input.subarray(0, 3)), Buffer.from(input.subarray(3))]),
  new Response(input)]) {
  const parsed = await parse(definition, source, options);
  assert.equal(parsed.success, true);
  assert.deepEqual(parsed.data, { kind: 2, length: 6 });
  assert.deepEqual(parsed.debug, []);
}
const largeDebug = await parseWithDebug(definition, largeInput, options);
assert.equal(largeDebug.success, true);
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
  const [a, b] = await Promise.all([compiled.parse(metadataBytes.data), compiled.parseWithDebug(metadataBytes.data)]);
  assert.deepEqual(a.data, metadata);
  assert.deepEqual(b, metadataRead);
  assert.equal(compiled.root, "root");
  const compiledBytes = await compiled.serialize(metadata);
  assert.deepEqual(compiledBytes.data, metadataBytes.data);
  const compiledUpdate = await compiled.update(metadataBytes.data, "root.label", "£");
  assert.deepEqual(compiledUpdate.data, metadataUpdate.data);
  const compiledAddress = await compiled.resolveAddress(metadataBytes.data, "root.label");
  assert.equal(compiledAddress.success, true);
  assert.equal(typeof compiledAddress.data, "number");
} finally { await compiled.dispose(); }
await assert.rejects(compiled.parse(input), /disposed/);
