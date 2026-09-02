import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { validateWasmPublication } from "./wasm-publication.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const npmCli = process.env.npm_execpath;

if (!npmCli) {
  throw new Error("The production build must be started through npm.");
}

function runScript(name) {
  const result = spawnSync(process.execPath, [npmCli, "run", name], {
    cwd: webRoot,
    stdio: "inherit",
    shell: false,
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
runScript("build:frontend");

const published = validateWasmPublication(path.join(webRoot, "public", "wasm"));
const bundled = validateWasmPublication(path.join(webRoot, "dist", "wasm"));
assert.deepEqual(
  bundled,
  published,
  "The production frontend did not embed the exact validated WASM publication.",
);
console.log(`Production build contains the exact ${bundled.totals.files}-file WASM publication.`);
