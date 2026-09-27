/**
 * Typed browser boundary for the CStructSharp WebAssembly module, shared by the explorer and the inspector.
 *
 * The bootstrap script publishes the package adapter on `window.CStructSharpWasm`. Every envelope is validated here
 * before an app reads it, so the rest of each app works with the contract in `contract.ts` instead of checking
 * three differently shaped responses.
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
} from "./contract";

/** The global the bootstrap publishes when the runtime failed to load. */
interface CStructSharpWasmFailed {
  exports: null;
  ready: false;
  error: string;
}

declare global {
  interface Window {
    CStructSharpWasm?: RawWasmAdapter | CStructSharpWasmFailed;
  }
}

let initPromise: Promise<void> | null = null;
const bootstrapSelector = "script[data-cstructsharp-wasm]";

/**
 * Loads the .NET runtime once. A failed attempt is not cached, so callers can retry after a transient network or
 * asset-loading failure; the failed bootstrap script is removed first.
 * @returns A promise that settles when the runtime is ready, or rejects with the load failure or a 30 s timeout.
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
    /** Removes both listeners and the timeout; with removeScript, also the bootstrap script so a retry reloads it. */
    const cleanup = (removeScript = false): void => {
      window.clearTimeout(timeoutId);
      window.removeEventListener("cstructsharp-wasm-ready", handleReady);
      window.removeEventListener("cstructsharp-wasm-error", handleFailure);
      if (removeScript) {
        document.head.querySelector(bootstrapSelector)?.remove();
      }
    };
    /** Settles the attempt when the bootstrap reports readiness. */
    const handleReady = (): void => {
      cleanup(true);
      if (window.CStructSharpWasm?.ready) {
        resolve();
      } else {
        reject(new Error("WASM reported readiness without callable exports."));
      }
    };
    /** Rejects the attempt with the bootstrap's failure detail. */
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

/**
 * Reports whether the runtime has loaded.
 * @returns True once {@link initWasm} has succeeded.
 */
export function isLoaded(): boolean {
  return window.CStructSharpWasm?.ready ?? false;
}

/**
 * Reads the managed library version.
 * @returns The version text.
 * @throws Error when the runtime has not loaded.
 */
export function getVersion(): string {
  return requireReadyWasm().getVersion();
}

/**
 * Parses bytes on the calling thread and records every value's byte range.
 * @param cstructDefinition Portable layout source.
 * @param binaryData The input bytes.
 * @param options Compile and parse options.
 * @returns The validated parse envelope.
 * @throws TypeError when the bridge returns an envelope that breaks the contract.
 */
export function parseWithDebug(
  cstructDefinition: string,
  binaryData: Uint8Array,
  options?: ParseWithDebugOptions,
): InteropResult {
  const json = requireReadyWasm().parseWithDebug(cstructDefinition, binaryData, options ?? null);
  let value: unknown;
  try {
    value = JSON.parse(json);
  } catch {
    throw new TypeError("WASM returned an invalid parse response envelope.");
  }

  return validateInteropResult(value, "parse");
}

/**
 * Parses any binary source through the worker and records every value's byte range.
 * @param definition Portable layout source.
 * @param source The input: a Blob (staged, never copied whole) or bytes.
 * @param options Compile and parse options.
 * @returns The validated parse envelope.
 * @throws TypeError when the bridge returns an envelope that breaks the contract.
 */
export async function parseSourceWithDebug(
  definition: string,
  source: Blob | Uint8Array,
  options?: ParseWithDebugOptions,
): Promise<InteropResult> {
  const result = await requireReadyWasm().parseSource(definition, source, options ?? null, true);
  return validateInteropResult(result, "parse");
}

/**
 * Encodes a value as the layout's bytes.
 * @param cstructDefinition Portable layout source.
 * @param data The value to encode; bigint members are sent as exact decimal text.
 * @param options Compile and serialize options.
 * @returns A serialize envelope whose data is the bytes, or the structured error.
 */
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

/**
 * Replaces one value in a copy of the input bytes.
 * @param cstructDefinition Portable layout source.
 * @param binaryData The input bytes; they are not modified.
 * @param elementNameOrPath The member or path to replace.
 * @param value The new value.
 * @param options Compile and update options.
 * @returns An update envelope whose data is the complete updated bytes, or the structured error.
 */
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
 * @param operation The operation the envelope reports.
 * @param options The call options; their `root` is echoed in the envelope.
 * @param invoke Calls the export.
 * @returns The envelope.
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

/**
 * Reads the structured error a failing byte-returning export throws.
 * @param cause The thrown value; its message is JSON-serialized ErrorDetails.
 * @param operation The operation, for the error message.
 * @returns The error details.
 * @throws TypeError when the message is not an ErrorDetails payload.
 */
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
 * Returns the loaded adapter.
 * @returns The adapter.
 * @throws Error when the runtime has not loaded.
 */
function requireReadyWasm(): RawWasmAdapter {
  const wasm = window.CStructSharpWasm;
  if (!wasm?.ready) {
    throw new Error("WASM not initialized. Call initWasm() first.");
  }

  return wasm;
}

/**
 * Checks that an envelope from the bridge matches the contract before an app reads it.
 * @param result The envelope object the adapter returned.
 * @param expectedOperation The operation the call performed.
 * @returns The same envelope, typed.
 * @throws TypeError when the envelope breaks the contract.
 */
function validateInteropResult(
  result: unknown,
  expectedOperation: InteropOperation,
): InteropResult {
  const value = (
    typeof result === "object" && result !== null ? result : {}
  ) as Partial<InteropResult>;
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

/**
 * Checks one debug range: integer offsets, a path and type, and the value text or null.
 * @param value The candidate.
 * @returns True when it is a DebugItem.
 */
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

/**
 * Checks an envelope's error field: null, or a code and message with optional location fields.
 * @param value The candidate.
 * @returns True when it is null or ErrorDetails.
 */
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

/**
 * Serializes a value for the bridge, sending bigint as exact decimal text.
 * @param value The value.
 * @returns The JSON text.
 */
function stringifyInteropValue(value: unknown): string {
  return JSON.stringify(value, (_key, current: unknown) =>
    typeof current === "bigint" ? current.toString(10) : current,
  );
}
