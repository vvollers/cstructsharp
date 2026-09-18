/**
 * Typed browser boundary for the CStructSharp WebAssembly module.
 *
 * The managed bridge always returns one versioned envelope. Keeping all JSON
 * validation here means the rest of the Vue application can work with a
 * predictable contract instead of handling three subtly different responses.
 */

import {
  INTEROP_CONTRACT_VERSION,
  type ErrorDetails,
  type InteropOperation,
  type InteropResult,
  type ParseWithDebugOptions,
  type RawWasmAdapter,
  type SerializeCallOptions,
  type UpdateCallOptions,
} from "./cstruct-contract";

export {
  INTEROP_CONTRACT_VERSION,
  isEnumValue,
  isPointerValue,
  isUnionValue,
} from "./cstruct-contract";
export type {
  DebugItem,
  ErrorDetails,
  InteropOperation,
  InteropResult,
  ParseWithDebugOptions,
  ParsedStruct,
  ParsedValue,
  RawWasmAdapter,
  SerializeCallOptions,
  UpdateCallOptions,
} from "./cstruct-contract";
export type {
  SerializeCallOptions as SerializeOptions,
  UpdateCallOptions as UpdateOptions,
} from "./cstruct-contract";

// Retain the original exported name for source compatibility with applications
// that only consume parse responses.
export type ParseResult = InteropResult;

type CStructSharpWasmReady = RawWasmAdapter;

interface CStructSharpWasmFailed {
  exports: null;
  ready: false;
  error: string;
}

type CStructSharpWasmGlobal = CStructSharpWasmReady | CStructSharpWasmFailed;

declare global {
  interface Window {
    CStructSharpWasm?: CStructSharpWasmGlobal;
  }
}

let initPromise: Promise<void> | null = null;
const bootstrapSelector = "script[data-cstructsharp-wasm]";

/**
 * Load the .NET runtime once. A failed attempt is deliberately not cached, so
 * callers can retry after a transient network or asset-loading failure.
 */
export async function initWasm(): Promise<void> {
  if (window.CStructSharpWasm?.ready) {
    return;
  }

  if (initPromise) {
    return initPromise;
  }

  const attempt = new Promise<void>((resolve, reject) => {
    if (window.CStructSharpWasm?.ready) {
      resolve();
      return;
    }

    let timeoutId = 0;
    const cleanup = (removeScript = false): void => {
      window.clearTimeout(timeoutId);
      window.removeEventListener("cstructsharp-wasm-ready", handleReady);
      window.removeEventListener("cstructsharp-wasm-error", handleFailure);
      if (removeScript) {
        document.head.querySelector(bootstrapSelector)?.remove();
      }
    };
    const handleReady = (): void => {
      cleanup(true);
      if (window.CStructSharpWasm?.ready) {
        resolve();
      } else {
        reject(new Error("WASM reported readiness without callable exports."));
      }
    };
    const handleFailure = (event: Event): void => {
      cleanup(true);
      const detail =
        event instanceof CustomEvent && typeof event.detail === "string"
          ? event.detail
          : window.CStructSharpWasm?.error;
      reject(new Error(detail || "CStructSharp WASM initialization failed."));
    };

    window.addEventListener("cstructsharp-wasm-ready", handleReady, {
      once: true,
    });
    window.addEventListener("cstructsharp-wasm-error", handleFailure, {
      once: true,
    });

    timeoutId = window.setTimeout(() => {
      cleanup(true);
      reject(new Error("WASM initialization timed out after 30 seconds."));
    }, 30_000);

    const existingScript = document.head.querySelector<HTMLScriptElement>(bootstrapSelector);
    const script = existingScript ?? document.createElement("script");
    if (!existingScript) {
      script.type = "module";
      script.dataset.cstructsharpWasm = "";
      // Resolve relative to Vite's configured base URL so deployments under a
      // sub-path do not accidentally request assets from the domain root.
      script.src = new URL("wasm/main.js", document.baseURI).toString();
    }
    script.onerror = () => {
      cleanup(true);
      reject(new Error("Failed to load the WASM bootstrap script."));
    };
    if (!existingScript) {
      document.head.appendChild(script);
    }
  });

  initPromise = attempt.catch((error: unknown) => {
    initPromise = null;
    throw error;
  });
  return initPromise;
}

export function isLoaded(): boolean {
  return window.CStructSharpWasm?.ready ?? false;
}

export function getVersion(): string {
  return requireReadyWasm().getVersion();
}

export function parseWithDebug(
  cstructDefinition: string,
  binaryData: Uint8Array,
  options?: ParseWithDebugOptions,
): ParseResult {
  const resultJson = requireReadyWasm().parseWithDebug(
    cstructDefinition,
    binaryData,
    options ?? null,
  );
  return parseInteropResult(resultJson, "parse");
}

export async function parseSourceWithDebug(
  definition: string,
  source: Blob | Uint8Array,
  options?: ParseWithDebugOptions,
): Promise<ParseResult> {
  return requireReadyWasm().parseSource(definition, source, options ?? null, true);
}

export function serialize(
  cstructDefinition: string,
  data: unknown,
  options?: SerializeCallOptions,
): InteropResult {
  return runBinaryOperation("serialize", options, () =>
    requireReadyWasm().serialize(
      cstructDefinition,
      stringifyInteropValue(data ?? {}),
      options ?? null,
    ),
  );
}

export function updateStream(
  cstructDefinition: string,
  binaryData: Uint8Array,
  elementNameOrPath: string,
  value: unknown,
  options?: UpdateCallOptions,
): InteropResult {
  return runBinaryOperation("update", options, () =>
    requireReadyWasm().updateStream(
      cstructDefinition,
      binaryData,
      elementNameOrPath,
      stringifyInteropValue(value),
      options ?? null,
    ),
  );
}

/**
 * Runs a byte-returning export: success returns the bytes directly (a native Uint8Array, never Base64 text);
 * failure is reported by the managed export throwing rather than through the JSON envelope "parse" uses, since
 * there's no envelope object left to carry an Error field alongside a native byte-array success payload. The
 * thrown error's message is the same JSON-serialized ErrorDetails shape the "parse" envelope's Error field
 * already uses, so this reconstructs an identical InteropResult either way.
 */
function runBinaryOperation(
  operation: "serialize" | "update",
  options: { root?: string | null } | undefined,
  invoke: () => Uint8Array,
): InteropResult {
  const root = typeof options?.root === "string" ? options.root : null;
  try {
    return {
      contractVersion: INTEROP_CONTRACT_VERSION,
      operation,
      success: true,
      root,
      data: invoke(),
      debug: [],
      error: null,
    };
  } catch (cause) {
    return {
      contractVersion: INTEROP_CONTRACT_VERSION,
      operation,
      success: false,
      root,
      data: null,
      debug: [],
      error: parseBridgeError(cause, operation),
    };
  }
}

function parseBridgeError(cause: unknown, operation: InteropOperation): ErrorDetails {
  const message = cause instanceof Error ? cause.message : String(cause);
  let parsed: unknown;
  try {
    parsed = JSON.parse(message);
  } catch {
    throw new TypeError(`WASM returned an invalid ${operation} error.`);
  }

  if (parsed === null || !isErrorDetails(parsed)) {
    throw new TypeError(`WASM returned an invalid ${operation} error.`);
  }

  return parsed as ErrorDetails;
}

/**
 * Convert a hexadecimal string only after validating the entire input. Silent
 * truncation of an odd final nibble or parseInt's partial parsing would produce
 * plausible-looking but incorrect binary test data.
 */
export function hexToBytes(hex: string): Uint8Array {
  const cleanHex = hex.replace(/\s/g, "");
  if (cleanHex.length % 2 !== 0) {
    throw new TypeError("Hex input must contain a whole number of bytes.");
  }

  if (!/^[0-9a-f]*$/i.test(cleanHex)) {
    throw new TypeError("Hex input contains a non-hexadecimal character.");
  }

  const bytes = new Uint8Array(cleanHex.length / 2);
  for (let index = 0; index < bytes.length; index++) {
    bytes[index] = Number.parseInt(cleanHex.slice(index * 2, index * 2 + 2), 16);
  }

  return bytes;
}

function requireReadyWasm(): CStructSharpWasmReady {
  const wasm = window.CStructSharpWasm;
  if (!wasm?.ready) {
    throw new Error("WASM not initialized. Call initWasm() first.");
  }

  return wasm;
}

function parseInteropResult(json: string, expectedOperation: InteropOperation): InteropResult {
  let value: Partial<InteropResult>;
  try {
    value = JSON.parse(json) as Partial<InteropResult>;
  } catch {
    throw new TypeError(`WASM returned an invalid ${expectedOperation} response envelope.`);
  }

  if (
    value.contractVersion !== INTEROP_CONTRACT_VERSION ||
    value.operation !== expectedOperation ||
    typeof value.success !== "boolean" ||
    (value.root !== null && typeof value.root !== "string") ||
    !Array.isArray(value.debug) ||
    !value.debug.every(isDebugItem) ||
    !isErrorDetails(value.error) ||
    (value.success ? value.error !== null : value.error === null) ||
    (!value.success && value.data !== null)
  ) {
    throw new TypeError(`WASM returned an invalid ${expectedOperation} response envelope.`);
  }

  return value as InteropResult;
}

function isDebugItem(value: unknown): boolean {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const item = value as Record<string, unknown>;
  return (
    Number.isSafeInteger(item.start) &&
    Number.isSafeInteger(item.end) &&
    typeof item.path === "string" &&
    typeof item.type === "string" &&
    (typeof item.value === "string" || item.value === null)
  );
}

function isErrorDetails(value: unknown): boolean {
  if (value === null) {
    return true;
  }

  if (typeof value !== "object") {
    return false;
  }

  const error = value as Record<string, unknown>;
  return (
    typeof error.code === "string" &&
    error.code.length > 0 &&
    typeof error.message === "string" &&
    error.message.length > 0 &&
    (error.offset === null || Number.isSafeInteger(error.offset)) &&
    (error.path === null || typeof error.path === "string") &&
    (error.member === null || typeof error.member === "string") &&
    (error.line === null || Number.isSafeInteger(error.line))
  );
}

function stringifyInteropValue(value: unknown): string {
  return JSON.stringify(value, (_key, current: unknown) =>
    typeof current === "bigint" ? current.toString(10) : current,
  );
}
