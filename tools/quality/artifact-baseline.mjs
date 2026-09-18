#!/usr/bin/env node
/**
 * Measures artifact directories (raw and gzip-equivalent sizes, per-extension summary, largest files) into one
 * JSON report for the release budgets.
 *
 *   node tools/quality/artifact-baseline.mjs --output-path <json> [--wasm-directory <dir>]
 *     [--frontend-directory <dir>] [--package-directory <dir>]
 */
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { gzipSync } from "node:zlib";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";

const options = parseArguments(process.argv.slice(2), { "wasm-directory": "string", "frontend-directory": "string", "package-directory": "string", "output-path": "string" });
assertCondition(options["output-path"], "Option --output-path is required.");

function listFiles(directory) {
  const files = [];
  for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
    const full = path.join(directory, entry.name);
    if (entry.isDirectory()) files.push(...listFiles(full));
    else if (entry.isFile()) files.push(full);
  }
  return files.sort();
}

function measureDirectory(directory) {
  const root = path.resolve(directory);
  const entries = listFiles(root).map((file) => {
    const data = fs.readFileSync(file);
    const extension = path.extname(file);
    return {
      relativePath: path.relative(root, file).replaceAll("\\", "/"),
      bytes: data.length,
      gzipBytes: gzipSync(data, { level: 9 }).length,
      extension: extension === "" ? "(none)" : extension.toLowerCase(),
    };
  });
  const byExtension = [...new Set(entries.map((entry) => entry.extension))].sort().map((extension) => {
    const group = entries.filter((entry) => entry.extension === extension);
    return { extension, files: group.length, bytes: group.reduce((sum, entry) => sum + entry.bytes, 0), gzipBytes: group.reduce((sum, entry) => sum + entry.gzipBytes, 0) };
  });
  return {
    path: path.relative(repositoryRoot, root).replaceAll("\\", "/"),
    files: entries.length,
    bytes: entries.reduce((sum, entry) => sum + entry.bytes, 0),
    gzipBytes: entries.reduce((sum, entry) => sum + entry.gzipBytes, 0),
    byExtension,
    largestFiles: [...entries].sort((a, b) => b.bytes - a.bytes).slice(0, 25),
  };
}

const versionOf = (command) => {
  const result = runCommand(command, ["--version"], { allowFailure: true });
  return result.error || result.status !== 0 ? null : result.stdout.trim();
};

await main(() => {
  assertCondition(options["wasm-directory"] || options["frontend-directory"] || options["package-directory"], "Specify at least one directory to measure.");
  const wasm = options["wasm-directory"] ? measureDirectory(options["wasm-directory"]) : null;
  const frontend = options["frontend-directory"] ? measureDirectory(options["frontend-directory"]) : null;
  const packages = options["package-directory"] ? measureDirectory(options["package-directory"]) : null;
  const report = {
    schemaVersion: 1,
    generatedAtUtc: new Date().toISOString(),
    revision: runCommand("git", ["-C", repositoryRoot, "rev-parse", "HEAD"], { allowFailure: true }).stdout.trim() || null,
    worktreeDirty: runCommand("git", ["-C", repositoryRoot, "status", "--porcelain"], { allowFailure: true }).stdout.trim().length > 0,
    compression: "sum of each file compressed independently with gzip level 9",
    environment: {
      os: `${os.type()} ${os.release()}`,
      processArchitecture: process.arch,
      dotnetSdk: versionOf("dotnet"),
      node: process.version,
      npm: versionOf(process.platform === "win32" ? "npm.cmd" : "npm"),
    },
    wasm,
    frontend,
    packages,
  };
  fs.mkdirSync(path.dirname(path.resolve(options["output-path"])), { recursive: true });
  fs.writeFileSync(options["output-path"], `${JSON.stringify(report, null, 2)}\n`);
  console.log(`Artifact baseline written to ${options["output-path"]}`);
  if (wasm) console.log(`WASM: ${wasm.files} files, ${wasm.bytes} bytes, ${wasm.gzipBytes} gzip bytes`);
  if (frontend) console.log(`Frontend: ${frontend.files} files, ${frontend.bytes} bytes, ${frontend.gzipBytes} gzip bytes`);
  if (packages) console.log(`Packages: ${packages.files} files, ${packages.bytes} bytes, ${packages.gzipBytes} gzip bytes`);
});
