#!/usr/bin/env node
/**
 * Checks the browser wire contract baseline (contracts/api/browser/contract.json) against the sources that
 * implement it: the canonical TypeScript declarations, the apps' shared contract module, the managed bridge (its
 * complete export list, the envelope writer, options, and error codes), and the JavaScript runtime modules, which
 * must bind every export and take the contract version from the managed envelopes rather than state it. Exit code 1
 * names the first missing piece.
 *
 *   node tools/quality/browser-contract.mjs
 */
import fs from "node:fs";
import path from "node:path";
import { repositoryRoot } from "../lib/tooling.mjs";

/**
 * Reads a repository file as text.
 * @throws {Error} When the file is missing.
 */
const read = (relative) => {
  const file = path.join(repositoryRoot, relative);
  if (!fs.existsSync(file)) throw new Error(`Browser contract input is missing: ${relative}`);
  return fs.readFileSync(file, "utf8");
};

const baseline = JSON.parse(read("contracts/api/browser/contract.json"));
const declarations = read("packages/cstructsharp/index.d.ts");
const contract = read("apps/shared/src/wasm/contract.ts") + "\n" + declarations;
const boundary = read("src/CStructSharp.Wasm/CStructInteropBoundary.cs");
// The failure mapping that names every error code the envelopes report.
const errorCategories = read("src/CStructSharp.Wasm/InteropErrorCategories.cs");
const wasmDirectory = "src/CStructSharp.Wasm";
// Every C# file of the bridge, so an export added in any partial file is seen.
const wasmSources = fs
  .readdirSync(path.join(repositoryRoot, wasmDirectory))
  .filter((name) => name.endsWith(".cs"))
  .sort()
  .map((name) => read(`${wasmDirectory}/${name}`))
  .join("\n");
// The envelope writer names every envelope, error, and debug field as an escaped C# JSON literal (\"name\":).
const envelopeWriter = read("src/CStructSharp.Wasm/InteropEnvelope.cs");
const optionSource = read("src/CStructSharp.Wasm/InteropOptionsDto.cs");
// The modules that call the managed exports: the adapter on the page and the source worker.
const bindings = read("packages/cstructsharp/src/bootstrap.js") + "\n" + read("packages/cstructsharp/src/source-worker.js");
const packageModules = [
  "packages/cstructsharp/src/bootstrap.js",
  "packages/cstructsharp/src/cstructsharp-api.js",
  "packages/cstructsharp/src/cstructsharp-shared.js",
  "packages/cstructsharp/src/cstructsharp-wasm.js",
  "packages/cstructsharp/src/large-source.js",
  "packages/cstructsharp/src/main.js",
  "packages/cstructsharp/src/source-worker.js",
  "packages/cstructsharp/browser.js",
  "packages/cstructsharp/node.js",
  "packages/cstructsharp/runtime-loader.js",
];

/** Stops the check with an error message. */
const fail = (message) => {
  throw new Error(message);
};

if (baseline.schemaVersion !== 1 || baseline.name !== "browser") {
  fail("Browser baseline schema/name is not the supported browser contract.");
}
// The wire contract is versioned by contractVersion, independently of the package version.
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

// The JavaScript package states no contract version of its own: every envelope it returns carries the version the
// managed exports wrote (the declarations fix it as a literal type, checked above).
for (const module of packageModules) {
  const source = read(module);
  if (/INTEROP_CONTRACT_VERSION|contractVersion\s*[:=]\s*\d/.test(source)) {
    fail(`${module} states a contract version; take contractVersion from the managed envelope instead.`);
  }
}

// The baseline lists exactly the bridge's exports, and the JavaScript modules bind each of them.
const declaredExports = [...wasmSources.matchAll(/\[JSExport\]\s+public static [\w[\]]+ (\w+)\s*\(/g)].map((match) => match[1]);
for (const name of declaredExports) {
  if (!baseline.managedExports.includes(name)) {
    fail(`Managed browser export is not in the baseline: ${name}`);
  }
}
for (const name of baseline.managedExports) {
  if (!declaredExports.includes(name)) {
    fail(`Managed browser export is missing: ${name}`);
  }
  if (!new RegExp(`['"]${name}['"]`).test(bindings) && !new RegExp(`managed\\.${name}\\(`).test(bindings)) {
    fail(`JavaScript binding is missing managed export: ${name}`);
  }
}

for (const operation of baseline.operations) {
  if (!new RegExp(`['"]${operation}['"]`).test(contract)) {
    fail(`TypeScript operation is missing: ${operation}`);
  }
  if (!new RegExp(`['"]${operation}['"]`).test(wasmSources)) {
    fail(`Managed operation is missing: ${operation}`);
  }
}
// Operations the runtime modules consume themselves (the version and static-plan envelopes) never reach callers.
for (const operation of baseline.bridgeOperations) {
  if (!new RegExp(`['"]${operation}['"]`).test(wasmSources)) {
    fail(`Managed bridge operation is missing: ${operation}`);
  }
  if (!new RegExp(`['"]${operation}['"]`).test(bindings)) {
    fail(`JavaScript binding does not read bridge operation: ${operation}`);
  }
}

/** Whether the envelope writer writes the given JSON member name. */
const writes = (field) => envelopeWriter.includes(`\\"${field}\\":`);
for (const field of baseline.envelopeFields) {
  if (!writes(field)) {
    fail(`Managed envelope field is missing: ${field}`);
  }
  if (!new RegExp(`(?:^|[\\s{;])${field}\\??:`, "m").test(declarations)) {
    fail(`TypeScript envelope field is missing: ${field}`);
  }
}
for (const field of [...baseline.errorFields, ...baseline.debugFields]) {
  if (!writes(field)) {
    fail(`Managed error or debug field is missing: ${field}`);
  }
}

for (const field of baseline.optionFields) {
  if (!new RegExp(`^\\s+${field}\\?:`, "m").test(contract)) {
    fail(`TypeScript option is missing: ${field}`);
  }
  if (!new RegExp(`\\[JsonPropertyName\\("${field}"\\)\\]`).test(optionSource)) {
    fail(`Managed option is missing: ${field}`);
  }
}

for (const code of baseline.errorCodes) {
  if (!errorCategories.includes(`"${code}"`)) {
    fail(`Managed browser error code is missing: ${code}`);
  }
}

console.log(
  `Browser contract validated: v${version}, ${baseline.managedExports.length} exports, ${baseline.operations.length} operations, ${baseline.envelopeFields.length} envelope fields, ${baseline.optionFields.length} options, and ${baseline.errorCodes.length} error codes.`,
);
