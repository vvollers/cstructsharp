import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const appRoot = path.resolve(scriptDirectory, "..");
// The npm CLI that started this script when run through `npm run`, otherwise the `npm` on PATH.
const npmCli = process.env.npm_execpath;

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

// The WASM copy runs before the frontend build so Vite's `public/` copy step picks up the bundle.
runScript("copy:wasm");
runScript("build:frontend");
