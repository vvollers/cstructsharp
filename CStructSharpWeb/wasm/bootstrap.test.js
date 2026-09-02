import assert from "node:assert/strict";
import test from "node:test";

import { createCStructSharpWasm } from "./bootstrap.js";

function createExports(calls) {
  const managed = {
    ParseWithDebug(...args) {
      calls.push(["ParseWithDebug", args]);
      return "parse-default";
    },
    SerializeToBase64(...args) {
      calls.push(["SerializeToBase64", args]);
      return "serialize";
    },
    UpdateStreamToBase64(...args) {
      calls.push(["UpdateStreamToBase64", args]);
      return "update";
    },
    GetVersion() {
      calls.push(["GetVersion", []]);
      return "version";
    },
  };

  return {
    CStructSharpWeb: {
      Wasm: {
        CStructExports: managed,
      },
    },
  };
}

test("adapter binds every managed export and normalizes boundary values", () => {
  const calls = [];
  const adapter = createCStructSharpWasm(createExports(calls));

  assert.equal(adapter.parseWithDebug("layout", "AA=="), "parse-default");
  assert.equal(
    adapter.parseWithDebug("layout", "AA==", {
      rootTypeName: "root",
      aligned: true,
      pointerSize: 4,
    }),
    "parse-default",
  );
  assert.equal(
    adapter.serializeToBase64("layout", "{}", {
      rootTypeName: null,
      aligned: false,
      pointerSize: 8,
    }),
    "serialize",
  );
  assert.equal(
    adapter.updateStreamToBase64("layout", "AA==", "root.value", "42", {
      aligned: false,
      pointerSize: 8,
      addressingMode: "Relative",
      origin: 9_007_199_254_740_993n,
      allowPointerDereference: true,
    }),
    "update",
  );
  assert.equal(adapter.getVersion(), "version");
  assert.equal(adapter.ready, true);
  assert.equal(adapter.error, null);

  assert.deepEqual(calls, [
    ["ParseWithDebug", ["layout", "AA==", "{}"]],
    [
      "ParseWithDebug",
      ["layout", "AA==", '{"rootTypeName":"root","aligned":true,"pointerSize":4}'],
    ],
    [
      "SerializeToBase64",
      ["layout", "{}", '{"rootTypeName":null,"aligned":false,"pointerSize":8}'],
    ],
    [
      "UpdateStreamToBase64",
      [
        "layout",
        "AA==",
        "root.value",
        "42",
        '{"aligned":false,"pointerSize":8,"addressingMode":"Relative","origin":"9007199254740993","allowPointerDereference":true}',
      ],
    ],
    ["GetVersion", []],
  ]);
});

test("adapter rejects a missing managed export at initialization", () => {
  const exports = createExports([]);
  delete exports.CStructSharpWeb.Wasm.CStructExports.UpdateStreamToBase64;

  assert.throws(
    () => createCStructSharpWasm(exports),
    /Managed CStruct exports are missing: UpdateStreamToBase64/,
  );
});

test("adapter accepts the flat export shape emitted by some runtimes", () => {
  const calls = [];
  const nested = createExports(calls);
  const flat = nested.CStructSharpWeb.Wasm;

  assert.equal(createCStructSharpWasm(flat).getVersion(), "version");
});
