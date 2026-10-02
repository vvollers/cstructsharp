import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { fileURLToPath, pathToFileURL } from "node:url";
import { MAIN_THREAD_EXPORTS, WORKER_EXPORTS, createCStructSharpWasm } from "./bootstrap.js";
import { serialize, update, parseWithDebug, getVersion } from "./cstructsharp-wasm.js";

/** The member order of every envelope, as the managed writer produces it. */
const ENVELOPE_KEYS = ["contractVersion", "operation", "success", "root", "data", "debug", "error"];

test("public wrapper returns byte arrays for writes, preserves errors and parse JSON", async () => {
  const previous = globalThis.CStructSharpWasm;
  const written = new Uint8Array([0, 255, 128]);
  let updateInput;
  let shouldFail = false;
  /** The bytes the last successful write left for TakeOutput, as in managed memory. */
  let pending = null;
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
  /** The managed envelope JSON of a write: the byte length on success, the error on failure. */
  const writeEnvelope = (operation, optionsJson) => {
    const root = JSON.parse(optionsJson).root ?? null;
    if (shouldFail) {
      pending = null;
      return JSON.stringify({ contractVersion: 10, operation, success: false, root, data: null, debug: [], error: failure });
    }
    pending = written;
    return JSON.stringify({ contractVersion: 10, operation, success: true, root, data: { byteLength: written.byteLength }, debug: [], error: null });
  };
  // Fake managed exports that follow the envelope-plus-TakeOutput transport.
  const managed = {
    /** Returns a successful parse envelope for any input, as UTF-8 bytes like the managed parse exports. */
    ParseBytes: () =>
      new TextEncoder().encode(
        JSON.stringify({ contractVersion: 10, operation: "parse", success: true, root: "root", data: { value: 2 }, debug: [], error: null }),
      ),
    /** Returns the serialize envelope and leaves the bytes pending. */
    Serialize: (_definition, _json, optionsJson) => writeEnvelope("serialize", optionsJson),
    /** Records the input bytes, returns the update envelope and leaves the bytes pending. */
    UpdateStream: (_definition, bytes, _path, _json, optionsJson) => {
      updateInput = bytes;
      return writeEnvelope("update", optionsJson);
    },
    /** Hands over the pending bytes once. */
    TakeOutput: () => {
      const output = pending;
      pending = null;
      return output;
    },
    /** Returns the version envelope. */
    GetVersion: () =>
      JSON.stringify({
        contractVersion: 10,
        operation: "version",
        success: true,
        root: null,
        data: { version: "CStructSharp WASM 9.9.9" },
        debug: [],
        error: null,
      }),
    /** Unused: the adapter is only asked for plans by parse, which this test does not call. */
    GetStaticPlan: () => "",
  };
  // The source worker's exports run in their own runtime; the adapter only checks that they exist.
  for (const name of WORKER_EXPORTS) {
    managed[name] = () => {
      throw new Error(`${name} runs in the source worker, not on the page.`);
    };
  }
  // The real adapter over those exports, so the public functions run the whole JavaScript side of the transport.
  globalThis.CStructSharpWasm = {
    ...createCStructSharpWasm({ CStructExports: managed }),
    /** Uses a byte input as is, as the real adapter does. */
    collectBytes: async (source) => source,
  };
  try {
    const serialized = await serialize("layout", {}, { root: "root" });
    assert.deepEqual(Object.keys(serialized), ENVELOPE_KEYS);
    assert.equal(serialized.contractVersion, 10);
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
    assert.equal(await getVersion(), "CStructSharp WASM 9.9.9");

    shouldFail = true;
    const failedSerialize = await serialize("layout", {});
    assert.deepEqual(Object.keys(failedSerialize), ENVELOPE_KEYS);
    assert.equal(failedSerialize.success, false);
    assert.equal(failedSerialize.root, null);
    assert.equal(failedSerialize.data, null);
    assert.deepEqual(failedSerialize.debug, []);
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
  /** Builds a successful parse envelope's JSON around the given data. */
  const envelope = (data) =>
    JSON.stringify({ contractVersion: 10, operation: "parse", success: true, root: "root", data, debug: [], error: null });
  globalThis.CStructSharpWasm = {
    ready: true,
    /** A layout that is not fully fixed has no static plan, so small inputs cross into WASM. */
    getStaticPlan: () => ({ contractVersion: 10, operation: "staticPlan", success: true, root: "root", data: null, debug: [], error: null }),
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

test("parseWithDebug routes byte inputs by the rule parse uses", async () => {
  const previous = globalThis.CStructSharpWasm;
  const calls = [];
  /** Builds a successful debug parse envelope around the given data. */
  const envelope = (data) => ({ contractVersion: 10, operation: "parse", success: true, root: "root", data, debug: [], error: null });
  globalThis.CStructSharpWasm = {
    ready: true,
    /** The synchronous debug parse, which receives a Uint8Array over the caller's bytes. */
    parseWithDebug: (definition, bytes, options) => {
      calls.push(["parseWithDebug", bytes instanceof Uint8Array, bytes.byteLength, options]);
      return JSON.stringify(envelope({ value: 1 }));
    },
    /** The staged worker path. */
    parseSource: async (definition, source, options, debug) => {
      calls.push(["parseSource", debug, source.byteLength ?? source.size, options]);
      return envelope({ value: 2 });
    },
  };
  try {
    const { parseWithDebug } = await import("./cstructsharp-wasm.js");
    const controller = new AbortController();

    // Any byte view up to 4 MiB without a signal parses on the calling thread; larger inputs and a signal use the worker.
    assert.deepEqual((await parseWithDebug("layout", new ArrayBuffer(16))).data, { value: 1 });
    assert.deepEqual((await parseWithDebug("layout", new DataView(new ArrayBuffer(32), 8, 8))).data, { value: 1 });
    assert.deepEqual((await parseWithDebug("layout", new Uint8Array(64 * 1024 + 1))).data, { value: 1 });
    assert.deepEqual((await parseWithDebug("layout", new Uint8Array(4 * 1024 * 1024 + 1))).data, { value: 2 });
    assert.deepEqual((await parseWithDebug("layout", new Uint8Array(16), { signal: controller.signal })).data, { value: 2 });

    assert.deepEqual(
      calls.map(([name, flag, size]) => [name, flag, size]),
      [
        ["parseWithDebug", true, 16],
        ["parseWithDebug", true, 8],
        ["parseWithDebug", true, 65537],
        ["parseSource", true, 4194305],
        ["parseSource", true, 16],
      ],
    );
  } finally {
    globalThis.CStructSharpWasm = previous;
  }
});

test("the standalone loader reports the runtime's own startup error and retries after a failure", async (t) => {
  // A copy of the bundle's modules beside a fake .NET runtime whose first start fails, as a failed download would.
  const bundle = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-loader-"));
  t.after(() => fs.rmSync(bundle, { recursive: true, force: true }));
  const sources = path.dirname(fileURLToPath(import.meta.url));
  for (const name of fs.readdirSync(sources).filter((entry) => entry.endsWith(".js") && !entry.endsWith(".test.js"))) {
    fs.copyFileSync(path.join(sources, name), path.join(bundle, name));
  }
  fs.mkdirSync(path.join(bundle, "_framework"));
  fs.writeFileSync(
    path.join(bundle, "_framework", "dotnet.js"),
    `export const dotnet = {
  async create() {
    globalThis.fakeRuntimeStarts = (globalThis.fakeRuntimeStarts ?? 0) + 1;
    if (globalThis.fakeRuntimeStarts === 1) throw new Error("Failed to fetch dotnet.native.wasm (503)");
    return { getAssemblyExports: async () => ({ CStructExports: globalThis.fakeManagedExports }) };
  },
};
`,
  );

  const managed = {};
  for (const name of [...MAIN_THREAD_EXPORTS, ...WORKER_EXPORTS]) {
    managed[name] = () => {
      throw new Error(`${name} is not used by this test.`);
    };
  }
  /** Returns the version envelope of the fake runtime. */
  managed.GetVersion = () =>
    JSON.stringify({ contractVersion: 10, operation: "version", success: true, root: null, data: { version: "fake" }, debug: [], error: null });

  // main.js publishes on window and announces the outcome with an event; Node has neither, so the test lends both.
  const saved = { window: globalThis.window, dispatchEvent: globalThis.dispatchEvent, adapter: globalThis.CStructSharpWasm };
  t.after(() => {
    globalThis.window = saved.window;
    globalThis.dispatchEvent = saved.dispatchEvent;
    globalThis.CStructSharpWasm = saved.adapter;
    delete globalThis.fakeRuntimeStarts;
    delete globalThis.fakeManagedExports;
  });
  globalThis.window = globalThis;
  globalThis.dispatchEvent = () => true;
  globalThis.fakeManagedExports = managed;
  delete globalThis.CStructSharpWasm;
  t.mock.method(console, "error", () => {});

  const { loadCStructSharpWasm, getVersion } = await import(pathToFileURL(path.join(bundle, "cstructsharp-wasm.js")).href);

  await assert.rejects(loadCStructSharpWasm(), /^Error: CStructSharp WASM failed to load: Failed to fetch dotnet\.native\.wasm \(503\)$/);
  const adapter = await loadCStructSharpWasm();
  assert.equal(adapter.ready, true);
  assert.equal(globalThis.fakeRuntimeStarts, 2);
  assert.equal(await getVersion(), "fake");
  assert.equal(await loadCStructSharpWasm(), adapter);
});
