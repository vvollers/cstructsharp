import { createPublicApi } from "./cstructsharp-api.js";
import { loadRuntime } from "./runtime-loader.js";

let loading;
let runtimeUrl;
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
export const { compile, parse, parseWithDebug, serialize, update, getVersion } =
  createPublicApi(loadCStructSharpWasm);
