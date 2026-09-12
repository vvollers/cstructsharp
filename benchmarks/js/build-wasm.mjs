#!/usr/bin/env node
// Publishes the WASM project with the benchmark-only exports into artifacts/js-bench/AppBundle, copies the bridge
// modules next to it (as packaging does), and records SHA-256 hashes of every bundle file for provenance.
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const here = path.dirname(fileURLToPath(import.meta.url));
const repositoryRoot = path.resolve(here, "../..");
const project = path.join(repositoryRoot, "src/CStructSharp.Wasm/CStructSharpWeb.Wasm.csproj");
const targets = path.join(here, "wasm/Benchmark.targets");
const appBundle = path.join(repositoryRoot, "src/CStructSharp.Wasm/bin/Release/net10.0/browser-wasm/AppBundle");
const output = path.join(repositoryRoot, "artifacts/js-bench");
const bundle = path.join(output, "bundle");

const extra = process.argv.slice(2); // e.g. -p:RunAOTCompilation=true for the E3.1 experiment
const result = spawnSync(
  "dotnet",
  [
    "publish", project, "-c", "Release", "--nologo",
    "-p:WasmDebugLevel=0", "-p:WasmEmitSourceMap=false",
    `-p:CustomAfterMicrosoftCommonTargets=${targets}`,
    "-p:RunAnalyzers=false",
    ...extra,
  ],
  { cwd: repositoryRoot, stdio: "inherit" },
);
if (result.status !== 0) process.exit(result.status ?? 1);

// Stage the AppBundle (the layout the packaging script consumes) into an isolated benchmark directory.
fs.rmSync(bundle, { recursive: true, force: true });
fs.mkdirSync(bundle, { recursive: true });
fs.cpSync(path.join(appBundle, "_framework"), path.join(bundle, "_framework"), { recursive: true });
for (const file of fs.readdirSync(appBundle)) {
  const source = path.join(appBundle, file);
  if (fs.statSync(source).isFile()) fs.copyFileSync(source, path.join(bundle, file));
}

for (const file of ["bootstrap.js", "large-source.js", "source-worker.js", "cstructsharp-api.js", "main.js"]) {
  fs.copyFileSync(path.join(repositoryRoot, "src/CStructSharp.Wasm", file), path.join(bundle, file));
}

const hashes = {};
let totalBytes = 0;
for (const entry of fs.readdirSync(path.join(bundle, "_framework"))) {
  const file = path.join(bundle, "_framework", entry);
  const bytes = fs.readFileSync(file);
  totalBytes += bytes.length;
  hashes[`_framework/${entry}`] = { sha256: crypto.createHash("sha256").update(bytes).digest("hex"), bytes: bytes.length };
}
const manifest = {
  builtAtUtc: new Date().toISOString(),
  extraProperties: extra,
  bundle: path.relative(repositoryRoot, bundle),
  totalFrameworkBytes: totalBytes,
  bundleSha256: crypto.createHash("sha256").update(Object.keys(hashes).sort().map((k) => `${k}:${hashes[k].sha256}`).join("\n")).digest("hex"),
  files: hashes,
};
fs.writeFileSync(path.join(output, "bundle-manifest.json"), JSON.stringify(manifest, null, 2) + "\n");
console.log(`Bundle at ${manifest.bundle}: ${Object.keys(hashes).length} framework files, ${totalBytes} bytes, sha256 ${manifest.bundleSha256.slice(0, 16)}…`);
