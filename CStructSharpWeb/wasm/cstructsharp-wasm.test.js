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
        ContractVersion: 5,
        Operation: "parse",
        Success: true,
        Data: '{"root":{"value":2}}',
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

    assert.equal((await parseWithDebug("layout", updated.Data)).Data, '{"root":{"value":2}}');

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
