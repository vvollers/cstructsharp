/**
 * Stages the repository's validated WASM publication (artifacts/wasm) into the calling app's public/wasm, so Vite
 * copies it into the build. Run from an app directory (npm run copy:wasm); the publication must already exist
 * (npm run build:wasm at the repository root). Both the source and the copy are checked against the publication
 * manifest.
 *
 *   node ../shared/scripts/copy-wasm.mjs
 */
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

import { validateWasmPublication } from "../../../tools/packaging/wasm-publication.mjs";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../../..");
const source = path.join(repositoryRoot, "artifacts/wasm");
const destination = path.join(process.cwd(), "public/wasm");

const expected = validateWasmPublication(source);
fs.rmSync(destination, { recursive: true, force: true });
fs.cpSync(source, destination, { recursive: true });
assert.deepEqual(validateWasmPublication(destination), expected);
console.log(`Copied ${expected.totals.files} validated WASM files.`);
