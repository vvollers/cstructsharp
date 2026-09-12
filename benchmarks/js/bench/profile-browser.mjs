#!/usr/bin/env node
// CPU-profiles the JS/WASM parse of a real PNG in headless Chromium through the Chrome DevTools Protocol and
// prints the top inclusive frames. Two profiles are taken:
//   main-direct : ParseSource export called on the page thread (managed parse + JSON projection run in-page, so
//                 interpreter/WASM frames are visible),
//   main-public : the public api.parse (worker-based); only the page-thread share (messaging, JSON.parse) shows.
// Usage: node benchmarks/js/bench/profile-browser.mjs [fixtureId] [iterations] [outdir]
import fs from "node:fs";
import http from "node:http";
import path from "node:path";
import { chromium } from "playwright";
import { repositoryRoot } from "./fixtures.mjs";

const fixtureId = process.argv[2] ?? "real-png";
const iterations = Number(process.argv[3] ?? 2000);
const outdir = path.resolve(process.argv[4] ?? "artifacts/profiles");
fs.mkdirSync(outdir, { recursive: true });
const jsRoot = path.join(repositoryRoot, "benchmarks/js");
const routes = {
  "/bundle/": path.join(repositoryRoot, "artifacts/js-bench/bundle"),
  "/fixtures/": path.join(repositoryRoot, "benchmarks/fixtures"),
  "/bench/": path.join(jsRoot, "bench"),
  "/browser/": path.join(jsRoot, "browser"),
  "/node_modules/": path.join(jsRoot, "node_modules"),
};
const mime = { ".html": "text/html", ".js": "text/javascript", ".mjs": "text/javascript", ".json": "application/json", ".wasm": "application/wasm" };
const server = http.createServer((request, response) => {
  const url = new URL(request.url, "http://localhost");
  const prefix = Object.keys(routes).find((p) => url.pathname.startsWith(p));
  const file = prefix ? path.join(routes[prefix], decodeURIComponent(url.pathname.slice(prefix.length))) : null;
  if (!file || !fs.existsSync(file) || fs.statSync(file).isDirectory()) { response.writeHead(404).end(); return; }
  response.writeHead(200, { "Content-Type": mime[path.extname(file)] ?? "application/octet-stream", "Cache-Control": "no-store" });
  fs.createReadStream(file).pipe(response);
});
await new Promise((resolve) => server.listen(0, "127.0.0.1", resolve));
const origin = `http://127.0.0.1:${server.address().port}`;

// Resolve wasm-function[N] through the symbol map emitted by -p:WasmEmitSymbolMap=true (build-wasm.mjs passes it
// through); the map does not change the wasm binary, only adds the sidecar file.
const symbolFile = path.join(repositoryRoot, "artifacts/js-bench/bundle/_framework/dotnet.native.js.symbols");
const wasmSymbols = new Map();
if (fs.existsSync(symbolFile)) {
  for (const line of fs.readFileSync(symbolFile, "utf8").split("\n")) {
    const m = line.match(/^(\d+):(.*)$/);
    if (m) wasmSymbols.set(m[1], m[2].replace(/\\([0-9a-f]{2})/g, (_, h) => String.fromCharCode(parseInt(h, 16))));
  }
}
function resolveName(name) {
  const m = name.match(/^wasm-function\[(\d+)\]$/);
  return m && wasmSymbols.has(m[1]) ? `${wasmSymbols.get(m[1])} (wasm#${m[1]})` : name;
}

function summarize(profile) {
  // Inclusive time per function: walk the node tree; each sample's time is attributed to the node and all ancestors.
  const nodes = new Map(profile.nodes.map((n) => [n.id, n]));
  const parent = new Map();
  for (const node of profile.nodes) for (const child of node.children ?? []) parent.set(child, node.id);
  const selfTime = new Map();
  const total = profile.timeDeltas.reduce((a, b) => a + b, 0);
  profile.samples.forEach((id, i) => selfTime.set(id, (selfTime.get(id) ?? 0) + (profile.timeDeltas[i] ?? 0)));
  const inclusive = new Map();
  const selfByName = new Map();
  const label = (n) => `${resolveName(n.callFrame.functionName || "(anonymous)")} [${path.basename(n.callFrame.url || "") || "native"}]`;
  for (const [id, time] of selfTime) {
    const seen = new Set();
    let current = id;
    selfByName.set(label(nodes.get(id)), (selfByName.get(label(nodes.get(id))) ?? 0) + time);
    while (current !== undefined) {
      const name = label(nodes.get(current));
      if (!seen.has(name)) { inclusive.set(name, (inclusive.get(name) ?? 0) + time); seen.add(name); }
      current = parent.get(current);
    }
  }
  const rows = [...inclusive.entries()].sort((a, b) => b[1] - a[1]).slice(0, 25).map(([name, time]) => ({ name, inclusivePercent: (100 * time / total).toFixed(1), selfPercent: (100 * (selfByName.get(name) ?? 0) / total).toFixed(1) }));
  return { totalMicroseconds: total, rows };
}

const browser = await chromium.launch({ headless: true });
try {
  const page = await browser.newPage();
  await page.goto(`${origin}/browser/index.html?mode=cold&fixture=${fixtureId}`);
  await page.waitForFunction(() => window.__coldReport || window.__benchError);
  const client = await page.context().newCDPSession(page);
  await client.send("Profiler.enable");
  await client.send("Profiler.setSamplingInterval", { interval: 100 });
  const results = {};
  for (const mode of ["main-direct", "main-public"]) {
    await client.send("Profiler.start");
    const summary = await page.evaluate(async ({ fixtureId, iterations, mode }) => {
      const doc = await (await fetch(`/fixtures/cases/${fixtureId}.json`)).json();
      const bytes = Uint8Array.from(doc.bytes.hex.match(/../g), (h) => parseInt(h, 16));
      const options = { pointerSize: doc.options.pointerSize, aligned: doc.options.aligned, littleEndian: doc.options.littleEndian, rootTypeName: doc.root, ...(doc.readOptions?.addressingMode ? { addressingMode: doc.readOptions.addressingMode } : {}) };
      const managed = window.CStructSharpWasm.exports.CStructSharpWeb.Wasm.CStructExports;
      const { createPublicApi } = await import("/bundle/cstructsharp-api.js");
      const api = createPublicApi(async () => window.CStructSharpWasm);
      const source = { size: bytes.byteLength, read: (o, c) => bytes.subarray(o, o + c) };
      let sink = 0;
      const start = performance.now();
      for (let i = 0; i < iterations; i++) {
        if (mode === "main-direct") sink += managed.ParseSource(doc.definition, source, JSON.stringify(options), false).length;
        else sink += (await api.parse(doc.definition, bytes, options)).Data.length;
      }
      return { iterations, elapsedMs: performance.now() - start, sink };
    }, { fixtureId, iterations: mode === "main-public" ? Math.max(50, Math.floor(iterations / 10)) : iterations, mode });
    const { profile } = await client.send("Profiler.stop");
    fs.writeFileSync(path.join(outdir, `browser-${mode}-${fixtureId}.cpuprofile`), JSON.stringify(profile));
    results[mode] = { ...summary, ...summarize(profile) };
    console.log(`\n== ${mode}: ${summary.iterations} iterations in ${summary.elapsedMs.toFixed(0)} ms (${(summary.elapsedMs * 1000 / summary.iterations).toFixed(0)} µs/op)`);
    for (const row of results[mode].rows.slice(0, 15)) console.log(`  ${row.inclusivePercent.padStart(5)}% incl ${row.selfPercent.padStart(5)}% self  ${row.name}`);
  }
  fs.writeFileSync(path.join(outdir, `browser-${fixtureId}-summary.json`), JSON.stringify(results, null, 2));
} finally {
  await browser.close();
  server.close();
}
