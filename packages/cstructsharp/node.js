import { createPublicApi } from "./cstructsharp-api.js";
import { loadRuntime } from "./runtime-loader.js";
import { readFile } from "node:fs/promises";
import { createHash } from "node:crypto";

/** The one startup attempt of this process; later calls share it, including a failed one. */
let loading;

/**
 * Loads the installed runtime once per process and returns the raw adapter. Every runtime file is checked against
 * the package's manifest (path and SHA-256) before .NET starts; nothing is fetched over the network.
 * @param {{ runtimeUrl?: string }} [options] Not supported in Node; a `runtimeUrl` rejects with a TypeError.
 * @returns {Promise<object>} The raw adapter (see RawWasmAdapter in index.d.ts).
 * @throws {Error} Through the promise, when a runtime file is missing, altered or unsafe, or startup fails.
 */
export function loadCStructSharpWasm(options) {
  if (options?.runtimeUrl !== undefined) {
    return Promise.reject(
      new TypeError(
        "runtimeUrl is a browser option; Node loads its installed runtime.",
      ),
    );
  }
  loading ??= (async () => {
    // Reject missing/corrupt resources before .NET starts: runtime startup failures can
    // otherwise invoke its host exit handler. No file is fetched over the network.
    const manifest = JSON.parse(
      await readFile(
        new URL("./runtime-manifest.json", import.meta.url),
        "utf8",
      ),
    );
    const base = new URL("./runtime/", import.meta.url);
    await Promise.all(
      manifest.files.map(async (file) => {
        if (
          !/^[A-Za-z0-9_./-]+$/.test(file.path) ||
          file.path.includes("..") ||
          file.path.startsWith("/")
        )
          throw new Error("Invalid CStructSharp runtime manifest path.");
        const bytes = await readFile(new URL(file.path, base));
        if (createHash("sha256").update(bytes).digest("hex") !== file.sha256)
          throw new Error(
            `CStructSharp runtime integrity mismatch: ${file.path}`,
          );
      }),
    );
    return loadRuntime(base);
  })();
  return loading;
}
export const { compile, parse, parseWithDebug, serialize, update, resolveAddress, getVersion } =
  createPublicApi(loadCStructSharpWasm);
