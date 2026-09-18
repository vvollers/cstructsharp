#!/usr/bin/env node
/**
 * Onboarding check against the standalone WASM archive: extracts it under a nested URL path in a throwaway static
 * host, checks the packaged TypeScript declarations with a strict consumer, and runs the starter-page Playwright
 * spec (CSTRUCT_BROWSERS selects the engines).
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
  const web = path.join(repositoryRoot, "apps/explorer");
  const hostRoot = path.resolve(web, "artifacts/onboarding-host");
  assertCondition(path.dirname(hostRoot) === path.resolve(web, "artifacts"), "Unsafe browser test staging directory.");
  fs.rmSync(hostRoot, { recursive: true, force: true });
  fs.mkdirSync(hostRoot, { recursive: true });
  const bundle = path.join(hostRoot, "tools/binary");
  extractZip(path.resolve(options["archive-path"]), bundle);
  fs.copyFileSync(path.join(bundle, "serve.mjs"), path.join(hostRoot, "serve.mjs"));

  const types = runCommand(process.execPath, [path.join(repositoryRoot, "tools/packaging/test-public-types.mjs"), bundle], { cwd: web, allowFailure: true });
  process.stdout.write(`${types.stdout ?? ""}${types.stderr ?? ""}`);
  if (types.status !== 0) throw new Error("Packaged public TypeScript declarations failed.");
  const playwright = runCommand(process.execPath, [path.join(web, "node_modules/@playwright/test/cli.js"), "test", "--config", "playwright.starter.config.ts"], { cwd: web, allowFailure: true });
  process.stdout.write(`${playwright.stdout ?? ""}${playwright.stderr ?? ""}`);
  if (playwright.status !== 0) throw new Error("Packaged browser onboarding checks failed.");
});
