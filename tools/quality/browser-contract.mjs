#!/usr/bin/env node
/**
 * Checks the browser wire contract baseline (contracts/api/browser-rc1/contract.json) against the sources that
 * implement it: the canonical TypeScript declarations, the explorer's contract module, the managed bridge, and
 * the JavaScript bootstrap. Exit code 1 names the first missing piece.
 *
 *   node tools/quality/browser-contract.mjs
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const read = (relative) => {
  const file = path.join(root, relative);
  if (!fs.existsSync(file)) throw new Error(`Browser contract input is missing: ${relative}`);
  return fs.readFileSync(file, "utf8");
};

const baseline = JSON.parse(read("contracts/api/browser-rc1/contract.json"));
const declarations = read("packages/cstructsharp/index.d.ts");
const contract = read("apps/workshop/src/wasm/cstruct-contract.ts") + "\n" + declarations;
const boundary = read("src/CStructSharp.Wasm/CStructInteropBoundary.cs");
const exports = read("src/CStructSharp.Wasm/CStructExports.cs") + "\n" + read("src/CStructSharp.Wasm/StaticPlanExport.cs");
const bootstrap = read("src/CStructSharp.Wasm/bootstrap.js");
const dtoSource = [
  "src/CStructSharp.Wasm/InteropOptionsDto.cs",
  "src/CStructSharp.Wasm/InteropResultDto.cs",
  "src/CStructSharp.Wasm/ErrorDetailsDto.cs",
  "src/CStructSharp.Wasm/DebugDataDto.cs",
  "src/CStructSharp.Wasm/CStructJsonContext.cs",
]
  .map(read)
  .join("\n");
const managedSources = exports + boundary + dtoSource;

const escape = (text) => text.replace(/[.*+?^${}()|[\]\\]/g, "\\$&");
const fail = (message) => {
  throw new Error(message);
};

if (baseline.schemaVersion !== 1 || baseline.name !== "browser-rc1") {
  fail("Browser baseline schema/name is not the supported browser-rc1 revision.");
}
// packageVersion records the historical freeze. Later package releases can retain the same wire contract;
// compare the actual version, fields, and exports below rather than requiring the old release number.
const version = baseline.contractVersion;
if (!new RegExp(`INTEROP_CONTRACT_VERSION\\s*=\\s*${version}\\s+as const`).test(contract)) {
  fail("TypeScript contract version differs from the browser baseline.");
}
if (!new RegExp(`contractVersion:\\s*${version};`).test(declarations)) {
  fail("Canonical declarations' envelope version differs from the browser baseline.");
}
if (!new RegExp(`InteropContractVersion\\s*=\\s*${version}\\s*;`).test(boundary)) {
  fail("Managed contract version differs from the browser baseline.");
}

for (const name of baseline.managedExports) {
  if (!new RegExp(`\\[JSExport\\]\\s+public static (?:string|byte\\[\\]) ${name}\\s*\\(`, "s").test(exports)) {
    fail(`Managed browser export is missing: ${name}`);
  }
  if (!new RegExp(`['"]${name}['"]`).test(bootstrap) && !new RegExp(`managed\\.${name}\\(`).test(bootstrap)) {
    fail(`Bootstrap binding is missing managed export: ${name}`);
  }
}

for (const operation of baseline.operations) {
  if (!new RegExp(`['"]${operation}['"]`).test(contract)) {
    fail(`TypeScript operation is missing: ${operation}`);
  }
  // The JSON-envelope operations name themselves in managed code; serialize/update report failure by throwing
  // and are identified by their export check above.
  if (!["serialize", "update"].includes(operation) && !new RegExp(`['"]${operation}['"]`).test(managedSources)) {
    fail(`Managed operation is missing: ${operation}`);
  }
}

for (const field of baseline.envelopeFields) {
  if (!new RegExp(`\\[JsonPropertyName\\("${field}"\\)\\]`).test(dtoSource)) {
    fail(`Managed envelope field is missing: ${field}`);
  }
  if (!new RegExp(`(?:^|[\\s{;])${field}\\??:`, "m").test(declarations)) {
    fail(`TypeScript envelope field is missing: ${field}`);
  }
}

for (const field of baseline.optionFields) {
  if (!new RegExp(`^\\s+${field}\\?:`, "m").test(contract)) {
    fail(`TypeScript option is missing: ${field}`);
  }
  if (!new RegExp(`\\[JsonPropertyName\\("${field}"\\)\\]`).test(dtoSource)) {
    fail(`Managed option is missing: ${field}`);
  }
}

for (const code of baseline.errorCodes) {
  if (!boundary.includes(`"${code}"`)) {
    fail(`Managed browser error code is missing: ${code}`);
  }
}

console.log(
  `Browser contract validated: v${version}, ${baseline.managedExports.length} exports, ${baseline.operations.length} operations, ${baseline.envelopeFields.length} envelope fields, ${baseline.optionFields.length} options, and ${baseline.errorCodes.length} error codes.`,
);
