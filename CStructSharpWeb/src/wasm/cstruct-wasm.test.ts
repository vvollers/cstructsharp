import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { InteropResult, RawWasmAdapter } from "./cstruct-contract";

const validParseResult: InteropResult = {
  ContractVersion: 4,
  Operation: "parse",
  Success: true,
  Data: '{"root":{"value":42}}',
  DebugData: [
    {
      CurPos: 0,
      EndPos: 1,
      DebugStackString: "root.value",
      Type: "byte",
      Value: "42",
      Buffer: "42",
    },
  ],
  Error: null,
};

function installAdapter(overrides: Partial<RawWasmAdapter> = {}): RawWasmAdapter {
  const adapter: RawWasmAdapter = {
    exports: {},
    ready: true,
    error: null,
    parseWithDebug: vi.fn(() => JSON.stringify(validParseResult)),
    serializeToBase64: vi.fn(() =>
      JSON.stringify({
        ...validParseResult,
        Operation: "serialize",
        Data: "Kg==",
        DebugData: [],
      }),
    ),
    updateStreamToBase64: vi.fn(() =>
      JSON.stringify({
        ...validParseResult,
        Operation: "update",
        Data: "Kg==",
        DebugData: [],
      }),
    ),
    getVersion: vi.fn(() => "test"),
    ...overrides,
  };
  window.CStructSharpWasm = adapter;
  return adapter;
}

describe("CStructSharp WASM browser boundary", () => {
  beforeEach(() => {
    vi.resetModules();
    (
      window as typeof window & {
        happyDOM: {
          settings: {
            disableJavaScriptFileLoading: boolean;
            handleDisabledFileLoadingAsSuccess: boolean;
          };
        };
      }
    ).happyDOM.settings.disableJavaScriptFileLoading = true;
    (
      window as typeof window & {
        happyDOM: { settings: { handleDisabledFileLoadingAsSuccess: boolean } };
      }
    ).happyDOM.settings.handleDisabledFileLoadingAsSuccess = true;
    document.head
      .querySelectorAll("script[data-cstructsharp-wasm]")
      .forEach((script) => script.remove());
    delete window.CStructSharpWasm;
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("forwards the complete v4 option object and preserves large binary input", async () => {
    const adapter = installAdapter();
    const { parseWithDebug } = await import("./cstruct-wasm");
    const bytes = Uint8Array.from({ length: 1_048_576 }, (_, index) => index & 0xff);
    const options = {
      rootTypeName: "root",
      aligned: true,
      pointerSize: 4,
      littleEndian: false,
      addressingMode: "Relative" as const,
      origin: 9_007_199_254_740_993n,
      dereferencePointers: false,
      maxPointerDepth: 7,
      maxPointerTargetBytes: 4096,
      maxArrayElements: 2048,
      maxStringBytes: 8192,
      maxTotalBytesRead: 16384,
      maxNestingDepth: 32,
      maxDefinitionLength: 4096,
      maxLayoutNestingDepth: 24,
      maxExpressionNestingDepth: 20,
      maxExpressionTokens: 1000,
    };

    expect(parseWithDebug("struct root { byte value; };", bytes, options)).toEqual(
      validParseResult,
    );

    expect(adapter.parseWithDebug).toHaveBeenCalledOnce();
    const [, encoded, forwarded] = vi.mocked(adapter.parseWithDebug).mock.calls[0];
    expect(encoded).toBe(
      btoa(
        String.fromCharCode(...Uint8Array.from({ length: 768 }, (_, index) => index & 0xff)),
      ).repeat(Math.floor(bytes.length / 768)) +
        btoa(String.fromCharCode(...bytes.subarray(bytes.length - (bytes.length % 768)))),
    );
    expect(forwarded).toEqual(options);
  });

  it.each([
    ["missing data", { ...validParseResult, Data: undefined }],
    ["malformed debug entry", { ...validParseResult, DebugData: [{}] }],
    [
      "success with an error",
      {
        ...validParseResult,
        Error: { Code: "bad", Message: "bad", Offset: null, Path: null },
      },
    ],
    [
      "failure without an error",
      { ...validParseResult, Success: false, Data: null, DebugData: [], Error: null },
    ],
  ])("rejects a structurally invalid envelope: %s", async (_name, response) => {
    installAdapter({ parseWithDebug: vi.fn(() => JSON.stringify(response)) });
    const { parseWithDebug } = await import("./cstruct-wasm");

    expect(() => parseWithDebug("struct root { byte value; };", new Uint8Array([42]))).toThrow(
      /invalid parse response envelope/i,
    );
  });

  it("removes a failed bootstrap script before a retry", async () => {
    const { initWasm } = await import("./cstruct-wasm");
    const first = initWasm();
    const firstScript = document.head.querySelector<HTMLScriptElement>(
      "script[data-cstructsharp-wasm]",
    );
    expect(firstScript).not.toBeNull();
    firstScript?.dispatchEvent(new Event("error"));
    await expect(first).rejects.toThrow(/failed to load/i);
    expect(document.head.querySelectorAll("script[data-cstructsharp-wasm]")).toHaveLength(0);

    const second = initWasm();
    expect(document.head.querySelectorAll("script[data-cstructsharp-wasm]")).toHaveLength(1);
    window.CStructSharpWasm = installAdapter();
    window.dispatchEvent(new Event("cstructsharp-wasm-ready"));
    await expect(second).resolves.toBeUndefined();
  });
});
