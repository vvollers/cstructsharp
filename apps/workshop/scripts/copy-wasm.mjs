import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import assert from "node:assert/strict";
import { validateWasmPublication } from "../../../tools/packaging/wasm-publication.mjs";

const appRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const source = path.resolve(appRoot, "../../artifacts/wasm");
const destination = path.join(appRoot, "public/wasm");
const expected = validateWasmPublication(source);
fs.rmSync(destination, { recursive: true, force: true });
fs.cpSync(source, destination, { recursive: true });
assert.deepEqual(validateWasmPublication(destination), expected);
console.log(`Copied ${expected.totals.files} validated WASM files.`);
