import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import type { InteropResult, RawWasmAdapter } from "./contract";

const validParseResult: InteropResult = {
  contractVersion: 10,
  operation: "parse",
  success: true,
  root: "root",
  data: { value: 42 },
  debug: [
    {
      start: 0,
      end: 1,
      path: "root.value",
      type: "byte",
      value: "42",
    },
  ],
  error: null,
};

/**
 * Builds the envelope the package adapter returns for a write.
 * @param operation The write operation.
 * @param data The bytes on success; null on failure.
 * @param error The structured error of a failure.
 * @returns The envelope.
 */
function writeResult(
  operation: "serialize" | "update",
  data: Uint8Array | null,
  error: InteropResult["error"] = null,
): InteropResult {
  return {
    contractVersion: 10,
    operation,
    success: error === null,
    root: null,
    data,
    debug: [],
    error,
  } as InteropResult;
}

/**
 * Publishes a fake ready adapter on window whose calls return the valid envelope unless overridden.
 * @param overrides Adapter members to replace.
 * @returns The installed adapter.
 */
function installAdapter(overrides: Partial<RawWasmAdapter> = {}): RawWasmAdapter {
  const adapter: RawWasmAdapter = {
    compile: vi.fn() as unknown as RawWasmAdapter["compile"],
    resolveAddressSource: vi.fn() as unknown as RawWasmAdapter["resolveAddressSource"],
    collectBytes: vi.fn() as unknown as RawWasmAdapter["collectBytes"],
    getStaticPlan: vi.fn() as unknown as RawWasmAdapter["getStaticPlan"],
    ready: true,
    error: null,
    parseWithDebug: vi.fn(() => JSON.stringify(validParseResult)),
    parseBytes: vi.fn(() => JSON.stringify(validParseResult)),
    parseSource: vi.fn(async () => validParseResult) as unknown as RawWasmAdapter["parseSource"],
    serialize: vi.fn(() =>
      writeResult("serialize", new Uint8Array([0x2a])),
    ) as unknown as RawWasmAdapter["serialize"],
    updateStream: vi.fn(() =>
      writeResult("update", new Uint8Array([0x2a])),
    ) as unknown as RawWasmAdapter["updateStream"],
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
    const { parseWithDebug } = await import("./adapter");
    const bytes = Uint8Array.from({ length: 1_048_576 }, (_, index) => index & 0xff);
    const options = {
      root: "root",
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
    ["malformed debug entry", { ...validParseResult, debug: [{}] }],
    ["malformed root", { ...validParseResult, root: 7 }],
    [
      "success with an error",
      {
        ...validParseResult,
        error: {
          code: "bad",
          message: "bad",
          offset: null,
          path: null,
          member: null,
          memberType: null,
          line: null,
          column: null,
        },
      },
    ],
    [
      "failure without an error",
      { ...validParseResult, success: false, data: null, debug: [], error: null },
    ],
    ["stale contract version", { ...validParseResult, contractVersion: 7 }],
  ])("rejects a structurally invalid envelope: %s", async (_name, response) => {
    installAdapter({ parseWithDebug: vi.fn(() => JSON.stringify(response)) });
    const { parseWithDebug } = await import("./adapter");

    expect(() => parseWithDebug("struct root { byte value; };", new Uint8Array([42]))).toThrow(
      /invalid parse response envelope/i,
    );
  });

  it("returns the package's write envelopes with the encoded bytes as data", async () => {
    const adapter = installAdapter();
    const { serialize, updateStream } = await import("./adapter");

    const serialized = serialize("struct root { byte value; };", { value: 42n });
    expect(serialized.success).toBe(true);
    expect(serialized.data).toEqual(new Uint8Array([0x2a]));
    expect(adapter.serialize).toHaveBeenCalledOnce();
    // The package helper encodes the value: a bigint travels as exact decimal text.
    expect(vi.mocked(adapter.serialize).mock.calls[0][1]).toBe('{"value":"42"}');

    const updated = updateStream(
      "struct root { byte value; };",
      new Uint8Array([0]),
      "root.value",
      42,
    );
    expect(updated.success).toBe(true);
    expect(updated.data).toEqual(new Uint8Array([0x2a]));
    expect(adapter.updateStream).toHaveBeenCalledOnce();
  });

  it("returns the structured error of a failed serialize/update envelope", async () => {
    const error = {
      code: "write-budget",
      message:
        "Write operation exceeded the configured total write-byte limit (field 'value' (byte), in 'root', offset 12).",
      offset: 12,
      path: "root.value",
      member: "value",
      memberType: "byte",
      line: null,
      column: null,
    };
    installAdapter({
      serialize: vi.fn(() =>
        writeResult("serialize", null, error),
      ) as unknown as RawWasmAdapter["serialize"],
    });
    const { serialize } = await import("./adapter");

    const result = serialize("struct root { byte value; };", { value: 42 });
    expect(result.success).toBe(false);
    expect(result.data).toBeNull();
    expect(result.error).toEqual(error);
  });

  it.each([
    [
      "stale contract version",
      { ...writeResult("serialize", new Uint8Array([1])), contractVersion: 8 },
    ],
    ["wrong operation", writeResult("update", new Uint8Array([1]))],
    ["failure without an error", { ...writeResult("serialize", null), success: false }],
  ])("rejects a structurally invalid write envelope: %s", async (_name, response) => {
    installAdapter({
      serialize: vi.fn(() => response) as unknown as RawWasmAdapter["serialize"],
    });
    const { serialize } = await import("./adapter");

    expect(() => serialize("struct root { byte value; };", { value: 42 })).toThrow(
      /invalid serialize response envelope/i,
    );
  });

  it("removes a failed bootstrap script before a retry", async () => {
    const { initWasm } = await import("./adapter");
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
