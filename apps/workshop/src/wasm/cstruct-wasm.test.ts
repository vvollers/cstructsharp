import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { InteropResult, RawWasmAdapter } from "./cstruct-contract";

const validParseResult: InteropResult = {
  ContractVersion: 5,
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
    serialize: vi.fn(() => new Uint8Array([0x2a])),
    updateStream: vi.fn(() => new Uint8Array([0x2a])),
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

  it("forwards the complete v5 option object and the binary input unchanged", async () => {
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
    const [, forwardedBytes, forwardedOptions] = vi.mocked(adapter.parseWithDebug).mock.calls[0];
    // No Base64 encoding step: the managed export receives the exact same Uint8Array instance.
    expect(forwardedBytes).toBe(bytes);
    expect(forwardedOptions).toEqual(options);
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

  it("returns the encoded bytes directly on a successful serialize/update, with no envelope decoding", async () => {
    const adapter = installAdapter();
    const { serialize, updateStream } = await import("./cstruct-wasm");

    const serialized = serialize("struct root { byte value; };", { value: 42 });
    expect(serialized.Success).toBe(true);
    expect(serialized.Data).toEqual(new Uint8Array([0x2a]));
    expect(adapter.serialize).toHaveBeenCalledOnce();

    const updated = updateStream(
      "struct root { byte value; };",
      new Uint8Array([0]),
      "root.value",
      42,
    );
    expect(updated.Success).toBe(true);
    expect(updated.Data).toEqual(new Uint8Array([0x2a]));
    expect(adapter.updateStream).toHaveBeenCalledOnce();
  });

  it("reconstructs the structured error from a thrown serialize/update failure", async () => {
    const errorJson = JSON.stringify({
      Code: "write-budget",
      Message: "A binary write safety limit was exceeded.",
      Offset: 12,
      Path: "root.value",
    });
    installAdapter({
      serialize: vi.fn(() => {
        throw new Error(errorJson);
      }),
    });
    const { serialize } = await import("./cstruct-wasm");

    const result = serialize("struct root { byte value; };", { value: 42 });
    expect(result.Success).toBe(false);
    expect(result.Data).toBeNull();
    expect(result.Error).toEqual({
      Code: "write-budget",
      Message: "A binary write safety limit was exceeded.",
      Offset: 12,
      Path: "root.value",
    });
  });

  it("throws a TypeError when a serialize/update failure's message is not a valid ErrorDetails payload", async () => {
    installAdapter({
      serialize: vi.fn(() => {
        throw new Error("not json");
      }),
    });
    const { serialize } = await import("./cstruct-wasm");

    expect(() => serialize("struct root { byte value; };", { value: 42 })).toThrow(
      /invalid serialize error/i,
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
