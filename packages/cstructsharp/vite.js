import fs from "node:fs";
import path from "node:path";
import { createHash } from "node:crypto";
import { manifest, runtimeDirectory } from "./assets.js";

/** Serve/emit the complete .NET runtime as opaque files, outside Vite's module graph. */
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
    configureServer(server) {
      const prefix = `${new URL(base, "http://localhost").pathname.replace(/\/$/, "")}/${directory}/`;
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
          .on("error", () => {
            res.statusCode = 500;
            res.end();
          })
          .pipe(res);
      });
    },
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
