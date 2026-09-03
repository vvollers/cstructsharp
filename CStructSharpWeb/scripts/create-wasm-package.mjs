import fs from "node:fs";
import path from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";
import { validateWasmPublication } from "./wasm-publication.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const source = path.join(webRoot, "dist", "wasm");
const destination = path.join(webRoot, "artifacts", "wasm-package");
const libraryEntry = path.join(webRoot, "wasm", "cstructsharp-wasm.js");
const readme = path.join(webRoot, "wasm", "README.md");

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
  fs.copyFileSync(readme, path.join(destinationDirectory, "README.md"));
  return {
    directory: destinationDirectory,
    manifest,
    files: ["README.md", "cstructsharp-wasm.js", ...manifest.files.map((entry) => entry.path)],
  };
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
  const packageInfo = createWasmPackage();
  console.log(
    `Created standalone WASM package with ${packageInfo.files.length} files at ${packageInfo.directory}.`,
  );
}
