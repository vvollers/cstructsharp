/**
 * Typed browser boundary for the CStructSharp WebAssembly module.
 *
 * The managed bridge always returns one versioned envelope. Keeping all JSON
 * validation here means the rest of the Vue application can work with a
 * predictable contract instead of handling three subtly different responses.
 */

import {
  INTEROP_CONTRACT_VERSION,
  type InteropOperation,
  type InteropResult,
  type ParseWithDebugOptions,
  type RawWasmAdapter,
  type SerializeOptions,
  type UpdateOptions,
} from "./cstruct-contract";

export type {
  DebugDataItem,
  ErrorDetails,
  InteropOperation,
  InteropResult,
  ParseWithDebugOptions,
  RawWasmAdapter,
  SerializeOptions,
  UpdateOptions,
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
    uint8ArrayToBase64(binaryData),
    options ?? null,
  );
  return parseInteropResult(resultJson, "parse");
}

export function serializeToBase64(
  cstructDefinition: string,
  data: unknown,
  options?: SerializeOptions,
): InteropResult {
  const resultJson = requireReadyWasm().serializeToBase64(
    cstructDefinition,
    stringifyInteropValue(data ?? {}),
    options ?? null,
  );
  return parseInteropResult(resultJson, "serialize");
}

export function updateStreamToBase64(
  cstructDefinition: string,
  binaryData: Uint8Array,
  elementNameOrPath: string,
  value: unknown,
  options?: UpdateOptions,
): InteropResult {
  const resultJson = requireReadyWasm().updateStreamToBase64(
    cstructDefinition,
    uint8ArrayToBase64(binaryData),
    elementNameOrPath,
    stringifyInteropValue(value),
    options ?? null,
  );
  return parseInteropResult(resultJson, "update");
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
    value.ContractVersion !== INTEROP_CONTRACT_VERSION ||
    value.Operation !== expectedOperation ||
    typeof value.Success !== "boolean" ||
    (typeof value.Data !== "string" && value.Data !== null) ||
    !Array.isArray(value.DebugData) ||
    !value.DebugData.every(isDebugDataItem) ||
    !isErrorDetails(value.Error) ||
    (value.Success ? value.Error !== null : value.Error === null) ||
    (!value.Success && value.Data !== null)
  ) {
    throw new TypeError(`WASM returned an invalid ${expectedOperation} response envelope.`);
  }

  return value as InteropResult;
}

function isDebugDataItem(value: unknown): boolean {
  if (typeof value !== "object" || value === null) {
    return false;
  }

  const item = value as Record<string, unknown>;
  return (
    Number.isSafeInteger(item.CurPos) &&
    Number.isSafeInteger(item.EndPos) &&
    typeof item.DebugStackString === "string" &&
    typeof item.Type === "string" &&
    (typeof item.Value === "string" || item.Value === null) &&
    (typeof item.Buffer === "string" || item.Buffer === null)
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
    typeof error.Code === "string" &&
    error.Code.length > 0 &&
    typeof error.Message === "string" &&
    error.Message.length > 0 &&
    (error.Offset === null || Number.isSafeInteger(error.Offset)) &&
    (error.Path === null || typeof error.Path === "string")
  );
}

function stringifyInteropValue(value: unknown): string {
  return JSON.stringify(value, (_key, current: unknown) =>
    typeof current === "bigint" ? current.toString(10) : current,
  );
}

function uint8ArrayToBase64(bytes: Uint8Array): string {
  const chunkSize = 24_576;
  const encoded: string[] = [];
  for (let offset = 0; offset < bytes.length; offset += chunkSize) {
    const chunk = bytes.subarray(offset, offset + chunkSize);
    encoded.push(btoa(String.fromCharCode(...chunk)));
  }

  return encoded.join("");
}
