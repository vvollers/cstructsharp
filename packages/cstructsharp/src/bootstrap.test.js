import assert from "node:assert/strict";
import fs from "node:fs";
import test from "node:test";

import { MAIN_THREAD_EXPORTS, WORKER_EXPORTS, createCStructSharpWasm } from "./bootstrap.js";
import { decodeEnvelopeText } from "./cstructsharp-shared.js";

/**
 * The JSON text of an envelope as the managed exports write it.
 * @param {string} operation The operation name.
 * @param {unknown} data The data member; null on failure.
 * @param {object | null} [error] The error details of a failure.
 * @param {string | null} [root] The root member.
 * @returns {string} The envelope JSON.
 */
function managedEnvelope(operation, data, error = null, root = null) {
  return JSON.stringify({ contractVersion: 10, operation, success: error === null, root, data, debug: [], error });
}

/**
 * Builds fake managed exports that record every call and follow the managed transport: parses return UTF-8 bytes,
 * writes return an envelope with `byteLength` and leave their bytes for TakeOutput.
 * @param {Array<[string, unknown[]]>} calls Receives each export name with its arguments.
 * @returns {object} The nested export shape the runtime produces.
 */
function createExports(calls) {
  /** The bytes the last write left for TakeOutput. */
  let pending = null;
  const managed = {
    /** Records a byte parse and returns a marker as UTF-8 bytes, the way the parse exports return envelopes. */
    ParseBytes(...args) {
      calls.push(["ParseBytes", args]);
      return new TextEncoder().encode("parse-bytes");
    },
    /** Records a serialize and leaves one byte pending. */
    Serialize(...args) {
      calls.push(["Serialize", args]);
      pending = new Uint8Array([0x2a]);
      return managedEnvelope("serialize", { byteLength: 1 });
    },
    /** Records an update and leaves one byte pending. */
    UpdateStream(...args) {
      calls.push(["UpdateStream", args]);
      pending = new Uint8Array([0x2a]);
      return managedEnvelope("update", { byteLength: 1 });
    },
    /** Hands over the pending bytes once; throws like the managed export when none are pending. */
    TakeOutput() {
      calls.push(["TakeOutput", []]);
      if (pending === null) throw new Error("No operation output is pending.");
      const output = pending;
      pending = null;
      return output;
    },
    /** Returns the version envelope. */
    GetVersion() {
      calls.push(["GetVersion", []]);
      return managedEnvelope("version", { version: "CStructSharp WASM 1.2.3" });
    },
    /** Records a plan request; data null means the root is not fully fixed. */
    GetStaticPlan(...args) {
      calls.push(["GetStaticPlan", args]);
      return managedEnvelope("staticPlan", null, null, "root");
    },
  };

  // The source worker calls these in its own runtime; the page's adapter only checks that they exist.
  for (const name of WORKER_EXPORTS) {
    managed[name] = () => {
      throw new Error(`${name} runs in the source worker, not on the page.`);
    };
  }

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

  assert.equal(adapter.parseWithDebug("layout", bytes), "parse-bytes");
  assert.equal(
    adapter.parseWithDebug("layout", bytes, {
      root: "root",
      aligned: true,
      pointerSize: 4,
    }),
    "parse-bytes",
  );
  assert.deepEqual(
    adapter.serialize("layout", "{}", {
      root: null,
      aligned: false,
      pointerSize: 8,
    }),
    { contractVersion: 10, operation: "serialize", success: true, root: null, data: new Uint8Array([0x2a]), debug: [], error: null },
  );
  assert.deepEqual(
    adapter.updateStream("layout", bytes, "root.value", "42", {
      aligned: false,
      pointerSize: 8,
      addressingMode: "Relative",
      origin: 9_007_199_254_740_993n,
      dereferencePointers: true,
    }).data,
    new Uint8Array([0x2a]),
  );
  assert.equal(adapter.parseBytes("layout", bytes, { root: "root" }, false), "parse-bytes");
  assert.deepEqual(adapter.getStaticPlan("layout", { root: "root" }), {
    contractVersion: 10,
    operation: "staticPlan",
    success: true,
    root: "root",
    data: null,
    debug: [],
    error: null,
  });
  assert.equal(adapter.getVersion(), "CStructSharp WASM 1.2.3");
  assert.equal(adapter.ready, true);
  assert.equal(adapter.error, null);

  assert.deepEqual(calls, [
    ["ParseBytes", ["layout", bytes, "{}", true]],
    ["ParseBytes", ["layout", bytes, '{"root":"root","aligned":true,"pointerSize":4}', true]],
    ["Serialize", ["layout", "{}", '{"root":null,"aligned":false,"pointerSize":8}']],
    ["TakeOutput", []],
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
    ["TakeOutput", []],
    ["ParseBytes", ["layout", bytes, '{"root":"root"}', false]],
    ["GetStaticPlan", ["layout", '{"root":"root"}']],
    ["GetVersion", []],
  ]);
  // The main-thread list names exactly the exports the adapter's synchronous operations call.
  assert.deepEqual(new Set(calls.map(([name]) => name)), new Set(MAIN_THREAD_EXPORTS));
});

test("the worker list names exactly the managed exports the source worker calls", () => {
  const worker = fs.readFileSync(new URL("./source-worker.js", import.meta.url), "utf8");
  const called = new Set([...worker.matchAll(/managed\.(\w+)\(/g)].map((match) => match[1]));

  assert.deepEqual(called, new Set(WORKER_EXPORTS));
  assert.deepEqual(
    MAIN_THREAD_EXPORTS.filter((name) => WORKER_EXPORTS.includes(name)),
    [],
  );
});

test("a failed write returns the managed error envelope without taking output", () => {
  const calls = [];
  const exports = createExports(calls);
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
  exports.CStructSharpWeb.Wasm.CStructExports.Serialize = () => managedEnvelope("serialize", null, failure, "root");
  const adapter = createCStructSharpWasm(exports);

  assert.deepEqual(adapter.serialize("layout", "{}", { root: "root" }), {
    contractVersion: 10,
    operation: "serialize",
    success: false,
    root: "root",
    data: null,
    debug: [],
    error: failure,
  });
  assert.deepEqual(calls, []);
});

test("a write whose handed-over bytes do not match the envelope is rejected", () => {
  const exports = createExports([]);
  exports.CStructSharpWeb.Wasm.CStructExports.Serialize = () => managedEnvelope("serialize", { byteLength: 2 });
  exports.CStructSharpWeb.Wasm.CStructExports.TakeOutput = () => new Uint8Array([1]);
  const adapter = createCStructSharpWasm(exports);

  assert.throws(() => adapter.serialize("layout", "{}"), /invalid serialize output/);
});

test("adapter rejects a missing main-thread or worker export at initialization", () => {
  for (const name of [...MAIN_THREAD_EXPORTS, ...WORKER_EXPORTS]) {
    const exports = createExports([]);
    delete exports.CStructSharpWeb.Wasm.CStructExports[name];

    assert.throws(
      () => createCStructSharpWasm(exports),
      new RegExp(`Managed CStruct exports are missing: ${name}`),
    );
  }
});

/** A parse export that returns text instead of UTF-8 bytes breaks the transport, so the adapter reports it. */
test("a parse export that does not return bytes is an invalid envelope", () => {
  const exports = createExports([]);
  exports.CStructSharpWeb.Wasm.CStructExports.ParseBytes = () => managedEnvelope("parse", { value: 1 });
  const adapter = createCStructSharpWasm(exports);

  assert.throws(() => adapter.parseBytes("layout", new Uint8Array([1])), {
    name: "TypeError",
    message: /invalid parse response envelope/,
  });
  assert.throws(() => adapter.parseWithDebug("layout", new Uint8Array([1])), TypeError);
});

/** Parse envelopes decode as UTF-8: escaped JSON text and raw multi-byte characters both round-trip. */
test("decodeEnvelopeText decodes UTF-8 envelope bytes and rejects other values", () => {
  const text = managedEnvelope("parse", { name: "café ☃", escaped: "\\u00e9" });

  assert.equal(decodeEnvelopeText(new TextEncoder().encode(text), "parse"), text);
  assert.equal(decodeEnvelopeText(new Uint8Array(0), "parse"), "");
  for (const value of [text, null, undefined, new ArrayBuffer(1), [123, 125]]) {
    assert.throws(() => decodeEnvelopeText(value, "parse"), {
      name: "TypeError",
      message: "CStructSharp returned an invalid parse response envelope.",
    });
  }
});

test("adapter accepts the flat export shape emitted by some runtimes", () => {
  const calls = [];
  const nested = createExports(calls);
  const flat = nested.CStructSharpWeb.Wasm;

  assert.equal(createCStructSharpWasm(flat).getVersion(), "CStructSharp WASM 1.2.3");
});
