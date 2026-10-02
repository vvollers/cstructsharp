import assert from "node:assert/strict";
import test from "node:test";

/**
 * The staticPlan envelope the adapter returns for a plan, as the managed export writes it.
 * @param {object | null} plan The `{ root, plan }` data, or null when the layout has no static plan.
 * @param {number} [contractVersion] The contract version the managed export states.
 * @returns {object} The envelope.
 */
function planEnvelope(plan, contractVersion = 10) {
  return { contractVersion, operation: "staticPlan", success: true, root: plan?.root ?? "root", data: plan, debug: [], error: null };
}

test("parse executes a fully fixed layout's static plan in JavaScript and falls back to WASM otherwise", async () => {
  const previous = globalThis.CStructSharpWasm;
  const calls = [];
  const plan = {
    root: "root",
    plan: {
      size: 26,
      ops: [
        { name: "kind", o: 0, k: "n", t: "u8", le: true },
        { name: "tag", o: 1, k: "c", n: 3 },
        { name: "mode", o: 4, k: "e", t: "i8", le: true, enum: "mode", members: [{ v: -1, n: "off" }, { v: 2, n: "on" }] },
        {
          name: "leaf",
          o: 5,
          k: "s",
          p: { size: 5, ops: [{ name: "k", o: 0, k: "n", t: "u8", le: true }, { name: "v", o: 1, k: "n", t: "u32", le: true }] },
        },
        { name: "pairs", o: 10, k: "sa", n: 2, p: { size: 2, ops: [{ name: "a", o: 0, k: "n", t: "i16", le: false }] } },
        { name: "samples", o: 14, k: "a", n: 2, t: "u16", le: true },
        { name: "big", o: 18, k: "n", t: "u64", le: true },
      ],
    },
  };
  globalThis.CStructSharpWasm = {
    ready: true,
    /** Records the request; only the "static" definition has a plan. */
    getStaticPlan: (definition, options) => {
      calls.push(["getStaticPlan", definition, options]);
      return planEnvelope(definition === "static" ? structuredClone(plan) : null);
    },
    /** Records the WASM parse and returns a marker envelope. */
    parseBytes: (definition, bytes, options, debug) => {
      calls.push(["parseBytes", definition, bytes.byteLength, options, debug]);
      return JSON.stringify({
        contractVersion: 10,
        operation: "parse",
        success: true,
        root: "root",
        data: "wasm",
        debug: [],
        error: null,
      });
    },
  };
  try {
    const { parse } = await import("./cstructsharp-wasm.js");
    const bytes = new Uint8Array([
      7, 0x61, 0x00, 0xff, 0xfe, 9, 1, 0, 0, 0, 0x01, 0x02, 0xff, 0xfe, 0x34, 0x12, 0x78, 0x56, 0xff, 0xff, 0xff, 0xff,
      0xff, 0xff, 0x1f, 0x00,
    ]);
    const native = await parse("static", bytes, { root: "root" });
    assert.deepEqual(native, {
      contractVersion: 10,
      operation: "parse",
      success: true,
      root: "root",
      data: {
        kind: 7,
        tag: "a\u0000ÿ",
        mode: { kind: "enum", enum: "mode", name: null, value: -2 },
        leaf: { k: 9, v: 1 },
        pairs: [{ a: 0x0102 }, { a: -2 }],
        samples: [0x1234, 0x5678],
        big: 9007199254740991,
      },
      debug: [],
      error: null,
    });
    // A 64-bit value beyond the exact range is a decimal string, like the projection writes it.
    bytes.fill(0, 18, 24);
    bytes[24] = 0x20;
    assert.equal((await parse("static", bytes, { root: "root" })).data.big, "9007199254740992");
    // The plan is cached per definition and options: one managed description served both parses.
    assert.equal(calls.filter(([name]) => name === "getStaticPlan").length, 1);

    // Read-semantics options, short buffers and non-fixed layouts go to WASM.
    assert.equal((await parse("static", bytes, { root: "root", maxTotalBytesRead: 4 })).data, "wasm");
    assert.equal((await parse("static", bytes.subarray(0, 10), { root: "root" })).data, "wasm");
    assert.equal((await parse("dynamic", bytes, { root: "root" })).data, "wasm");
    assert.deepEqual(
      calls.filter(([name]) => name === "parseBytes").map(([, definition, length]) => [definition, length]),
      [
        ["static", 26],
        ["static", 10],
        ["dynamic", 26],
      ],
    );

    // A non-finite float is the same string the managed projection writes, so the native path keeps the parse.
    plan.plan.ops = [{ name: "x", o: 0, k: "n", t: "f32", le: true }];
    plan.plan.size = 4;
    globalThis.CStructSharpWasm.getStaticPlan = () => planEnvelope(structuredClone(plan));
    assert.equal((await parse("float-inf", new Uint8Array([0, 0, 0x80, 0x7f]))).data.x, "Infinity");
    assert.equal((await parse("float-neg-inf", new Uint8Array([0, 0, 0x80, 0xff]))).data.x, "-Infinity");
    assert.equal((await parse("float-nan", new Uint8Array([0, 0, 0xc0, 0x7f]))).data.x, "NaN");
    // A float32 reads as the double JSON.parse would produce from the projection's shortest decimal.
    assert.equal((await parse("float-ok", new Uint8Array([0xcd, 0xcc, 0xcc, 0x3d]))).data.x, 0.1);
    // A decimal tie at the shortest precision rounds to the even last digit, as the managed formatter does.
    assert.equal((await parse("float-tie", new Uint8Array([0x00, 0x40, 0x00, 0x40]))).data.x, 2.0039062);
    assert.equal((await parse("float-tie-2", new Uint8Array([0x5a, 0x34, 0xb4, 0x49]))).data.x, 1476235.2);
    plan.plan.ops = [{ name: "x", o: 0, k: "n", t: "f64", le: true }];
    plan.plan.size = 8;
    assert.equal((await parse("double-nan", new Uint8Array([0, 0, 0, 0, 0, 0, 0xf8, 0x7f]))).data.x, "NaN");
  } finally {
    globalThis.CStructSharpWasm = previous;
  }
});

test("the native parse states the managed contract version and leaves failed plan requests to WASM", async () => {
  const previous = globalThis.CStructSharpWasm;
  const calls = [];
  const plan = { root: "root", plan: { size: 1, ops: [{ name: "kind", o: 0, k: "n", t: "u8", le: true }] } };
  globalThis.CStructSharpWasm = {
    ready: true,
    /** Records the request; "invalid" fails, any other definition has a plan with an unknown version. */
    getStaticPlan: (definition) => {
      calls.push(["getStaticPlan", definition]);
      if (definition === "invalid") {
        return {
          contractVersion: 10,
          operation: "staticPlan",
          success: false,
          root: null,
          data: null,
          debug: [],
          error: { code: "invalid-input", message: "The layout definition is empty.", path: null, offset: null, member: null, memberType: null, line: null, column: null },
        };
      }
      // A version the package does not know: the native envelope must repeat it rather than state its own.
      return planEnvelope(structuredClone(plan), 42);
    },
    /** Records the WASM parse and reports the invalid input. */
    parseBytes: (definition) => {
      calls.push(["parseBytes", definition]);
      return JSON.stringify({ contractVersion: 10, operation: "parse", success: false, root: null, data: null, debug: [], error: { code: "invalid-input" } });
    },
  };
  try {
    const { parse } = await import("./cstructsharp-wasm.js");
    const native = await parse("versioned", new Uint8Array([7]));
    assert.equal(native.contractVersion, 42);
    assert.deepEqual(native.data, { kind: 7 });

    // A failure envelope is not cached: each parse asks again and the WASM parse reports the error.
    assert.equal((await parse("invalid", new Uint8Array([7]))).error.code, "invalid-input");
    assert.equal((await parse("invalid", new Uint8Array([7]))).error.code, "invalid-input");
    assert.deepEqual(calls, [
      ["getStaticPlan", "versioned"],
      ["getStaticPlan", "invalid"],
      ["parseBytes", "invalid"],
      ["getStaticPlan", "invalid"],
      ["parseBytes", "invalid"],
    ]);
  } finally {
    globalThis.CStructSharpWasm = previous;
  }
});
