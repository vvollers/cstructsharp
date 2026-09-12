import { createPublicApi } from "./cstructsharp-api.js";
import { loadRuntime } from "./runtime-loader.js";
import { readFile } from "node:fs/promises";
import { createHash } from "node:crypto";

let loading;
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
export const { compile, parse, parseWithDebug, serialize, update, getVersion } =
  createPublicApi(loadCStructSharpWasm);
