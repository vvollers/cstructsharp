import { expect, test } from "@playwright/test";
import type { InteropResult as Envelope, RawWasmAdapter } from "../../src/wasm/cstruct-contract";

interface PositionalTestAdapter extends Omit<
  RawWasmAdapter,
  "serializeToBase64" | "updateStreamToBase64"
> {
  serializeToBase64(
    definition: string,
    dataJson: string,
    rootTypeName: string | null,
    aligned: boolean,
    pointerSize: number,
  ): string;
  updateStreamToBase64(
    definition: string,
    binaryBase64: string,
    path: string,
    valueJson: string,
    aligned: boolean,
    pointerSize: number,
    addressingMode: "Absolute" | "Relative",
    origin: string,
    allowPointerDereference: boolean,
  ): string;
}

declare global {
  interface Window {
    CStructSharpWasmTest?: PositionalTestAdapter;
  }
}

test.beforeEach(async ({ page }) => {
  await page.goto("/");
  await expect(page.locator(".status-badge")).toContainText("Ready", {
    timeout: 60_000,
  });
  await page.evaluate(() => {
    const raw = window.CStructSharpWasm as unknown as RawWasmAdapter;
    window.CStructSharpWasmTest = {
      ...raw,
      serializeToBase64(definition, dataJson, rootTypeName, aligned, pointerSize) {
        return raw.serializeToBase64(definition, dataJson, {
          rootTypeName,
          aligned,
          pointerSize,
        });
      },
      updateStreamToBase64(
        definition,
        binaryBase64,
        path,
        valueJson,
        aligned,
        pointerSize,
        addressingMode,
        origin,
        allowPointerDereference,
      ) {
        return raw.updateStreamToBase64(definition, binaryBase64, path, valueJson, {
          aligned,
          pointerSize,
          addressingMode,
          origin,
          allowPointerDereference,
        });
      },
    };
  });
});

test("real managed exports parse, serialize, and update through the browser", async ({ page }) => {
  const results = await page.evaluate(() => {
    const wasm = window.CStructSharpWasmTest as PositionalTestAdapter;
    const definition = "struct root { byte value; };";

    return {
      parse: JSON.parse(wasm.parseWithDebug(definition, "Kg==")) as Envelope,
      scopedInlineParse: JSON.parse(
        wasm.parseWithDebug(
          "struct first { struct { byte small; } value; }; " +
            "struct second { struct { uint16 large; } value; };",
          "Kg==",
        ),
      ) as Envelope,
      pointerUnionParse: JSON.parse(
        wasm.parseWithDebug(
          "union choice { uint8 small; uint16 large; }; struct root { choice *target; };",
          "ATQS",
          { rootTypeName: "root", pointerSize: 1 },
        ),
      ) as Envelope,
      unionParse: JSON.parse(
        wasm.parseWithDebug("union choice { uint8 small; uint16 large; };", "NBI=", {
          rootTypeName: "choice",
          pointerSize: 1,
        }),
      ) as Envelope,
      selectedUnionSerialize: JSON.parse(
        wasm.serializeToBase64(
          "union choice { uint8 small; uint16 large; };",
          '{"$kind":"union","Union":"choice","RawStorage":null,"Members":{"small":165},"SelectedMember":"small"}',
          "choice",
          false,
          1,
        ),
      ) as Envelope,
      rawUnionSerialize: JSON.parse(
        wasm.serializeToBase64(
          "union choice { uint8 small; uint16 large; };",
          '{"$kind":"union","Union":"choice","RawStorage":"NBI=","Members":{},"SelectedMember":null}',
          "choice",
          false,
          1,
        ),
      ) as Envelope,
      legacyUnionSerialize: JSON.parse(
        wasm.serializeToBase64(
          "union choice { uint8 small; uint16 large; };",
          '{"small":165}',
          "choice",
          false,
          1,
        ),
      ) as Envelope,
      selectedUnionUpdate: JSON.parse(
        wasm.updateStreamToBase64(
          "union choice { uint8 small; uint16 large; };",
          "NBI=",
          "choice",
          '{"$kind":"union","Union":"choice","RawStorage":null,"Members":{"small":165},"SelectedMember":"small"}',
          false,
          1,
          "Absolute",
          "0",
          true,
        ),
      ) as Envelope,
      serialize: JSON.parse(
        wasm.serializeToBase64(definition, '{"value":42}', "root", false, 8),
      ) as Envelope,
      selectedArraySerialize: JSON.parse(
        wasm.serializeToBase64(
          "struct root { uint16 items[3]; };",
          "4660",
          "root.items[1]",
          false,
          1,
        ),
      ) as Envelope,
      update: JSON.parse(
        wasm.updateStreamToBase64(
          definition,
          "AA==",
          "root.value",
          "42",
          false,
          8,
          "Absolute",
          "0",
          true,
        ),
      ) as Envelope,
      alignedPointerUpdate: JSON.parse(
        wasm.updateStreamToBase64(
          "struct root { uint16 *ptr; uint8 tail; };",
          btoa(String.fromCharCode(0x03, 0xee, 0xa5, 0x34, 0x12, 0x7e)),
          "root.ptr.value",
          "48879",
          true,
          1,
          "Absolute",
          "0",
          true,
        ),
      ) as Envelope,
      relativeNullPointer: JSON.parse(
        wasm.updateStreamToBase64(
          "struct root { uint8 *ptr; };",
          "pQ==",
          "root.ptr.address",
          "0",
          false,
          1,
          "Relative",
          "10",
          true,
        ),
      ) as Envelope,
      nullPointerSerialize: JSON.parse(
        wasm.serializeToBase64(
          "struct root { uint8 *ptr; byte tail; };",
          '{"ptr":null,"tail":165}',
          "root",
          false,
          1,
        ),
      ) as Envelope,
      nullRootPointerSerialize: JSON.parse(
        wasm.serializeToBase64("typedef uint8 *link;", "null", "link", false, 2),
      ) as Envelope,
      nullPrimitiveSerialize: JSON.parse(
        wasm.serializeToBase64("struct root { byte value; };", '{"value":null}', "root", false, 1),
      ) as Envelope,
      nullRootStructSerialize: JSON.parse(
        wasm.serializeToBase64("struct root { byte value; };", "null", "root", false, 1),
      ) as Envelope,
      explicitBigEndianWideParse: JSON.parse(
        wasm.parseWithDebug("struct root { wchar> value[]; };", "AEEAAA=="),
      ) as Envelope,
      explicitBigEndianWideSerialize: JSON.parse(
        wasm.serializeToBase64(
          "struct root { wchar> value[]; };",
          '{"value":"A"}',
          "root",
          false,
          8,
        ),
      ) as Envelope,
      explicitBigEndianWideUpdate: JSON.parse(
        wasm.updateStreamToBase64(
          "struct root { wchar> value[]; };",
          "AEEAAA==",
          "root.value",
          '"B"',
          false,
          8,
          "Absolute",
          "0",
          true,
        ),
      ) as Envelope,
    };
  });

  expect(results.parse).toMatchObject({
    ContractVersion: 4,
    Operation: "parse",
    Success: true,
    Error: null,
  });
  expect(JSON.parse(results.parse.Data ?? "{}")).toEqual({
    root: { value: 42 },
  });
  expect(results.scopedInlineParse).toMatchObject({
    ContractVersion: 4,
    Operation: "parse",
    Success: true,
    Error: null,
  });
  expect(JSON.parse(results.scopedInlineParse.Data ?? "{}")).toEqual({
    first: { value: { small: 42 } },
  });
  expect(results.pointerUnionParse).toMatchObject({
    ContractVersion: 4,
    Operation: "parse",
    Success: true,
    Error: null,
  });
  expect(JSON.parse(results.pointerUnionParse.Data ?? "{}")).toEqual({
    root: {
      target: {
        Address: 1,
        Depth: 1,
        IsDereferenced: true,
        Value: {
          $kind: "union",
          Union: "choice",
          RawStorage: "NBI=",
          Members: { small: 52, large: 4660 },
          SelectedMember: null,
        },
      },
    },
  });
  expect(results.unionParse).toMatchObject({
    ContractVersion: 4,
    Operation: "parse",
    Success: true,
    Error: null,
  });
  expect(JSON.parse(results.unionParse.Data ?? "{}")).toEqual({
    $kind: "union",
    Union: "choice",
    RawStorage: "NBI=",
    Members: { small: 52, large: 4660 },
    SelectedMember: null,
  });
  expect(results.selectedUnionSerialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: true,
    Data: "pQA=",
    Error: null,
  });
  expect(results.rawUnionSerialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: true,
    Data: "NBI=",
    Error: null,
  });
  expect(results.legacyUnionSerialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: false,
    Data: null,
    Error: {
      Code: "write-failed",
    },
  });
  expect(results.selectedUnionUpdate).toMatchObject({
    ContractVersion: 4,
    Operation: "update",
    Success: true,
    Data: "pQA=",
    Error: null,
  });
  expect(results.serialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: true,
    Data: "Kg==",
    Error: null,
  });
  expect(results.selectedArraySerialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: true,
    Data: "NBI=",
    Error: null,
  });
  expect(results.update).toMatchObject({
    ContractVersion: 4,
    Operation: "update",
    Success: true,
    Data: "Kg==",
    Error: null,
  });
  expect(results.alignedPointerUpdate).toMatchObject({
    ContractVersion: 4,
    Operation: "update",
    Success: true,
    Data: "A+6l775+",
    Error: null,
  });
  expect(results.relativeNullPointer).toMatchObject({
    ContractVersion: 4,
    Operation: "update",
    Success: true,
    Data: "AA==",
    Error: null,
  });
  expect(results.nullPointerSerialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: true,
    Data: "AKU=",
    Error: null,
  });
  expect(results.nullRootPointerSerialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: true,
    Data: "AAA=",
    Error: null,
  });
  expect(results.nullPrimitiveSerialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: false,
    Data: null,
    Error: {
      Code: "write-failed",
    },
  });
  expect(results.nullRootStructSerialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: false,
    Data: null,
    Error: {
      Code: "write-failed",
    },
  });
  expect(results.explicitBigEndianWideParse).toMatchObject({
    ContractVersion: 4,
    Operation: "parse",
    Success: true,
    Error: null,
  });
  expect(JSON.parse(results.explicitBigEndianWideParse.Data ?? "{}")).toEqual({
    root: { value: "A" },
  });
  expect(results.explicitBigEndianWideSerialize).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: true,
    Data: "AEEAAA==",
    Error: null,
  });
  expect(results.explicitBigEndianWideUpdate).toMatchObject({
    ContractVersion: 4,
    Operation: "update",
    Success: true,
    Data: "AEIAAA==",
    Error: null,
  });
});

test("64-bit values remain exact and invalid options return stable errors", async ({ page }) => {
  const results = await page.evaluate(() => {
    const wasm = window.CStructSharpWasmTest as PositionalTestAdapter;
    const definition = "struct root { uint64 value; };";

    return {
      parse: JSON.parse(wasm.parseWithDebug(definition, "//////////8=")) as Envelope,
      serialize: JSON.parse(
        wasm.serializeToBase64(definition, '{"value":"18446744073709551615"}', "root", false, 8),
      ) as Envelope,
      invalidMode: JSON.parse(
        wasm.updateStreamToBase64(
          definition,
          "AAAAAAAAAAA=",
          "root.value",
          "1",
          false,
          8,
          "not-a-mode",
          "0",
          true,
        ),
      ) as Envelope,
    };
  });

  expect(JSON.parse(results.parse.Data ?? "{}")).toEqual({
    root: { value: "18446744073709551615" },
  });
  expect(results.serialize).toMatchObject({
    Success: true,
    Data: "//////////8=",
  });
  expect(results.invalidMode).toMatchObject({
    ContractVersion: 4,
    Operation: "update",
    Success: false,
    Data: null,
    Error: {
      Code: "invalid-input",
    },
  });
});

test("v4 options control endian behavior and enforce caller-selected safety budgets", async ({
  page,
}) => {
  const results = await page.evaluate(() => {
    const wasm = window.CStructSharpWasm as unknown as RawWasmAdapter;
    const parse = (value: string) => JSON.parse(value) as Envelope;
    const definition = "struct root { uint16 value; };";

    return {
      bigEndian: parse(
        wasm.parseWithDebug(definition, "EjQ=", {
          rootTypeName: "root",
          littleEndian: false,
          pointerSize: 4,
        }),
      ),
      readBudget: parse(
        wasm.parseWithDebug(definition, "EjQ=", {
          maxTotalBytesRead: 1,
        }),
      ),
      optionCap: parse(
        wasm.parseWithDebug("struct root { byte value; };", "Kg==", {
          maxArrayElements: 1_000_001,
        }),
      ),
      definitionBudget: parse(
        wasm.parseWithDebug("struct root { byte value; };", "Kg==", {
          maxDefinitionLength: 8,
        }),
      ),
    };
  });

  expect(results.bigEndian).toMatchObject({
    ContractVersion: 4,
    Operation: "parse",
    Success: true,
    Error: null,
  });
  expect(JSON.parse(results.bigEndian.Data ?? "{}")).toEqual({
    root: { value: 0x1234 },
  });
  expect(results.readBudget).toMatchObject({
    ContractVersion: 4,
    Success: false,
    Error: { Code: "read-budget" },
  });
  expect(results.optionCap).toMatchObject({
    ContractVersion: 4,
    Success: false,
    Error: { Code: "invalid-input" },
  });
  expect(results.definitionBudget).toMatchObject({
    ContractVersion: 4,
    Success: false,
    Error: { Code: "invalid-layout" },
  });
});

test("all signed and unsigned JavaScript precision boundaries round-trip exactly", async ({
  page,
}) => {
  const results = await page.evaluate(() => {
    const wasm = window.CStructSharpWasmTest as PositionalTestAdapter;

    const toLittleEndianBase64 = (value: bigint): string => {
      let bits = BigInt.asUintN(64, value);
      const bytes = new Uint8Array(8);
      for (let index = 0; index < bytes.length; index++) {
        bytes[index] = Number(bits & 0xffn);
        bits >>= 8n;
      }

      return btoa(String.fromCharCode(...bytes));
    };

    const cases = [
      { type: "uint64", value: 9_007_199_254_740_991n },
      { type: "uint64", value: 9_007_199_254_740_992n },
      { type: "int64", value: 9_223_372_036_854_775_807n },
      { type: "uint64", value: 18_446_744_073_709_551_615n },
      { type: "int64", value: -9_223_372_036_854_775_808n },
      { type: "int64", value: -9_007_199_254_740_992n },
    ];

    return cases.map(({ type, value }) => {
      const definition = `struct root { ${type} value; };`;
      const bytes = toLittleEndianBase64(value);
      const parsed = JSON.parse(wasm.parseWithDebug(definition, bytes)) as Envelope;
      const serialized = JSON.parse(
        wasm.serializeToBase64(
          definition,
          JSON.stringify({ value: value.toString() }),
          "root",
          false,
          8,
        ),
      ) as Envelope;

      return {
        expected: value.toString(),
        bytes,
        parsed,
        serialized,
      };
    });
  });

  for (const result of results) {
    const parsedValue = JSON.parse(result.parsed.Data ?? "{}").root.value as number | string;
    expect(String(parsedValue)).toBe(result.expected);
    expect(result.parsed).toMatchObject({
      ContractVersion: 4,
      Operation: "parse",
      Success: true,
      Error: null,
    });
    expect(result.serialized).toMatchObject({
      ContractVersion: 4,
      Operation: "serialize",
      Success: true,
      Data: result.bytes,
      Error: null,
    });
  }
});

test("full-width enum values remain exact across browser parse, serialize, and update", async ({
  page,
}) => {
  const results = await page.evaluate(() => {
    const wasm = window.CStructSharpWasmTest as PositionalTestAdapter;
    const unknownDefinition = "enum state : uint64 { Known = 1 }; struct root { state value; };";
    const knownDefinition =
      "enum state : uint64 { Maximum = 18446744073709551615 }; " + "struct root { state value; };";

    return {
      unknown: JSON.parse(wasm.parseWithDebug(unknownDefinition, "//////////8=")) as Envelope,
      known: JSON.parse(wasm.parseWithDebug(knownDefinition, "//////////8=")) as Envelope,
      decimalString: JSON.parse(
        wasm.serializeToBase64(
          unknownDefinition,
          '{"value":"18446744073709551615"}',
          "root",
          false,
          8,
        ),
      ) as Envelope,
      safeNumber: JSON.parse(
        wasm.serializeToBase64(unknownDefinition, '{"value":42}', "root", false, 8),
      ) as Envelope,
      objectShape: JSON.parse(
        wasm.serializeToBase64(
          knownDefinition,
          '{"value":{"Enum":"state","Name":"Maximum","Value":"18446744073709551615"}}',
          "root",
          false,
          8,
        ),
      ) as Envelope,
      update: JSON.parse(
        wasm.updateStreamToBase64(
          unknownDefinition,
          "AAAAAAAAAAA=",
          "root.value",
          '"18446744073709551615"',
          false,
          8,
          "Absolute",
          "0",
          true,
        ),
      ) as Envelope,
      fractional: JSON.parse(
        wasm.serializeToBase64(unknownDefinition, '{"value":1.5}', "root", false, 8),
      ) as Envelope,
    };
  });

  expect(JSON.parse(results.unknown.Data ?? "{}")).toEqual({
    root: {
      value: {
        Enum: "state",
        Name: null,
        Value: "18446744073709551615",
      },
    },
  });
  expect(results.unknown.DebugData).toEqual([
    expect.objectContaining({ Value: "18446744073709551615" }),
  ]);
  expect(JSON.parse(results.known.Data ?? "{}")).toEqual({
    root: {
      value: {
        Enum: "state",
        Name: "Maximum",
        Value: "18446744073709551615",
      },
    },
  });
  expect(results.decimalString).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: true,
    Data: "//////////8=",
    Error: null,
  });
  expect(results.safeNumber).toMatchObject({
    Success: true,
    Data: "KgAAAAAAAAA=",
  });
  expect(results.objectShape).toMatchObject({
    Success: true,
    Data: "//////////8=",
  });
  expect(results.update).toMatchObject({
    ContractVersion: 4,
    Operation: "update",
    Success: true,
    Data: "//////////8=",
    Error: null,
  });
  expect(results.fractional).toMatchObject({
    ContractVersion: 4,
    Operation: "serialize",
    Success: false,
    Data: null,
    Error: {
      Code: "write-failed",
    },
  });
});

test("each major failure category uses the same release-safe contract", async ({ page }) => {
  const failures = await page.evaluate(() => {
    const wasm = window.CStructSharpWasmTest as PositionalTestAdapter;
    const parse = (value: string) => JSON.parse(value) as Envelope;

    return {
      invalidLayout: parse(wasm.parseWithDebug("struct root {", "AA==")),
      duplicateMember: parse(
        wasm.parseWithDebug("struct root { byte value; uint16 value; };", "AAAA"),
      ),
      nonIntegralBitfield: parse(
        wasm.parseWithDebug("struct root { ascii_string_zero flags:1; };", "AA=="),
      ),
      expressionSafety: parse(
        wasm.parseWithDebug(`struct root { byte values[${"~".repeat(300)}1]; };`, "AA=="),
      ),
      anonymousTypeLeak: parse(
        wasm.parseWithDebug(
          "struct first { struct { byte item; } local; }; struct second { local leaked; };",
          "AA==",
        ),
      ),
      invalidPath: parse(
        wasm.updateStreamToBase64(
          "struct root { byte value; };",
          "AA==",
          "root.missing",
          "1",
          false,
          8,
          "Absolute",
          "0",
          true,
        ),
      ),
      readFailed: parse(wasm.parseWithDebug("struct root { uint64 value; };", "AA==")),
      readBudget: parse(wasm.parseWithDebug("struct root { byte values[1000001]; };", "AA==")),
      writeFailed: parse(
        wasm.serializeToBase64(
          "struct root { byte values[2]; };",
          '{"values":[1]}',
          "root",
          false,
          8,
        ),
      ),
      bitfieldOverflow: parse(
        wasm.updateStreamToBase64(
          "struct root { uint8 low:4; uint8 high:4; };",
          "pQ==",
          "root.high",
          "16",
          false,
          8,
          "Absolute",
          "0",
          true,
        ),
      ),
      relativePointerOverflow: parse(
        wasm.updateStreamToBase64(
          "struct root { uint8 *ptr; };",
          "paWlpaWlpaU=",
          "root.ptr.address",
          '"-9223372036854775808"',
          false,
          8,
          "Relative",
          "1",
          true,
        ),
      ),
      negativeRelativePointer: parse(
        wasm.updateStreamToBase64(
          "struct root { uint8 *ptr; };",
          "pQ==",
          "root.ptr.address",
          "-1",
          false,
          1,
          "Relative",
          "-2",
          true,
        ),
      ),
      invalidJson: parse(
        wasm.serializeToBase64("struct root { byte value; };", "{", "root", false, 8),
      ),
      malformedUtf8: parse(
        wasm.parseWithDebug(
          "struct root { utf8_string_zero value; };",
          btoa(String.fromCharCode(0xc3, 0x28, 0x00)),
        ),
      ),
      lossyAscii: parse(
        wasm.serializeToBase64(
          "struct root { ascii_string_zero value; };",
          '{"value":"é"}',
          "root",
          false,
          8,
        ),
      ),
    };
  });

  const expectedCodes: Record<string, string> = {
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
    expect(failure).toMatchObject({
      ContractVersion: 4,
      Success: false,
      Data: null,
      Error: {
        Code: expectedCodes[name],
      },
    });
    expect(Object.keys(failure.Error ?? {}).sort()).toEqual(["Code", "Message", "Offset", "Path"]);
    expect(failure.Error?.Message).toBeTruthy();
    expect(failure.Error?.Message).not.toContain("CStructSharp");
    expect(failure.Error?.Message).not.toContain("System.");
    if (failure.Error?.Offset !== null) {
      expect(Number.isSafeInteger(failure.Error?.Offset)).toBe(true);
      expect(failure.Error?.Offset).toBeGreaterThanOrEqual(0);
    }
    if (failure.Error?.Path !== null) {
      expect(failure.Error?.Path).toMatch(
        /^[A-Za-z_][A-Za-z0-9_]*(?:\[\d+\])?(?:\.[A-Za-z_][A-Za-z0-9_]*(?:\[\d+\])?)*$/,
      );
    }
  }

  expect(failures.invalidPath.Error).toMatchObject({
    Offset: 1,
    Path: "root.missing",
  });
  expect(failures.readFailed.Error).toMatchObject({
    Offset: 1,
    Path: "root",
  });
});
