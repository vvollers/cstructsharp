import fs from "node:fs";
import path from "node:path";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { gzipSync } from "node:zlib";

import { validateWasmPublication } from "./wasm-publication.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const repositoryRoot = path.resolve(webRoot, "..");
const baselinePath = path.join(
  repositoryRoot,
  "artifacts",
  "baseline",
  "web-final-entry-artifacts.json",
);
const outputPath = path.join(repositoryRoot, "artifacts", "baseline", "web-final-artifacts.json");
const budgetPath = path.join(repositoryRoot, "docs", "performance", "budgets", "web-rc1.json");

function listFiles(root) {
  const files = [];
  function visit(directory) {
    for (const entry of fs
      .readdirSync(directory, { withFileTypes: true })
      .sort((left, right) => left.name.localeCompare(right.name))) {
      const absolute = path.join(directory, entry.name);
      if (entry.isDirectory()) {
        visit(absolute);
      } else if (entry.isFile()) {
        files.push(absolute);
      }
    }
  }
  visit(root);
  return files;
}

function measure(root) {
  const files = listFiles(root).map((absolute) => {
    const content = fs.readFileSync(absolute);
    return {
      relativePath: path.relative(root, absolute).replaceAll(path.sep, "/"),
      bytes: content.byteLength,
      gzipBytes: gzipSync(content, { level: 9 }).byteLength,
    };
  });
  return {
    path: path.relative(repositoryRoot, root).replaceAll(path.sep, "/"),
    files: files.length,
    bytes: files.reduce((total, file) => total + file.bytes, 0),
    gzipBytes: files.reduce((total, file) => total + file.gzipBytes, 0),
    largestFiles: [...files].sort((left, right) => right.bytes - left.bytes).slice(0, 12),
  };
}

function requireBudget(name, actual, maximum) {
  if (actual > maximum) {
    throw new Error(`${name} is ${actual} bytes, above the ${maximum}-byte budget.`);
  }
}

const baseline = JSON.parse(fs.readFileSync(baselinePath, "utf8"));
const budgetPolicy = JSON.parse(fs.readFileSync(budgetPath, "utf8"));
const wasm = measure(path.join(webRoot, "public", "wasm"));
const frontend = measure(path.join(webRoot, "dist"));
const mainJavaScript = frontend.largestFiles
  .concat(
    listFiles(path.join(webRoot, "dist", "assets")).map((absolute) => {
      const content = fs.readFileSync(absolute);
      return {
        relativePath: path.relative(path.join(webRoot, "dist"), absolute).replaceAll(path.sep, "/"),
        bytes: content.byteLength,
        gzipBytes: gzipSync(content, { level: 9 }).byteLength,
      };
    }),
  )
  .filter((file) => /^assets\/index-.*\.js$/.test(file.relativePath))
  .sort((left, right) => right.bytes - left.bytes)[0];

if (!mainJavaScript) {
  throw new Error("The production frontend has no main JavaScript asset.");
}

validateWasmPublication(path.join(webRoot, "public", "wasm"));
validateWasmPublication(path.join(webRoot, "dist", "wasm"));

const budgets = budgetPolicy.maximums;
requireBudget("WASM file count", wasm.files, budgets.wasmFiles);
requireBudget("WASM raw payload", wasm.bytes, budgets.wasmBytes);
requireBudget("WASM gzip payload", wasm.gzipBytes, budgets.wasmGzipBytes);
requireBudget("Frontend raw payload", frontend.bytes, budgets.frontendBytes);
requireBudget("Frontend gzip payload", frontend.gzipBytes, budgets.frontendGzipBytes);
requireBudget("Main JavaScript", mainJavaScript.bytes, budgets.mainJavaScriptBytes);
requireBudget("Main JavaScript gzip", mainJavaScript.gzipBytes, budgets.mainJavaScriptGzipBytes);

for (const [name, current, previous] of [
  ["WASM raw payload", wasm.bytes, baseline.wasm.bytes],
  ["WASM gzip payload", wasm.gzipBytes, baseline.wasm.gzipBytes],
  ["Frontend raw payload", frontend.bytes, baseline.frontend.bytes],
  ["Frontend gzip payload", frontend.gzipBytes, baseline.frontend.gzipBytes],
  ["Main JavaScript", mainJavaScript.bytes, baseline.mainJavaScript.bytes],
  ["Main JavaScript gzip", mainJavaScript.gzipBytes, baseline.mainJavaScript.gzipBytes],
]) {
  if (current > previous) {
    throw new Error(`${name} regressed from ${previous} to ${current} bytes.`);
  }
}

const artifact = {
  schemaVersion: 1,
  generatedAtUtc: new Date().toISOString(),
  revision: execFileSync("git", ["rev-parse", "HEAD"], {
    cwd: repositoryRoot,
    encoding: "utf8",
  }).trim(),
  worktreeDirty:
    execFileSync("git", ["status", "--porcelain"], {
      cwd: repositoryRoot,
      encoding: "utf8",
    }).trim().length > 0,
  compression: "sum of each file compressed independently with gzip level 9",
  baseline: {
    path: path.relative(repositoryRoot, baselinePath).replaceAll(path.sep, "/"),
    wasm: {
      files: baseline.wasm.files,
      bytes: baseline.wasm.bytes,
      gzipBytes: baseline.wasm.gzipBytes,
    },
    frontend: {
      files: baseline.frontend.files,
      bytes: baseline.frontend.bytes,
      gzipBytes: baseline.frontend.gzipBytes,
    },
    mainJavaScript: baseline.mainJavaScript,
  },
  current: {
    wasm,
    frontend,
    mainJavaScript,
    removedClangFormatBytes: baseline.clangFormat?.bytes ?? null,
  },
  deltas: {
    wasmFiles: wasm.files - baseline.wasm.files,
    wasmBytes: wasm.bytes - baseline.wasm.bytes,
    wasmGzipBytes: wasm.gzipBytes - baseline.wasm.gzipBytes,
    frontendFiles: frontend.files - baseline.frontend.files,
    frontendBytes: frontend.bytes - baseline.frontend.bytes,
    frontendGzipBytes: frontend.gzipBytes - baseline.frontend.gzipBytes,
    mainJavaScriptBytes: mainJavaScript.bytes - baseline.mainJavaScript.bytes,
    mainJavaScriptGzipBytes: mainJavaScript.gzipBytes - baseline.mainJavaScript.gzipBytes,
  },
  budgets,
};

fs.mkdirSync(path.dirname(outputPath), { recursive: true });
fs.writeFileSync(outputPath, `${JSON.stringify(artifact, null, 2)}\n`, "utf8");
console.log(
  `Web artifacts pass: WASM ${wasm.bytes}/${wasm.gzipBytes}, frontend ${frontend.bytes}/${frontend.gzipBytes}, main JS ${mainJavaScript.bytes}/${mainJavaScript.gzipBytes} bytes raw/gzip.`,
);
console.log(`Artifact evidence: ${outputPath}`);
