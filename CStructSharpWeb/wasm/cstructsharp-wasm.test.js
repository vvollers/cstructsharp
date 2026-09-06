import assert from "node:assert/strict";
import test from "node:test";
import { serialize, update, parseWithDebug } from "./cstructsharp-wasm.js";

test("public wrapper returns byte arrays for writes, preserves errors and parse JSON", async () => {
  const previous = globalThis.CStructSharpWasm;
  const result = { ContractVersion: 4, Success: true, Data: "AP+A", DebugData: [], Error: null };
  let updateInput;
  globalThis.CStructSharpWasm = {
    ready: true,
    serializeToBase64: () => JSON.stringify(result),
    updateStreamToBase64: (_definition, bytes) => {
      updateInput = bytes;
      return JSON.stringify(result);
    },
    parseWithDebug: () => JSON.stringify({ ...result, Data: '{"root":{"value":2}}' }),
  };
  try {
    const written = await serialize("layout", {});
    assert.ok(written.Data instanceof Uint8Array);
    assert.deepEqual(written.Data, new Uint8Array([0, 255, 128]));
    const updated = await update("layout", written.Data, "root.value", 2);
    assert.equal(updateInput, "AP+A");
    assert.deepEqual(updated.Data, written.Data);
    assert.notEqual(updated.Data, written.Data);
    assert.equal((await parseWithDebug("layout", updated.Data)).Data, '{"root":{"value":2}}');
    result.Data = "";
    assert.deepEqual((await serialize("layout", {})).Data, new Uint8Array());
    result.Success = false;
    result.Data = null;
    result.Error = {
      Code: "write-failed",
      Message: "Invalid value",
      Path: "root.value",
      Offset: 0,
    };
    assert.deepEqual(await serialize("layout", {}), result);
    assert.deepEqual(await update("layout", written.Data, "root.value", 999), result);
  } finally {
    globalThis.CStructSharpWasm = previous;
  }
});
