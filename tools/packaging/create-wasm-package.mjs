/**
 * Builds the standalone WASM bundle directory artifacts/wasm-package from the publication in artifacts/wasm and the
 * standalone sources in packages/cstructsharp (library entry, API module, declarations, README, starter pages and
 * static server); the release workflow zips that directory. It also exports `createWasmPackage` for other destinations.
 *
 *   node tools/packaging/create-wasm-package.mjs    (npm run pack:zip)
 */
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { repositoryRoot as root } from "../lib/tooling.mjs";
import { validateWasmPublication } from "./wasm-publication.mjs";

const source = path.join(root, "artifacts/wasm");
const destination = path.join(root, "artifacts/wasm-package");
const adapterSource = path.join(root, "packages/cstructsharp/src");
const standaloneSource = path.join(root, "packages/cstructsharp/standalone");
const libraryEntry = path.join(adapterSource, "cstructsharp-wasm.js");
const readme = path.join(standaloneSource, "README.md");

/**
 * Copies a directory tree of regular files.
 * @throws {Error} When the tree contains an entry that is neither a directory nor a regular file.
 */
function copyDirectory(sourceDirectory, destinationDirectory) {
  fs.mkdirSync(destinationDirectory, { recursive: true });
  for (const entry of fs.readdirSync(sourceDirectory, { withFileTypes: true })) {
    const sourcePath = path.join(sourceDirectory, entry.name);
    const destinationPath = path.join(destinationDirectory, entry.name);
    if (entry.isDirectory()) {
      copyDirectory(sourcePath, destinationPath);
    } else if (entry.isFile()) {
      fs.copyFileSync(sourcePath, destinationPath);
    } else {
      throw new Error(`WASM package source contains an unsupported entry: ${sourcePath}`);
    }
  }
}

/**
 * Builds the standalone WASM package: the validated publication plus the library entry, API module, declarations,
 * README, starter files, and static server. The destination directory is replaced.
 * @param {string} sourceDirectory Built WASM publication.
 * @param {string} destinationDirectory Package directory to create.
 * @returns {{directory: string, manifest: object, files: string[]}} The package directory, the publication manifest,
 *   and the package-relative paths of its files.
 * @throws {Error} When the publication, library entry, or README is missing.
 */
export function createWasmPackage(sourceDirectory = source, destinationDirectory = destination) {
  if (!fs.existsSync(sourceDirectory)) {
    throw new Error(`The built WASM publication does not exist: ${sourceDirectory}`);
  }
  if (!fs.existsSync(libraryEntry) || !fs.existsSync(readme)) {
    throw new Error("The standalone WASM library entry point or README is missing.");
  }

  fs.rmSync(destinationDirectory, { recursive: true, force: true });
  const manifest = validateWasmPublication(sourceDirectory);
  copyDirectory(sourceDirectory, destinationDirectory);
  fs.copyFileSync(libraryEntry, path.join(destinationDirectory, "cstructsharp-wasm.js"));
  fs.copyFileSync(path.join(adapterSource, "cstructsharp-api.js"), path.join(destinationDirectory, "cstructsharp-api.js"));
  // The ZIP bundle ships the package's declarations under the bundle entry's name.
  fs.copyFileSync(path.join(root, "packages/cstructsharp/index.d.ts"), path.join(destinationDirectory, "cstructsharp-wasm.d.ts"));
  fs.copyFileSync(readme, path.join(destinationDirectory, "README.md"));
  copyDirectory(path.join(standaloneSource, "starter"), path.join(destinationDirectory, "starter"));
  fs.copyFileSync(path.join(standaloneSource, "serve.mjs"), path.join(destinationDirectory, "serve.mjs"));
  return {
    directory: destinationDirectory,
    manifest,
    files: [
      "README.md",
      "cstructsharp-wasm.js",
      "cstructsharp-api.js",
      "cstructsharp-wasm.d.ts",
      "serve.mjs",
      ...fs.readdirSync(path.join(standaloneSource, "starter")).map((name) => `starter/${name}`),
      ...manifest.files.map((entry) => entry.path),
    ],
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const packageInfo = createWasmPackage();
  console.log(
    `Created standalone WASM package with ${packageInfo.files.length} files at ${packageInfo.directory}.`,
  );
}
