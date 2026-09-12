// Loads the benchmark bundle in Node the way the packaged Node loader does, exposing runtime, exports, and public API.
import path from "node:path";
import { pathToFileURL } from "node:url";
import { repositoryRoot } from "./fixtures.mjs";

export const bundleDirectory = path.join(repositoryRoot, "artifacts/js-bench/bundle");

export async function loadBundle() {
  const url = (file) => pathToFileURL(path.join(bundleDirectory, file)).href;
  const started = performance.now();
  const { dotnet } = await import(url("_framework/dotnet.js"));
  const runtime = await dotnet.create();
  const runtimeReady = performance.now();
  const exports = await runtime.getAssemblyExports("CStructSharpWeb.Wasm");
  const exportsReady = performance.now();
  const managed = exports.CStructSharpWeb.Wasm.CStructExports;
  runtime.setModuleImports("cstructsharp-source", { read: (source, offset, count) => source.read(offset, count) });
  const { createCStructSharpWasm } = await import(url("bootstrap.js"));
  const { createPublicApi } = await import(url("cstructsharp-api.js"));
  const wasm = createCStructSharpWasm(exports);
  // The source adapter (large-source.js) resolves the worker relative to this global, as main.js sets it.
  globalThis.CStructSharpWasm = wasm;
  const api = createPublicApi(async () => wasm);
  return {
    runtime,
    exports,
    managed,
    api,
    timings: { runtimeCreateMs: runtimeReady - started, exportsReadyMs: exportsReady - runtimeReady },
  };
}
