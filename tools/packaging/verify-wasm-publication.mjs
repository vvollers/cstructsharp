import path from "node:path";
import { fileURLToPath } from "node:url";
import { validateWasmPublication } from "./wasm-publication.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "../../apps/workshop");
const publicationDirectory = process.argv[2]
  ? path.resolve(process.argv[2])
  : path.resolve(webRoot, "../../artifacts/wasm");

const manifest = validateWasmPublication(publicationDirectory);
console.log(
  `Validated ${manifest.totals.files} deployable WASM files (${manifest.totals.bytes} bytes).`,
);
