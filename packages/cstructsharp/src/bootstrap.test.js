import assert from "node:assert/strict";
import test from "node:test";

import { createCStructSharpWasm } from "./bootstrap.js";

function createExports(calls) {
  const managed = {
    ParseWithDebug(...args) {
      calls.push(["ParseWithDebug", args]);
      return "parse-default";
    },
    ParseBytes(...args) {
      calls.push(["ParseBytes", args]);
      return "parse-bytes";
    },
    Serialize(...args) {
      calls.push(["Serialize", args]);
      return new Uint8Array([0x2a]);
    },
    UpdateStream(...args) {
      calls.push(["UpdateStream", args]);
      return new Uint8Array([0x2a]);
    },
    ResolveAddress(...args) {
      calls.push(["ResolveAddress", args]);
      return "resolve-address";
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
  const bytes = new Uint8Array([0, 0]);

  assert.equal(adapter.parseWithDebug("layout", bytes), "parse-default");
  assert.equal(
    adapter.parseWithDebug("layout", bytes, {
      root: "root",
      aligned: true,
      pointerSize: 4,
    }),
    "parse-default",
  );
  assert.deepEqual(
    adapter.serialize("layout", "{}", {
      root: null,
      aligned: false,
      pointerSize: 8,
    }),
    new Uint8Array([0x2a]),
  );
  assert.deepEqual(
    adapter.updateStream("layout", bytes, "root.value", "42", {
      aligned: false,
      pointerSize: 8,
      addressingMode: "Relative",
      origin: 9_007_199_254_740_993n,
      dereferencePointers: true,
    }),
    new Uint8Array([0x2a]),
  );
  assert.equal(adapter.parseBytes("layout", bytes, { root: "root" }, false), "parse-bytes");
  assert.equal(adapter.resolveAddress("layout", bytes, "root.value", { root: "root" }), "resolve-address");
  assert.equal(adapter.getVersion(), "version");
  assert.equal(adapter.ready, true);
  assert.equal(adapter.error, null);

  assert.deepEqual(calls, [
    ["ParseWithDebug", ["layout", bytes, "{}"]],
    ["ParseWithDebug", ["layout", bytes, '{"root":"root","aligned":true,"pointerSize":4}']],
    ["Serialize", ["layout", "{}", '{"root":null,"aligned":false,"pointerSize":8}']],
    [
      "UpdateStream",
      [
        "layout",
        bytes,
        "root.value",
        "42",
        '{"aligned":false,"pointerSize":8,"addressingMode":"Relative","origin":"9007199254740993","dereferencePointers":true}',
      ],
    ],
    ["ParseBytes", ["layout", bytes, '{"root":"root"}', false]],
    ["ResolveAddress", ["layout", bytes, "root.value", '{"root":"root"}']],
    ["GetVersion", []],
  ]);
});

test("adapter rejects a missing managed export at initialization", () => {
  const exports = createExports([]);
  delete exports.CStructSharpWeb.Wasm.CStructExports.UpdateStream;

  assert.throws(
    () => createCStructSharpWasm(exports),
    /Managed CStruct exports are missing: UpdateStream/,
  );
});

test("adapter accepts the flat export shape emitted by some runtimes", () => {
  const calls = [];
  const nested = createExports(calls);
  const flat = nested.CStructSharpWeb.Wasm;

  assert.equal(createCStructSharpWasm(flat).getVersion(), "version");
});
