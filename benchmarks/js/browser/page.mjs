// Browser benchmark page, driven by bench/browser.mjs through Playwright. Query parameters:
//   ?mode=bench&filter=<regex>&quick=1   — run the shared case groups and expose window.__benchReport
//   ?mode=cold&fixture=<id>              — measure runtime create / exports / first compile / first parse once
import { runCases, formatRecord, sinkValue } from "/bench/harness.mjs";
import { boundaryCases, coreCases, publicCases, streamCases, comparatorCases, verifyFixture } from "/bench/cases.mjs";

const params = new URLSearchParams(location.search);
const logElement = document.getElementById("log");
const log = (line) => { logElement.textContent += "\n" + line; };

function xorshiftBytes(seed, size) {
  let x = seed >>> 0 || 1;
  const bytes = new Uint8Array(size);
  for (let i = 0; i < size; i++) { x ^= x << 13; x >>>= 0; x ^= x >>> 17; x ^= x << 5; x >>>= 0; bytes[i] = x & 0xff; }
  return bytes;
}
const fixtureCache = new Map();
async function loadFixture(id) {
  if (fixtureCache.has(id)) return fixtureCache.get(id);
  const doc = await (await fetch(`/fixtures/cases/${id}.json`)).json();
  let bytes;
  if (!doc.bytes) bytes = new Uint8Array(0);
  else if (doc.bytes.kind === "hex") bytes = Uint8Array.from(doc.bytes.hex.match(/../g), (h) => parseInt(h, 16));
  else if (doc.bytes.kind === "file") bytes = new Uint8Array(await (await fetch(`/fixtures/${doc.bytes.file}`)).arrayBuffer());
  else bytes = xorshiftBytes(doc.bytes.seed, doc.bytes.size);
  const fixture = { document: doc, bytes };
  fixtureCache.set(id, fixture);
  return fixture;
}

async function loadBundle() {
  const t0 = performance.now();
  const { dotnet } = await import("/bundle/_framework/dotnet.js");
  const runtime = await dotnet.create();
  const t1 = performance.now();
  const exports = await runtime.getAssemblyExports("CStructSharpWeb.Wasm");
  const t2 = performance.now();
  runtime.setModuleImports("cstructsharp-source", { read: (source, offset, count) => source.read(offset, count) });
  const { createCStructSharpWasm } = await import("/bundle/bootstrap.js");
  const { createPublicApi } = await import("/bundle/cstructsharp-api.js");
  const wasm = createCStructSharpWasm(exports);
  window.CStructSharpWasm = wasm;
  const api = createPublicApi(async () => wasm);
  return { runtime, exports, managed: exports.CStructSharpWeb.Wasm.CStructExports, api, timings: { runtimeCreateMs: t1 - t0, exportsReadyMs: t2 - t1 } };
}

try {
  if (params.get("mode") === "cold") {
    const id = params.get("fixture") ?? "real-png";
    const t0 = performance.now();
    const bundle = await loadBundle();
    const t1 = performance.now();
    const { document: doc, bytes } = await loadFixture(id);
    const options = { pointerSize: doc.options.pointerSize, aligned: doc.options.aligned, littleEndian: doc.options.littleEndian, rootTypeName: doc.root };
    const optionsJson = JSON.stringify(options);
    bundle.managed.BenchCompile(doc.definition, optionsJson);
    const t2 = performance.now();
    const consumed = bundle.managed.BenchParseCore(bytes, doc.root, optionsJson);
    const t3 = performance.now();
    const result = await bundle.api.parse(doc.definition, bytes, options);
    const t4 = performance.now();
    window.__coldReport = {
      fixture: id,
      pageStartToBundleReadyMs: t1 - t0,
      runtimeCreateMs: bundle.timings.runtimeCreateMs,
      exportsReadyMs: bundle.timings.exportsReadyMs,
      firstCompileMs: t2 - t1,
      firstCoreParseMs: t3 - t2,
      firstPublicParseMs: t4 - t3,
      consumed,
      publicSuccess: result.Success,
    };
    log(JSON.stringify(window.__coldReport));
  } else {
    const bundle = await loadBundle();
    const copyCounter = { pages: 0, pageBytes: 0, bytesIn: 0, bytesOut: 0, jsonChars: 0 };
    const env = { host: "browser", loadFixture, managed: bundle.managed, api: bundle.api, runtime: bundle.runtime, copyCounter };
    const verified = [];
    for (const id of ["prim-le-record", "nested-x256", "array-u8-1024", "union-x1k", "strings-1024", "pointer-depth-8", "cond-if128", "real-png", "real-pe-exe"]) {
      await verifyFixture(env, id);
      verified.push(id);
    }
    log(`verified ${verified.length} fixtures`);
    const filter = params.get("filter") ? new RegExp(params.get("filter")) : null;
    const quick = params.get("quick") === "1";
    const options = quick
      ? { warmupTime: 150, batchTargetMs: 30, batches: 5, latencyTime: 300 }
      : { warmupTime: 600, batchTargetMs: 100, batches: 9, latencyTime: 1500 };
    const groups = [
      ["boundary", await boundaryCases(env)],
      ["core", await coreCases(env)],
      ["public", await publicCases(env)],
      ["stream", await streamCases(env)],
      ["comparator", await comparatorCases(env)],
    ];
    const records = [];
    for (const [group, cases] of groups) {
      const selected = filter ? cases.filter((c) => filter.test(c.name)) : cases;
      if (selected.length === 0) continue;
      log(`== ${group}`);
      const groupRecords = await runCases(selected, {
        ...options,
        allocatedBytes: () => bundle.managed.BenchAllocatedBytes(),
        onResult: (record) => log("  " + formatRecord(record)),
      });
      for (let i = 0; i < selected.length; i++) {
        Object.keys(copyCounter).forEach((key) => (copyCounter[key] = 0));
        const value = await selected[i].fn();
        groupRecords[i].copies = {
          pageReads: copyCounter.pages,
          pageBytes: copyCounter.pageBytes,
          inputBytes: selected[i].meta?.bytes ?? 0,
          outputChars: typeof value === "string" ? value.length : typeof value?.Data === "string" ? value.Data.length : value?.Data?.byteLength ?? null,
        };
        groupRecords[i].group = group;
      }
      records.push(...groupRecords);
    }
    window.__benchReport = {
      settings: options,
      runtimeTimings: bundle.timings,
      verifiedFixtures: verified,
      userAgent: navigator.userAgent,
      hardwareConcurrency: navigator.hardwareConcurrency,
      memory: performance.memory ? { usedJSHeapSize: performance.memory.usedJSHeapSize, totalJSHeapSize: performance.memory.totalJSHeapSize } : null,
      results: records,
      sink: sinkValue(),
    };
    log("done");
  }
} catch (error) {
  window.__benchError = error instanceof Error ? `${error.message}\n${error.stack}` : String(error);
  log("ERROR " + window.__benchError);
}
