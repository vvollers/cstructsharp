import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import http from "node:http";
import { pathToFileURL } from "node:url";
import { build, createServer, preview } from "../../apps/workshop/node_modules/vite/dist/node/index.js";
import { chromium } from "../../apps/workshop/node_modules/@playwright/test/index.mjs";

const exercise = `
const def = "struct header { uint16 kind; uint32 length; };";
const opts = {rootTypeName:"header"};
const [a,b] = await Promise.all([api.loadCStructSharpWasm(),api.loadCStructSharpWasm()]);
if (a !== b) throw Error("Concurrent loads created different APIs");
const bytes = new Uint8Array([2,0,6,0,0,0]);
const read = await api.parseWithDebug(def, bytes, opts);
const written = await api.serialize(def, {kind:3,length:6}, opts);
const changed = await api.update(def, bytes, "header.kind", 4, opts);
const invalid = await api.parseWithDebug(def, new Uint8Array(), opts);
const fileBytes = new Uint8Array(8 * 1024 * 1024); fileBytes.set(bytes);
const sourceRead = await api.parse(def, new Blob([fileBytes]), opts);
const streamRead = await api.parseWithDebug(def, new Response(bytes), opts);
if (!sourceRead.Success || JSON.parse(sourceRead.Data).header.kind !== 2 || sourceRead.DebugData.length || !streamRead.Success) throw Error("Large source parity failed");
const large = await api.serialize("struct large { uint64 value; };", {value:18446744073709551615n}, {rootTypeName:"large"});
if (JSON.parse(read.Data).header.kind !== 2 || !written.Success || written.Data[0] !== 3 || !changed.Success || changed.Data[0] !== 4 || bytes[0] !== 2 || invalid.Success || !large.Success || !large.Data.every(v=>v===255)) throw Error("Operation parity failed");
try { await api.loadCStructSharpWasm({runtimeUrl:"/different/"}); throw Error("Reconfiguration accepted"); }
catch(error) { if(!error.message.includes("different runtimeUrl")) throw error; }
window.result = {version:await api.getVersion(), kind:JSON.parse(read.Data).header.kind};
`;

export async function testBrowserConsumer(consumer, installed, info) {
  const { cstructsharp } = await import(pathToFileURL(path.join(installed, "vite.js")).href);
  const browser = await chromium.launch({ headless: true });
  async function visit(url) {
    const page = await browser.newPage();
    const errors = [];
    page.on("pageerror", (error) => errors.push(error.message));
    page.on("response", (response) => {
      if (response.status() >= 400) errors.push(`${response.status()} ${response.url()}`);
    });
    await page.route("**/*", (route) => {
      if (new URL(route.request().url()).hostname !== "127.0.0.1") return route.abort();
      return route.continue();
    });
    try {
      await page.goto(url);
      await page.waitForFunction(() => window.result || window.failure, { timeout: 30000 });
      const outcome = await page.evaluate(() => ({
        result: window.result,
        failure: window.failure,
      }));
      assert.equal(outcome.failure, undefined);
      assert.equal(outcome.result.kind, 2);
      assert.ok(outcome.result.version.startsWith(`CStructSharp WASM ${info.version}`));
      assert.deepEqual(errors, []);
    } finally {
      await page.close();
    }
  }
  fs.writeFileSync(
    path.join(consumer, "index.html"),
    '<!doctype html><html><head><meta charset="utf-8"><title>npm consumer</title><link rel="icon" href="data:,"></head><body><script type="module" src="./app.js"></script></body></html>',
  );
  fs.writeFileSync(
    path.join(consumer, "app.js"),
    `import * as api from "cstructsharp";\ntry { ${exercise} } catch(error) { window.failure = error.stack; }`,
  );
  try {
    for (const base of ["/", "/nested/app/"]) {
      const config = {
        root: consumer,
        configFile: false,
        base,
        plugins: [cstructsharp()],
        logLevel: "warn",
        server: { host: "127.0.0.1", port: 0 },
        build: { outDir: `dist-${base === "/" ? "root" : "nested"}`, emptyOutDir: true },
      };
      const dev = await createServer(config);
      try {
        await dev.listen();
        await visit(`http://127.0.0.1:${dev.httpServer.address().port}${base}`);
        fs.writeFileSync(
          path.join(consumer, "server-check.js"),
          'import { getVersion } from "cstructsharp"; export const version = await getVersion();',
        );
        assert.ok(
          (await dev.ssrLoadModule("/server-check.js")).version.startsWith(
            `CStructSharp WASM ${info.version}`,
          ),
        );
      } finally {
        await dev.close();
      }
      await build(config);
      const prod = await preview({ ...config, preview: { host: "127.0.0.1", port: 0 } });
      try {
        await visit(`http://127.0.0.1:${prod.httpServer.address().port}${base}`);
      } finally {
        await new Promise((resolve, reject) =>
          prod.httpServer.close((error) => (error ? reject(error) : resolve())),
        );
      }
    }
    // No Vite transforms: test the explicit browser URL against files copied by the installed CLI.
    const manual = http.createServer((req, res) => {
      const pathname = new URL(req.url, "http://localhost").pathname;
      res.setHeader(
        "Content-Security-Policy",
        "default-src 'self'; script-src 'self' 'wasm-unsafe-eval'; connect-src 'self'; img-src 'self' data:",
      );
      if (pathname === "/") {
        res.setHeader("Content-Type", "text/html");
        res.end(
          '<!doctype html><link rel="icon" href="data:,"><script type="module" src="/manual.js"></script>',
        );
        return;
      }
      if (pathname === "/manual.js") {
        res.setHeader("Content-Type", "text/javascript");
        res.end(
          `import * as api from '/pkg/browser.js'; try { await api.loadCStructSharpWasm({runtimeUrl:'/static-runtime/'}); ${exercise} } catch(error) { window.failure=error.stack; }`,
        );
        return;
      }
      const directory = pathname.startsWith("/pkg/") ? installed : path.join(consumer, "public");
      const relative = pathname.startsWith("/pkg/") ? pathname.slice(5) : pathname.slice(1);
      const file = path.resolve(directory, relative);
      if (
        !file.startsWith(`${directory}${path.sep}`) ||
        !fs.existsSync(file) ||
        !fs.statSync(file).isFile()
      ) {
        res.statusCode = 404;
        res.end();
        return;
      }
      res.setHeader(
        "Content-Type",
        file.endsWith(".wasm")
          ? "application/wasm"
          : file.endsWith(".js")
            ? "text/javascript"
            : "application/json",
      );
      fs.createReadStream(file).pipe(res);
    });
    await new Promise((resolve) => manual.listen(0, "127.0.0.1", resolve));
    try {
      await visit(`http://127.0.0.1:${manual.address().port}/`);
    } finally {
      await new Promise((resolve) => manual.close(resolve));
    }
  } finally {
    await browser.close();
  }
  console.log("Browser consumers passed: Vite dev/build, root/nested base, SSR, static copy, CSP.");
}
