/**
 * Validates a WASM publication directory (default artifacts/wasm) and prints its file count and size.
 *
 *   node tools/packaging/verify-wasm-publication.mjs [<publication-directory>]    (npm run verify:wasm)
 */
import path from "node:path";
import { parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { validateWasmPublication } from "./wasm-publication.mjs";

const {
  _: [publicationArgument],
} = parseArguments(process.argv.slice(2), {}, { positionals: true });
const publicationDirectory = publicationArgument
  ? path.resolve(publicationArgument)
  : path.join(repositoryRoot, "artifacts/wasm");

const manifest = validateWasmPublication(publicationDirectory);
console.log(
  `Validated ${manifest.totals.files} deployable WASM files (${manifest.totals.bytes} bytes).`,
);
