import assert from "node:assert/strict";
import test from "node:test";
import { serialize, update, parseWithDebug } from "./cstructsharp-wasm.js";

test("public wrapper returns byte arrays for writes, preserves errors and parse JSON", async () => {
  const previous = globalThis.CStructSharpWasm;
  const written = new Uint8Array([0, 255, 128]);
  let updateInput;
  let shouldFail = false;
  const failure = {
    Code: "write-failed",
    Message: "Invalid value",
    Path: "root.value",
    Offset: 0,
  };
  globalThis.CStructSharpWasm = {
    ready: true,
    serialize: () => {
      if (shouldFail) {
        throw new Error(JSON.stringify(failure));
      }
      return written;
    },
    updateStream: (_definition, bytes) => {
      updateInput = bytes;
      if (shouldFail) {
        throw new Error(JSON.stringify(failure));
      }
      return written;
    },
    parseWithDebug: () =>
      JSON.stringify({
        ContractVersion: 7,
        Operation: "parse",
        Success: true,
        Data: { root: { value: 2 } },
        DebugData: [],
        Error: null,
      }),
  };
  try {
    const serialized = await serialize("layout", {});
    assert.equal(serialized.Success, true);
    assert.ok(serialized.Data instanceof Uint8Array);
    assert.deepEqual(serialized.Data, written);

    const updated = await update("layout", serialized.Data, "root.value", 2);
    // No Base64 round trip: the managed export receives the exact same bytes instance.
    assert.equal(updateInput, serialized.Data);
    assert.deepEqual(updated.Data, written);

    assert.deepEqual((await parseWithDebug("layout", updated.Data)).Data, { root: { value: 2 } });

    shouldFail = true;
    const failedSerialize = await serialize("layout", {});
    assert.equal(failedSerialize.Success, false);
    assert.equal(failedSerialize.Data, null);
    assert.deepEqual(failedSerialize.Error, failure);

    const failedUpdate = await update("layout", written, "root.value", 999);
    assert.equal(failedUpdate.Success, false);
    assert.equal(failedUpdate.Data, null);
    assert.deepEqual(failedUpdate.Error, failure);
  } finally {
    globalThis.CStructSharpWasm = previous;
  }
});

test("parse takes the synchronous path for small byte inputs and the worker path otherwise", async () => {
  const previous = globalThis.CStructSharpWasm;
  const calls = [];
  const envelope = (data) =>
    JSON.stringify({ ContractVersion: 7, Operation: "parse", Success: true, Data: data, DebugData: [], Error: null });
  globalThis.CStructSharpWasm = {
    ready: true,
    parseBytes: (definition, bytes, options, debug) => {
      calls.push(["parseBytes", bytes.byteLength, options, debug]);
      return envelope('{"root":{"value":1}}');
    },
    parseSource: async (definition, source, options, debug) => {
      calls.push(["parseSource", source.byteLength ?? source.size, options, debug]);
      return JSON.parse(envelope('{"root":{"value":2}}'));
    },
  };
  try {
    const { parse } = await import("./cstructsharp-wasm.js");
    const small = new Uint8Array(16);
    const view = new DataView(new ArrayBuffer(32), 8, 8);
    const large = new Uint8Array(64 * 1024 + 1);
    const controller = new AbortController();

    assert.equal((await parse("layout", small, { rootTypeName: "root" })).Data, '{"root":{"value":1}}');
    assert.equal((await parse("layout", view)).Data, '{"root":{"value":1}}');
    assert.equal((await parse("layout", large)).Data, '{"root":{"value":2}}');
    assert.equal((await parse("layout", small, { signal: controller.signal })).Data, '{"root":{"value":2}}');
    assert.equal((await parse("layout", new Blob([small]))).Data, '{"root":{"value":2}}');

    assert.deepEqual(
      calls.map(([name, size]) => [name, size]),
      [["parseBytes", 16], ["parseBytes", 8], ["parseSource", 65537], ["parseSource", 16], ["parseSource", 16]],
    );
    assert.deepEqual(calls[0][2], { rootTypeName: "root" });
    assert.equal(calls[0][3], false);
  } finally {
    globalThis.CStructSharpWasm = previous;
  }
});
