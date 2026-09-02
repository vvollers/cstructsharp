import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { validateWasmPublication } from "./wasm-publication.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const publicationDirectory = path.join(webRoot, "public", "wasm");
const staleRootFile = path.join(publicationDirectory, "stale-output.dll");
const staleFrameworkFile = path.join(publicationDirectory, "_framework", "stale-output.map");

const firstManifest = validateWasmPublication(publicationDirectory);
try {
  fs.writeFileSync(staleRootFile, "stale");
  fs.writeFileSync(staleFrameworkFile, "stale");

  const result = spawnSync(process.execPath, [path.join(scriptDirectory, "publish-wasm.mjs")], {
    cwd: webRoot,
    stdio: "inherit",
    shell: false,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`The dirty-destination rebuild failed with exit code ${result.status}.`);
  }

  const secondManifest = validateWasmPublication(publicationDirectory);
  assert.deepEqual(
    secondManifest,
    firstManifest,
    "A stale destination changed the deployable paths, bytes, or SHA-256 hashes.",
  );
  assert.equal(fs.existsSync(staleRootFile), false);
  assert.equal(fs.existsSync(staleFrameworkFile), false);
  console.log(
    `Clean/dirty publication reproducibility passed for ${secondManifest.totals.files} files.`,
  );
} finally {
  for (const staleFile of [staleRootFile, staleFrameworkFile]) {
    if (fs.existsSync(staleFile)) {
      fs.rmSync(staleFile, { force: true });
    }
  }
}
