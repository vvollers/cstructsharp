/**
 * The browser contract of the npm package, exercised through its public API (`parse`, `parseWithDebug`,
 * `serialize`, `update`) on the real WebAssembly runtime: value shapes, byte results, exact 64-bit values, option
 * handling, and one release-safe error shape per failure category. Byte results are compared as hex text because a
 * Uint8Array cannot leave the page unchanged.
 */
import { expect, test } from "@playwright/test";

test.beforeEach(async ({ page }) => {
  await page.goto("/tools/binary/bridge.html");
  await expect(page.locator("body")).toHaveAttribute("data-ready", "true", { timeout: 60_000 });
});

test("parse, serialize and update return the contract's envelopes and value shapes", async ({ page }) => {
  const results = await page.evaluate(async () => {
    const { api, bytes, plain } = window.bridge;
    const definition = "struct root { byte value; };";
    const union = "union choice { uint8 small; uint16 large; };";
    const selectedSmall = { kind: "union", union: "choice", rawStorage: null, members: { small: 165 }, selectedMember: "small" };
    const narrow = { root: "choice", aligned: false, pointerSize: 1 };
    /** Awaits an operation and makes its envelope cloneable out of the page. */
    const run = async (promise) => plain(await promise);

    return {
      parse: await run(api.parseWithDebug(definition, bytes("2a"))),
      scopedInlineParse: await run(
        api.parseWithDebug(
          "struct first { struct { byte small; } value; }; struct second { struct { uint16 large; } value; };",
          bytes("2a"),
        ),
      ),
      pointerUnionParse: await run(
        api.parseWithDebug(`${union} struct root { choice *target; };`, bytes("01 34 12"), { root: "root", pointerSize: 1 }),
      ),
      unionParse: await run(api.parseWithDebug(union, bytes("34 12"), { root: "choice", pointerSize: 1 })),
      selectedUnionSerialize: await run(api.serialize(union, selectedSmall, narrow)),
      rawUnionSerialize: await run(
        api.serialize(union, { kind: "union", union: "choice", rawStorage: "NBI=", members: {}, selectedMember: null }, narrow),
      ),
      memberOnlyUnionSerialize: await run(api.serialize(union, { small: 165 }, narrow)),
      selectedUnionUpdate: await run(api.update(union, bytes("34 12"), "choice", selectedSmall, narrow)),
      serialize: await run(api.serialize(definition, { value: 42 }, { root: "root" })),
      selectedArraySerialize: await run(
        api.serialize("struct root { uint16 items[3]; };", 4660, { root: "root.items[1]", pointerSize: 1 }),
      ),
      update: await run(api.update(definition, bytes("00"), "root.value", 42)),
      alignedPointerUpdate: await run(
        api.update("struct root { uint16 *ptr; uint8 tail; };", bytes("03 ee a5 34 12 7e"), "root.ptr.value", 48879, {
          aligned: true,
          pointerSize: 1,
        }),
      ),
      relativeNullPointer: await run(
        api.update("struct root { uint8 *ptr; };", bytes("a5"), "root.ptr.address", 0, {
          pointerSize: 1,
          addressingMode: "Relative",
          origin: "10",
        }),
      ),
      nullPointerSerialize: await run(
        api.serialize("struct root { uint8 *ptr; byte tail; };", { ptr: null, tail: 165 }, { root: "root", pointerSize: 1 }),
      ),
      nullRootPointerSerialize: await run(api.serialize("typedef uint8 *link;", null, { root: "link", pointerSize: 2 })),
      nullPrimitiveSerialize: await run(api.serialize(definition, { value: null }, { root: "root", pointerSize: 1 })),
      nullRootStructSerialize: await run(api.serialize(definition, null, { root: "root", pointerSize: 1 })),
      bigEndianWideParse: await run(api.parseWithDebug("struct root { wchar> value[]; };", bytes("00 41 00 00"))),
      bigEndianWideSerialize: await run(api.serialize("struct root { wchar> value[]; };", { value: "A" }, { root: "root" })),
      bigEndianWideUpdate: await run(api.update("struct root { wchar> value[]; };", bytes("00 41 00 00"), "root.value", "B")),
    };
  });

  /** The envelope of a successful operation with the given data. */
  const success = (operation, data) => ({ contractVersion: 8, operation, success: true, data, error: null });
  /** The envelope of an operation that failed to write. */
  const writeFailure = (operation) => ({ contractVersion: 8, operation, success: false, data: null, error: { code: "write-failed" } });

  expect(results.parse).toMatchObject(success("parse", { value: 42 }));
  expect(results.scopedInlineParse).toMatchObject(success("parse", { value: { small: 42 } }));
  const unionValue = { kind: "union", union: "choice", rawStorage: "NBI=", members: { small: 52, large: 4660 }, selectedMember: null };
  expect(results.pointerUnionParse.data).toEqual({
    target: { kind: "pointer", address: 1, depth: 1, dereferenced: true, value: unionValue },
  });
  expect(results.unionParse).toMatchObject(success("parse", unionValue));
  expect(results.selectedUnionSerialize).toMatchObject(success("serialize", "a5 00"));
  expect(results.rawUnionSerialize).toMatchObject(success("serialize", "34 12"));
  // A plain member object does not say which member to write, so a union needs the union shape.
  expect(results.memberOnlyUnionSerialize).toMatchObject(writeFailure("serialize"));
  expect(results.selectedUnionUpdate).toMatchObject(success("update", "a5 00"));
  expect(results.serialize).toMatchObject(success("serialize", "2a"));
  expect(results.selectedArraySerialize).toMatchObject(success("serialize", "34 12"));
  expect(results.update).toMatchObject(success("update", "2a"));
  expect(results.alignedPointerUpdate).toMatchObject(success("update", "03 ee a5 ef be 7e"));
  expect(results.relativeNullPointer).toMatchObject(success("update", "00"));
  expect(results.nullPointerSerialize).toMatchObject(success("serialize", "00 a5"));
  expect(results.nullRootPointerSerialize).toMatchObject(success("serialize", "00 00"));
  expect(results.nullPrimitiveSerialize).toMatchObject(writeFailure("serialize"));
  expect(results.nullRootStructSerialize).toMatchObject(writeFailure("serialize"));
  expect(results.bigEndianWideParse).toMatchObject(success("parse", { value: "A" }));
  expect(results.bigEndianWideSerialize).toMatchObject(success("serialize", "00 41 00 00"));
  expect(results.bigEndianWideUpdate).toMatchObject(success("update", "00 42 00 00"));
});

test("64-bit values stay exact and an invalid option is a stable error", async ({ page }) => {
  const results = await page.evaluate(async () => {
    const { api, bytes, plain } = window.bridge;
    const definition = "struct root { uint64 value; };";
    return {
      parse: plain(await api.parseWithDebug(definition, bytes("ff ff ff ff ff ff ff ff"))),
      serialize: plain(await api.serialize(definition, { value: 18446744073709551615n }, { root: "root" })),
      invalidMode: plain(
        await api.update(definition, bytes("00 00 00 00 00 00 00 00"), "root.value", 1, { addressingMode: "not-a-mode" }),
      ),
    };
  });

  expect(results.parse.data).toEqual({ value: "18446744073709551615" });
  expect(results.serialize).toMatchObject({ success: true, data: "ff ff ff ff ff ff ff ff" });
  expect(results.invalidMode).toMatchObject({
    contractVersion: 8,
    operation: "update",
    success: false,
    data: null,
    error: { code: "invalid-input" },
  });
});

test("options select byte order and enforce the caller's limits", async ({ page }) => {
  const results = await page.evaluate(async () => {
    const { api, bytes } = window.bridge;
    const definition = "struct root { uint16 value; };";
    return {
      bigEndian: await api.parseWithDebug(definition, bytes("12 34"), { root: "root", littleEndian: false, pointerSize: 4 }),
      readBudget: await api.parseWithDebug(definition, bytes("12 34"), { maxTotalBytesRead: 1 }),
      optionCap: await api.parseWithDebug("struct root { byte value; };", bytes("2a"), { maxArrayElements: 0 }),
      definitionBudget: await api.parseWithDebug("struct root { byte value; };", bytes("2a"), { maxDefinitionLength: 8 }),
    };
  });

  expect(results.bigEndian).toMatchObject({ contractVersion: 8, operation: "parse", success: true, data: { value: 0x1234 } });
  expect(results.readBudget).toMatchObject({ success: false, error: { code: "read-budget" } });
  expect(results.optionCap).toMatchObject({ success: false, error: { code: "invalid-input" } });
  expect(results.definitionBudget).toMatchObject({ success: false, error: { code: "invalid-layout" } });
});

test("every signed and unsigned JavaScript precision boundary round-trips exactly", async ({ page }) => {
  const results = await page.evaluate(async () => {
    const { api, hex, plain } = window.bridge;
    /** The little-endian bytes of a 64-bit two's-complement value. */
    const littleEndian = (value) => {
      let bits = BigInt.asUintN(64, value);
      const bytes = new Uint8Array(8);
      for (let index = 0; index < bytes.length; index++) {
        bytes[index] = Number(bits & 0xffn);
        bits >>= 8n;
      }
      return bytes;
    };
    const cases = [
      { type: "uint64", value: 9_007_199_254_740_991n },
      { type: "uint64", value: 9_007_199_254_740_992n },
      { type: "int64", value: 9_223_372_036_854_775_807n },
      { type: "uint64", value: 18_446_744_073_709_551_615n },
      { type: "int64", value: -9_223_372_036_854_775_808n },
      { type: "int64", value: -9_007_199_254_740_992n },
    ];
    const out = [];
    for (const { type, value } of cases) {
      const definition = `struct root { ${type} value; };`;
      const bytes = littleEndian(value);
      out.push({
        expected: value.toString(),
        bytes: hex(bytes),
        parsed: await api.parseWithDebug(definition, bytes),
        serialized: plain(await api.serialize(definition, { value: value.toString() }, { root: "root" })),
      });
    }
    return out;
  });

  for (const result of results) {
    expect(String(result.parsed.data.value)).toBe(result.expected);
    expect(result.parsed).toMatchObject({ contractVersion: 8, operation: "parse", success: true, error: null });
    expect(result.serialized).toMatchObject({ contractVersion: 8, operation: "serialize", success: true, data: result.bytes });
  }
});

test("full-width enum values stay exact through parse, serialize and update", async ({ page }) => {
  const results = await page.evaluate(async () => {
    const { api, bytes, plain } = window.bridge;
    const unknownDefinition = "enum state : uint64 { Known = 1 }; struct root { state value; };";
    const knownDefinition = "enum state : uint64 { Maximum = 18446744073709551615 }; struct root { state value; };";
    const allOnes = bytes("ff ff ff ff ff ff ff ff");
    return {
      unknown: await api.parseWithDebug(unknownDefinition, allOnes),
      known: await api.parseWithDebug(knownDefinition, allOnes),
      decimalString: plain(await api.serialize(unknownDefinition, { value: "18446744073709551615" }, { root: "root" })),
      safeNumber: plain(await api.serialize(unknownDefinition, { value: 42 }, { root: "root" })),
      objectShape: plain(
        await api.serialize(
          knownDefinition,
          { value: { kind: "enum", enum: "state", name: "Maximum", value: "18446744073709551615" } },
          { root: "root" },
        ),
      ),
      update: plain(await api.update(unknownDefinition, bytes("00 00 00 00 00 00 00 00"), "root.value", "18446744073709551615")),
      fractional: plain(await api.serialize(unknownDefinition, { value: 1.5 }, { root: "root" })),
    };
  });

  expect(results.unknown.data).toEqual({ value: { kind: "enum", enum: "state", name: null, value: "18446744073709551615" } });
  expect(results.unknown.debug).toEqual([expect.objectContaining({ value: "18446744073709551615" })]);
  expect(results.known.data).toEqual({ value: { kind: "enum", enum: "state", name: "Maximum", value: "18446744073709551615" } });
  expect(results.decimalString).toMatchObject({ success: true, data: "ff ff ff ff ff ff ff ff" });
  expect(results.safeNumber).toMatchObject({ success: true, data: "2a 00 00 00 00 00 00 00" });
  expect(results.objectShape).toMatchObject({ success: true, data: "ff ff ff ff ff ff ff ff" });
  expect(results.update).toMatchObject({ operation: "update", success: true, data: "ff ff ff ff ff ff ff ff" });
  expect(results.fractional).toMatchObject({ success: false, data: null, error: { code: "write-failed" } });
});

test("each failure category uses the same release-safe error shape", async ({ page }) => {
  const failures = await page.evaluate(async () => {
    const { api, bytes, plain } = window.bridge;
    /** Awaits an operation and makes its envelope cloneable out of the page. */
    const run = async (promise) => plain(await promise);
    const pointer = "struct root { uint8 *ptr; };";

    // The public API always sends valid JSON, so malformed JSON can only reach the managed export directly.
    let invalidJson;
    try {
      window.CStructSharpWasm.serialize("struct root { byte value; };", "{", { root: "root" });
    } catch (cause) {
      invalidJson = { contractVersion: 8, success: false, data: null, error: JSON.parse(cause.message) };
    }

    return {
      invalidLayout: await run(api.parseWithDebug("struct root {", bytes("00"))),
      duplicateMember: await run(api.parseWithDebug("struct root { byte value; uint16 value; };", bytes("00 00 00"))),
      nonIntegralBitfield: await run(api.parseWithDebug("struct root { ascii_string_zero flags:1; };", bytes("00"))),
      expressionSafety: await run(api.parseWithDebug(`struct root { byte values[${"~".repeat(300)}1]; };`, bytes("00"))),
      anonymousTypeLeak: await run(
        api.parseWithDebug("struct first { struct { byte item; } local; }; struct second { local leaked; };", bytes("00")),
      ),
      invalidPath: await run(api.update("struct root { byte value; };", bytes("00"), "root.missing", 1)),
      readFailed: await run(api.parseWithDebug("struct root { uint64 value; };", bytes("00"))),
      readBudget: await run(api.parseWithDebug("struct root { byte values[1000001]; };", bytes("00"))),
      writeFailed: await run(api.serialize("struct root { byte values[2]; };", { values: [1] }, { root: "root" })),
      bitfieldOverflow: await run(api.update("struct root { uint8 low:4; uint8 high:4; };", bytes("a5"), "root.high", 16)),
      relativePointerOverflow: await run(
        api.update(pointer, bytes("a5 a5 a5 a5 a5 a5 a5 a5"), "root.ptr.address", "-9223372036854775808", {
          addressingMode: "Relative",
          origin: "1",
        }),
      ),
      negativeRelativePointer: await run(
        api.update(pointer, bytes("a5"), "root.ptr.address", -1, { pointerSize: 1, addressingMode: "Relative", origin: "-2" }),
      ),
      invalidJson,
      malformedUtf8: await run(api.parseWithDebug("struct root { utf8_string_zero value; };", bytes("c3 28 00"))),
      lossyAscii: await run(api.serialize("struct root { ascii_string_zero value; };", { value: "é" }, { root: "root" })),
    };
  });

  const expectedCodes = {
    invalidLayout: "invalid-layout",
    duplicateMember: "invalid-layout",
    nonIntegralBitfield: "invalid-layout",
    expressionSafety: "invalid-layout",
    anonymousTypeLeak: "invalid-layout",
    invalidPath: "invalid-path",
    readFailed: "read-failed",
    readBudget: "read-budget",
    writeFailed: "write-failed",
    bitfieldOverflow: "write-failed",
    relativePointerOverflow: "write-failed",
    negativeRelativePointer: "write-failed",
    invalidJson: "invalid-json",
    malformedUtf8: "read-failed",
    lossyAscii: "write-failed",
  };

  for (const [name, failure] of Object.entries(failures)) {
    expect(failure, name).toMatchObject({ contractVersion: 8, success: false, data: null, error: { code: expectedCodes[name] } });
    expect(Object.keys(failure.error).sort(), name).toEqual([
      "code",
      "column",
      "line",
      "member",
      "memberType",
      "message",
      "offset",
      "path",
    ]);
    expect(failure.error.message, name).toBeTruthy();
    expect(failure.error.message, name).not.toContain("src/CStructSharp");
    expect(failure.error.message, name).not.toContain("System.");
    if (failure.error.offset !== null) {
      expect(Number.isSafeInteger(failure.error.offset), name).toBe(true);
      expect(failure.error.offset, name).toBeGreaterThanOrEqual(0);
    }
    if (failure.error.path !== null) {
      expect(failure.error.path, name).toMatch(/^[A-Za-z_][A-Za-z0-9_]*(?:\[\d+\])?(?:\.[A-Za-z_][A-Za-z0-9_]*(?:\[\d+\])?)*$/);
    }
  }

  expect(failures.invalidPath.error).toMatchObject({ offset: 1, path: "root.missing" });
  expect(failures.readFailed.error).toMatchObject({ offset: 1, path: "root" });
});
