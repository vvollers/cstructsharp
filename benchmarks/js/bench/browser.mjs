#!/usr/bin/env node
// Browser harness: serves the repository over a local static server, drives browser/index.html in headless
// Chromium (Playwright), and collects the same case records as the Node harness plus cold-start samples from
// fresh browser contexts. Writes artifacts/js-bench/results/browser-<timestamp>.json (+ browser-latest.json).
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { chromium } from "playwright";
import { captureEnvironment } from "./environment.mjs";
import { repositoryRoot } from "./fixtures.mjs";

const here = path.resolve(path.dirname(new URL(import.meta.url).pathname));
const jsRoot = path.resolve(here, "..");
const resultsDirectory = path.join(repositoryRoot, "artifacts/js-bench/results");
fs.mkdirSync(resultsDirectory, { recursive: true });

const routes = {
  "/bundle/": path.join(repositoryRoot, "artifacts/js-bench/bundle"),
  "/fixtures/": path.join(repositoryRoot, "benchmarks/fixtures"),
  "/bench/": path.join(jsRoot, "bench"),
  "/browser/": path.join(jsRoot, "browser"),
  "/node_modules/": path.join(jsRoot, "node_modules"),
};
const mime = { ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json", ".wasm": "application/wasm", ".bin": "application/octet-stream", ".dat": "application/octet-stream", ".dll": "application/octet-stream", ".map": "application/json" };

const server = http.createServer((request, response) => {
  const url = new URL(request.url, "http://localhost");
  const prefix = Object.keys(routes).find((p) => url.pathname.startsWith(p));
  const file = prefix ? path.join(routes[prefix], decodeURIComponent(url.pathname.slice(prefix.length))) : null;
  if (!file || !file.startsWith(routes[prefix]) || !fs.existsSync(file) || fs.statSync(file).isDirectory()) {
    response.writeHead(404).end();
    return;
  }
  response.writeHead(200, {
    "Content-Type": mime[path.extname(file)] ?? "application/octet-stream",
    "Cache-Control": "no-store",
    "Cross-Origin-Opener-Policy": "same-origin",
    "Cross-Origin-Embedder-Policy": "require-corp",
  });
  fs.createReadStream(file).pipe(response);
});
await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
const origin = `http://127.0.0.1:${server.address().port}`;

const browser = await chromium.launch({ headless: true, args: ["--enable-precise-memory-info"] });
const filter = process.env.BENCH_FILTER ?? "";
const quick = process.env.BENCH_QUICK === "1";
const coldLaunches = Number(process.env.BENCH_COLD_LAUNCHES ?? 5);
const coldFixtures = (process.env.BENCH_COLD_FIXTURES ?? "real-png,prim-le-record").split(",");
const timeoutMs = Number(process.env.BENCH_BROWSER_TIMEOUT_MS ?? 60 * 60 * 1000);

try {
  // Warm steady-state cases in one long-lived page.
  const context = await browser.newContext();
  const page = await context.newPage();
  page.on("console", (message) => { if (message.type() === "error") console.error("[page]", message.text()); });
  const query = new URLSearchParams({ mode: "bench", ...(filter ? { filter } : {}), ...(quick ? { quick: "1" } : {}) });
  await page.goto(`${origin}/browser/index.html?${query}`);
  await page.waitForFunction(() => window.__benchReport || window.__benchError, null, { timeout: timeoutMs });
  const error = await page.evaluate(() => window.__benchError);
  if (error) throw new Error(error);
  const report = await page.evaluate(() => window.__benchReport);
  for (const record of report.results) console.log(`  ${record.name}: median ${(record.medianNanoseconds / 1000).toFixed(1)} µs rsd ${(record.relativeStandardDeviation * 100).toFixed(1)}%${record.unstable ? " UNSTABLE" : ""}`);
  await context.close();

  // Cold start: fresh context per sample.
  const cold = [];
  for (const fixture of coldFixtures) {
    const samples = [];
    for (let i = 0; i < coldLaunches; i++) {
      const coldContext = await browser.newContext();
      const coldPage = await coldContext.newPage();
      const wall = performance.now();
      await coldPage.goto(`${origin}/browser/index.html?mode=cold&fixture=${fixture}`);
      await coldPage.waitForFunction(() => window.__coldReport || window.__benchError, null, { timeout: timeoutMs });
      const coldError = await coldPage.evaluate(() => window.__benchError);
      if (coldError) throw new Error(coldError);
      samples.push({ ...(await coldPage.evaluate(() => window.__coldReport)), pageWallMs: performance.now() - wall });
      await coldContext.close();
    }
    const median = (values) => { const s = [...values].sort((a, b) => a - b); const m = s.length >> 1; return s.length % 2 ? s[m] : (s[m - 1] + s[m]) / 2; };
    const keys = ["pageWallMs", "pageStartToBundleReadyMs", "runtimeCreateMs", "exportsReadyMs", "firstCompileMs", "firstCoreParseMs", "firstPublicParseMs"];
    const summary = { fixture, launches: coldLaunches, medians: Object.fromEntries(keys.map((k) => [k, Number(median(samples.map((s) => s[k])).toFixed(3))])), samples };
    cold.push(summary);
    console.log(`cold ${fixture}: ` + keys.map((k) => `${k}=${summary.medians[k].toFixed(1)}`).join(" "));
  }

  const output = {
    schemaVersion: 1,
    harness: "benchmarks/js/bench/browser.mjs",
    settings: report.settings,
    environment: captureEnvironment({ browser: { name: "chromium", version: browser.version(), userAgent: report.userAgent, hardwareConcurrency: report.hardwareConcurrency }, runtimeTimings: report.runtimeTimings, verifiedFixtures: report.verifiedFixtures, pageMemory: report.memory }),
    results: report.results,
    coldStart: cold,
    sink: report.sink,
  };
  const stamp = new Date().toISOString().replace(/[:.]/g, "-");
  fs.writeFileSync(path.join(resultsDirectory, `browser-${stamp}.json`), JSON.stringify(output, null, 2) + "\n");
  fs.writeFileSync(path.join(resultsDirectory, "browser-latest.json"), JSON.stringify(output, null, 2) + "\n");
  console.log(`Wrote ${report.results.length} results and ${cold.length} cold-start summaries.`);
} finally {
  await browser.close();
  server.close();
}
