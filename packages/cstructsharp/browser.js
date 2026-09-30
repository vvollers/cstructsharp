import { createPublicApi } from "./cstructsharp-api.js";
import { loadRuntime } from "./runtime-loader.js";

/** The one startup attempt of this page; later calls share it, including a failed one. */
let loading;
/** The runtime directory URL the first call fixed; later calls must name the same one or none. */
let runtimeUrl;

/**
 * Loads the runtime once per page and returns the raw adapter. The runtime directory comes from `runtimeUrl`, a
 * URL fixed earlier, or the one the `cstructsharp/vite` plugin compiled in; a relative URL resolves against the
 * document. Errors are returned as a rejected promise, never thrown synchronously.
 * @param {{ runtimeUrl?: string }} [options] `runtimeUrl` is an HTTP(S) directory URL ending in `/` that holds the
 *   copied runtime assets.
 * @returns {Promise<object>} The raw adapter (see RawWasmAdapter in index.d.ts).
 * @throws {Error} Through the promise, outside a browser window, without a configured runtime URL, for an invalid
 *   URL or a URL different from the one already in use, or when startup fails.
 */
export function loadCStructSharpWasm(options = {}) {
  try {
    if (typeof window === "undefined" || typeof document === "undefined") {
      throw new Error(
        "Use the cstructsharp Node entry point on the server; the browser entry requires a browser window.",
      );
    }
    // Replaced by the Vite plugin in application builds; explicit URLs support other tools.
    const configured =
      options.runtimeUrl ??
      runtimeUrl ??
      (typeof __CSTRUCTSHARP_RUNTIME_URL__ !== "undefined"
        ? __CSTRUCTSHARP_RUNTIME_URL__
        : undefined);
    if (!configured && !runtimeUrl) {
      throw new Error(
        "Configure cstructsharp/vite or call loadCStructSharpWasm({ runtimeUrl: '/cstructsharp/' }) after copying the runtime assets.",
      );
    }
    const base = configured
      ? new URL(configured, document.baseURI).href
      : runtimeUrl;
    if (!/^https?:/.test(base) || !base.endsWith("/")) {
      throw new TypeError(
        "runtimeUrl must be an HTTP(S) directory URL ending in '/'.",
      );
    }
    if (runtimeUrl && runtimeUrl !== base)
      throw new Error(
        "CStructSharp is already initialized with a different runtimeUrl.",
      );
    runtimeUrl = base;
    loading ??= loadRuntime(new URL(base));
    return loading;
  } catch (error) {
    return Promise.reject(error);
  }
}
export const { compile, parse, parseWithDebug, serialize, update, resolveAddress, getVersion } =
  createPublicApi(loadCStructSharpWasm);
