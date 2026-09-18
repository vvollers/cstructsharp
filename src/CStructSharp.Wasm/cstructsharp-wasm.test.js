import assert from "node:assert/strict";
import test from "node:test";
import { serialize, update, parseWithDebug } from "./cstructsharp-wasm.js";

test("public wrapper returns byte arrays for writes, preserves errors and parse JSON", async () => {
  const previous = globalThis.CStructSharpWasm;
  const written = new Uint8Array([0, 255, 128]);
  let updateInput;
  let shouldFail = false;
  const failure = {
    code: "write-failed",
    message: "Invalid value",
    path: "root.value",
    offset: 0,
    member: "value",
    memberType: "uint8",
    line: null,
    column: null,
  };
  globalThis.CStructSharpWasm = {
    ready: true,
    serialize: () => {
      if (shouldFail) {
        throw new Error(JSON.stringify(failure));
      }
      return written;
    },
    collectBytes: async (source) => source,
    updateStream: (_definition, bytes) => {
      updateInput = bytes;
      if (shouldFail) {
        throw new Error(JSON.stringify(failure));
      }
      return written;
    },
    parseWithDebug: () =>
      JSON.stringify({
        contractVersion: 8,
        operation: "parse",
        success: true,
        root: "root",
        data: { value: 2 },
        debug: [],
        error: null,
      }),
  };
  try {
    const serialized = await serialize("layout", {}, { root: "root" });
    assert.equal(serialized.contractVersion, 8);
    assert.equal(serialized.operation, "serialize");
    assert.equal(serialized.success, true);
    assert.equal(serialized.root, "root");
    assert.ok(serialized.data instanceof Uint8Array);
    assert.deepEqual(serialized.data, written);
    assert.deepEqual(serialized.debug, []);
    assert.equal(serialized.error, null);

    const updated = await update("layout", serialized.data, "root.value", 2);
    // No Base64 round trip: the managed export receives the exact same bytes instance.
    assert.equal(updateInput, serialized.data);
    assert.equal(updated.operation, "update");
    assert.equal(updated.root, null);
    assert.deepEqual(updated.data, written);

    const parsed = await parseWithDebug("layout", updated.data);
    assert.equal(parsed.root, "root");
    assert.deepEqual(parsed.data, { value: 2 });

    shouldFail = true;
    const failedSerialize = await serialize("layout", {});
    assert.equal(failedSerialize.success, false);
    assert.equal(failedSerialize.data, null);
    assert.deepEqual(failedSerialize.error, failure);

    const failedUpdate = await update("layout", written, "root.value", 999);
    assert.equal(failedUpdate.success, false);
    assert.equal(failedUpdate.data, null);
    assert.deepEqual(failedUpdate.error, failure);
  } finally {
    globalThis.CStructSharpWasm = previous;
  }
});

test("parse takes the synchronous path for small byte inputs and the worker path otherwise", async () => {
  const previous = globalThis.CStructSharpWasm;
  const calls = [];
  const envelope = (data) =>
    JSON.stringify({ contractVersion: 8, operation: "parse", success: true, root: "root", data, debug: [], error: null });
  globalThis.CStructSharpWasm = {
    ready: true,
    parseBytes: (definition, bytes, options, debug) => {
      calls.push(["parseBytes", bytes.byteLength, options, debug]);
      return envelope({ value: 1 });
    },
    parseSource: async (definition, source, options, debug) => {
      calls.push(["parseSource", source.byteLength ?? source.size, options, debug]);
      return JSON.parse(envelope({ value: 2 }));
    },
  };
  try {
    const { parse } = await import("./cstructsharp-wasm.js");
    const small = new Uint8Array(16);
    const view = new DataView(new ArrayBuffer(32), 8, 8);
    const large = new Uint8Array(64 * 1024 + 1);
    const controller = new AbortController();

    assert.deepEqual((await parse("layout", small, { root: "root" })).data, { value: 1 });
    assert.deepEqual((await parse("layout", view)).data, { value: 1 });
    assert.deepEqual((await parse("layout", large)).data, { value: 2 });
    assert.deepEqual((await parse("layout", small, { signal: controller.signal })).data, { value: 2 });
    assert.deepEqual((await parse("layout", new Blob([small]))).data, { value: 2 });

    assert.deepEqual(
      calls.map(([name, size]) => [name, size]),
      [["parseBytes", 16], ["parseBytes", 8], ["parseSource", 65537], ["parseSource", 16], ["parseSource", 16]],
    );
    assert.deepEqual(calls[0][2], { root: "root" });
    assert.equal(calls[0][3], false);
  } finally {
    globalThis.CStructSharpWasm = previous;
  }
});
