/**
 * Measures the WASM publication (artifacts/wasm) and the explorer build (apps/explorer/dist): raw and gzip sizes and
 * the main JavaScript bundle, compared with contracts/performance/web-size-budget.json. It writes the report to
 * artifacts/performance/web.json and prints it; `--check` fails when a budget is exceeded.
 *
 *   node tools/packaging/measure-web-artifacts.mjs [--check]
 */
import fs from "node:fs";
import path from "node:path";
import { measureDirectory } from "../lib/files.mjs";
import { parseArguments, repositoryRoot as root } from "../lib/tooling.mjs";
import { validateWasmPublication } from "./wasm-publication.mjs";

const { check } = parseArguments(process.argv.slice(2), { check: "flag" }, { defaults: { check: false } });
const policy = JSON.parse(fs.readFileSync(path.join(root, "contracts/performance/web-size-budget.json"), "utf8"));
/** Measures a repository-relative directory (see measureDirectory). */
const measure = (relative) => measureDirectory(path.join(root, relative));
validateWasmPublication(path.join(root, "artifacts/wasm"));
const wasm = measure("artifacts/wasm");
const frontend = measure("apps/explorer/dist");
const main = frontend.entries.filter((entry) => /^assets\/index-.*\.js$/.test(entry.path)).sort((a, b) => b.bytes - a.bytes)[0];
if (!main) throw new Error("Build the explorer before measuring its entry bundle.");
const values = { wasmFiles: wasm.files, wasmBytes: wasm.bytes, wasmGzipBytes: wasm.gzipBytes, frontendBytes: frontend.bytes, frontendGzipBytes: frontend.gzipBytes, mainJavaScriptBytes: main.bytes, mainJavaScriptGzipBytes: main.gzipBytes };
const exceeded = Object.entries(values).filter(([key, value]) => value > policy.maximums[key]).map(([key, value]) => `${key}: ${value} > ${policy.maximums[key]}`);
const report = { schemaVersion: 1, policy: policy.name, values, maximums: policy.maximums, exceeded };
fs.mkdirSync(path.join(root, "artifacts/performance"), { recursive: true });
fs.writeFileSync(path.join(root, "artifacts/performance/web.json"), `${JSON.stringify(report, null, 2)}\n`);
console.log(JSON.stringify(report, null, 2));
// The frontend budgets are a manual check (--check). Publication size and browser startup are enforced by their
// automated gates.
if (check && exceeded.length) throw new Error(exceeded.join("\n"));
