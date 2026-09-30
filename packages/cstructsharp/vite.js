import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { manifest, runtimeDirectory } from "./assets.js";

/**
 * The Vite plugin that hosts the runtime for the browser entry point. It serves (dev server) and emits (build) the
 * complete .NET runtime as opaque files under one content-hashed directory, outside Vite's module graph, and compiles
 * that directory's URL into the application, so `loadCStructSharpWasm` needs no `runtimeUrl`.
 * @returns {{ name: string, config: Function, configureServer: Function, generateBundle: Function }} The plugin.
 * @throws {Error} From `config`, when the application's `base` is relative (`""` or `"./"`).
 */
export function cstructsharp() {
  const hash = createHash("sha256")
    .update(JSON.stringify(manifest))
    .digest("hex")
    .slice(0, 16);
  const directory = `cstructsharp-${hash}`;
  const files = new Set(manifest.files.map((file) => file.path));
  let base = "/";
  return {
    name: "cstructsharp-runtime",
    /**
     * Records the application's base path and defines the runtime URL; keeps the package out of dependency
     * pre-bundling and SSR bundling.
     * @param {object} config The user's Vite configuration.
     * @returns {object} The configuration Vite merges in.
     */
    config(config) {
      base = config.base ?? "/";
      if (base === "" || base === "./")
        throw new Error(
          "cstructsharp/vite requires an absolute base path or HTTP(S) base URL.",
        );
      const url = `${base.replace(/\/$/, "")}/${directory}/`;
      return {
        define: { __CSTRUCTSHARP_RUNTIME_URL__: JSON.stringify(url) },
        optimizeDeps: { exclude: ["cstructsharp", "cstructsharp/browser"] },
        ssr: { external: ["cstructsharp"] },
      };
    },
    /**
     * Serves the runtime files listed in the manifest from the package on the dev server; any other name under the
     * runtime directory is a 404.
     * @param {object} server The Vite dev server.
     */
    configureServer(server) {
      const prefix = `${new URL(base, "http://localhost").pathname.replace(/\/$/, "")}/${directory}/`;
      // Answers requests under the runtime directory and passes every other request on.
      server.middlewares.use((req, res, next) => {
        const pathname = new URL(req.url, "http://localhost").pathname;
        if (!pathname.startsWith(prefix)) return next();
        const name = pathname.slice(prefix.length);
        if (!files.has(name)) {
          res.statusCode = 404;
          res.end("Unknown CStructSharp runtime asset");
          return;
        }
        res.setHeader(
          "Content-Type",
          name.endsWith(".wasm")
            ? "application/wasm"
            : name.endsWith(".js")
              ? "text/javascript"
              : "application/json",
        );
        res.setHeader("Cache-Control", "no-cache");
        fs.createReadStream(path.join(runtimeDirectory, name))
          // A file that cannot be read ends the response as a server error.
          .on("error", () => {
            res.statusCode = 500;
            res.end();
          })
          .pipe(res);
      });
    },
    /** Emits every runtime file of the manifest into the build output's runtime directory. */
    generateBundle() {
      for (const name of files)
        this.emitFile({
          type: "asset",
          fileName: `${directory}/${name}`,
          source: fs.readFileSync(path.join(runtimeDirectory, name)),
        });
    },
  };
}
