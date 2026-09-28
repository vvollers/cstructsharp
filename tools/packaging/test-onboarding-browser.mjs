#!/usr/bin/env node
/**
 * Browser checks of the standalone WASM archive: extracts it under a nested URL path in a throwaway static host
 * (artifacts/onboarding-host) beside the package's bridge test page, checks the packaged TypeScript declarations
 * with a strict consumer, and runs the package's Playwright specs (packages/cstructsharp/tests/browser: the starter
 * pages and the bridge contract; CSTRUCT_BROWSERS selects the engines).
 *
 *   node tools/packaging/test-onboarding-browser.mjs --archive-path <cstructsharp-wasm.zip>
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";
import { openZip } from "../lib/zip.mjs";

const options = parseArguments(process.argv.slice(2), { "archive-path": "string" });
assertCondition(options["archive-path"], "Option --archive-path is required.");

function extractZip(archivePath, destination) {
  const archive = openZip(archivePath);
  for (const entry of archive.entries) {
    const target = path.resolve(destination, entry.name);
    if (!target.startsWith(`${path.resolve(destination)}${path.sep}`)) throw new Error(`Archive entry escapes the destination: ${entry.name}`);
    if (entry.name.endsWith("/")) {
      fs.mkdirSync(target, { recursive: true });
      continue;
    }
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, archive.read(entry.name));
  }
}

await main(() => {
  const tests = path.join(repositoryRoot, "packages/cstructsharp/tests");
  const hostRoot = path.join(repositoryRoot, "artifacts/onboarding-host");
  fs.rmSync(hostRoot, { recursive: true, force: true });
  fs.mkdirSync(hostRoot, { recursive: true });
  const bundle = path.join(hostRoot, "tools/binary");
  extractZip(path.resolve(options["archive-path"]), bundle);
  fs.copyFileSync(path.join(bundle, "serve.mjs"), path.join(hostRoot, "serve.mjs"));
  fs.copyFileSync(path.join(tests, "browser/bridge.html"), path.join(bundle, "bridge.html"));

  const types = runCommand(process.execPath, [path.join(repositoryRoot, "tools/packaging/test-public-types.mjs"), bundle], { cwd: repositoryRoot, allowFailure: true });
  process.stdout.write(`${types.stdout ?? ""}${types.stderr ?? ""}`);
  if (types.status !== 0) throw new Error("Packaged public TypeScript declarations failed.");
  const playwright = runCommand(process.execPath, [path.join(repositoryRoot, "node_modules/@playwright/test/cli.js"), "test", "--config", path.join(tests, "browser/playwright.config.mjs")], { cwd: repositoryRoot, allowFailure: true });
  process.stdout.write(`${playwright.stdout ?? ""}${playwright.stderr ?? ""}`);
  if (playwright.status !== 0) throw new Error("Packaged browser onboarding checks failed.");
});
