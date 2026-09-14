import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";
import { validateWasmPublication } from "./wasm-publication.mjs";

const root = fileURLToPath(new URL("../../", import.meta.url));
const policy = JSON.parse(fs.readFileSync(path.join(root, "contracts/performance/web-rc1.json"), "utf8"));
function measure(relative) {
  const directory = path.join(root, relative);
  const entries = fs.readdirSync(directory, { recursive: true, withFileTypes: true })
    .filter((entry) => entry.isFile()).map((entry) => {
      const file = path.join(entry.parentPath, entry.name);
      const data = fs.readFileSync(file);
      return { path: path.relative(directory, file).replaceAll("\\", "/"), bytes: data.length, gzipBytes: gzipSync(data, { level: 9 }).length };
    });
  return { files: entries.length, bytes: entries.reduce((sum, file) => sum + file.bytes, 0), gzipBytes: entries.reduce((sum, file) => sum + file.gzipBytes, 0), entries };
}
validateWasmPublication(path.join(root, "artifacts/wasm"));
const wasm = measure("artifacts/wasm");
const frontend = measure("apps/workshop/dist");
const main = frontend.entries.filter((entry) => /^assets\/index-.*\.js$/.test(entry.path)).sort((a, b) => b.bytes - a.bytes)[0];
if (!main) throw new Error("Build the workshop before measuring its entry bundle.");
const values = { wasmFiles: wasm.files, wasmBytes: wasm.bytes, wasmGzipBytes: wasm.gzipBytes, frontendBytes: frontend.bytes, frontendGzipBytes: frontend.gzipBytes, mainJavaScriptBytes: main.bytes, mainJavaScriptGzipBytes: main.gzipBytes };
const exceeded = Object.entries(values).filter(([key, value]) => value > policy.maximums[key]).map(([key, value]) => `${key}: ${value} > ${policy.maximums[key]}`);
const report = { schemaVersion: 1, policy: policy.name, values, maximums: policy.maximums, exceeded };
fs.mkdirSync(path.join(root, "artifacts/performance"), { recursive: true });
fs.writeFileSync(path.join(root, "artifacts/performance/web.json"), `${JSON.stringify(report, null, 2)}\n`);
console.log(JSON.stringify(report, null, 2));
// Historical frontend budgets remain an explicit manual check. Publication size
// and browser startup are enforced by their existing automated gates.
if (process.argv.includes("--check") && exceeded.length) throw new Error(exceeded.join("\n"));
