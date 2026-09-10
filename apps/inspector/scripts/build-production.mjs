import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const appRoot = path.resolve(scriptDirectory, "..");
const npmCli = process.env.npm_execpath;

if (!npmCli) {
  throw new Error("The production build must be started through npm.");
}

function runScript(name) {
  const result = spawnSync(process.execPath, [npmCli, "run", name], {
    cwd: appRoot,
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

// The WASM copy runs before the frontend build so Vite's `public/` copy step picks up the bundle.
runScript("copy:wasm");
runScript("build:frontend");
