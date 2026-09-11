/**
 * Browser library entry point for the CStructSharp WebAssembly bundle.
 *
 * Keep this file beside main.js and _framework/ when distributing the bundle.
 */

import { createPublicApi } from "./cstructsharp-api.js";

let loading;

/** Load the managed CStructSharp exports and return the browser API. */
export async function loadCStructSharpWasm() {
  if (globalThis.CStructSharpWasm?.ready) {
    return globalThis.CStructSharpWasm;
  }

  if (!loading) {
    loading = import("./main.js").then(() => {
      if (globalThis.CStructSharpWasm?.ready) {
        return globalThis.CStructSharpWasm;
      }
      throw new Error("CStructSharp WASM finished loading without usable exports.");
    });
  }

  return loading;
}

export const { parse, parseWithDebug, serialize, update, getVersion } =
  createPublicApi(loadCStructSharpWasm);
