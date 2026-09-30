/**
 * Browser library entry point for the CStructSharp WebAssembly bundle.
 *
 * Keep this file beside main.js and _framework/ when distributing the bundle.
 */

import { createPublicApi } from "./cstructsharp-api.js";

/** The attempt in progress or the successful one; a failed attempt is cleared so the next call starts again. */
let loading;
/** How many attempts have started; a retry imports main.js under a new URL so the module runs again. */
let attempts = 0;

/**
 * Loads the bundle's runtime and returns the raw adapter that main.js publishes on `globalThis.CStructSharpWasm`.
 * Concurrent calls share one attempt. A failed attempt rejects with the runtime's own error and is not kept, so a
 * later call tries again (for example after a transient network failure).
 * @returns {Promise<object>} The ready raw adapter.
 * @throws {Error} When main.js cannot be imported, or the runtime published a failure instead of an adapter.
 */
export async function loadCStructSharpWasm() {
  if (globalThis.CStructSharpWasm?.ready) {
    return globalThis.CStructSharpWasm;
  }

  loading ??= startRuntime().catch((error) => {
    loading = undefined;
    throw error;
  });
  return loading;
}

/**
 * Runs main.js once per attempt. A module runs only the first time its URL is imported, so a retry adds a query
 * that names the attempt; its own imports (the .NET runtime, bootstrap.js) resolve to the same files.
 * @returns {Promise<object>} The ready raw adapter.
 * @throws {Error} The import failure, or the startup error main.js recorded in the published failure object.
 */
async function startRuntime() {
  attempts += 1;
  await import(attempts === 1 ? "./main.js" : `./main.js?attempt=${attempts}`);
  const published = globalThis.CStructSharpWasm;
  if (published?.ready) {
    return published;
  }
  throw new Error(`CStructSharp WASM failed to load: ${published?.error ?? "the bundle published no runtime adapter."}`);
}

export const { compile, parse, parseWithDebug, serialize, update, resolveAddress, getVersion } =
  createPublicApi(loadCStructSharpWasm);
