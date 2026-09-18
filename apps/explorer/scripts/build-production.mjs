import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { validateWasmPublication } from "../../../tools/packaging/wasm-publication.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
// The npm CLI that started this script when run through `npm run`, otherwise the `npm` on PATH.
const npmCli = process.env.npm_execpath;

function runScript(name) {
  const result = npmCli
    ? spawnSync(process.execPath, [npmCli, "run", name], {
    cwd: webRoot,
        stdio: "inherit",
        shell: false,
      })
    : spawnSync(process.platform === "win32" ? "npm.cmd" : "npm", ["run", name], {
        cwd: webRoot,
        stdio: "inherit",
        shell: process.platform === "win32",
      });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`${name} failed with exit code ${result.status}.`);
  }
}

// Publication and frontend copying are deliberately sequential. This prevents
// Vite from observing the atomic public/wasm swap halfway through a build.
runScript("build:wasm");
runScript("copy:wasm");
runScript("build:frontend");

const published = validateWasmPublication(path.join(webRoot, "public", "wasm"));
const bundled = validateWasmPublication(path.join(webRoot, "dist", "wasm"));
assert.deepEqual(
  bundled,
  published,
  "The production frontend did not embed the exact validated WASM publication.",
);
console.log(`Production build contains the exact ${bundled.totals.files}-file WASM publication.`);
