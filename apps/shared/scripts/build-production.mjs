/**
 * Production build of the calling app (npm run build from an app directory): stages the validated WASM publication,
 * builds the frontend, and checks that dist/ embeds exactly that publication. The WASM itself is published once by
 * npm run build:wasm at the repository root, before either app is built.
 *
 *   node ../shared/scripts/build-production.mjs
 */
import assert from "node:assert/strict";
import { spawnSync } from "node:child_process";
import path from "node:path";

import { validateWasmPublication } from "../../../tools/packaging/wasm-publication.mjs";

const appRoot = process.cwd();
// The npm CLI that started this script when run through `npm run`, otherwise the `npm` on PATH.
const npmCli = process.env.npm_execpath;

/**
 * Runs one of the app's npm scripts and fails the build when it fails.
 * @param {string} name The script name.
 * @throws {Error} When the script cannot start or exits with a nonzero code.
 */
function runScript(name) {
  const result = npmCli
    ? spawnSync(process.execPath, [npmCli, "run", name], {
        cwd: appRoot,
        stdio: "inherit",
        shell: false,
      })
    : spawnSync(process.platform === "win32" ? "npm.cmd" : "npm", ["run", name], {
        cwd: appRoot,
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

// The copy runs before the frontend build so Vite's public/ copy step picks up the complete publication.
runScript("copy:wasm");
runScript("build:frontend");

const published = validateWasmPublication(path.join(appRoot, "public", "wasm"));
const bundled = validateWasmPublication(path.join(appRoot, "dist", "wasm"));
assert.deepEqual(
  bundled,
  published,
  "The production frontend did not embed the exact validated WASM publication.",
);
console.log(`Production build contains the exact ${bundled.totals.files}-file WASM publication.`);
