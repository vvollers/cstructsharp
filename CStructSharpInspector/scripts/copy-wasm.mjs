import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// This app does not publish its own .NET WASM bundle - CStructSharpWeb already builds and validates
// one (scripts/publish-wasm.mjs + wasm-publication.mjs there), and re-publishing here would mean the
// browser bundle is built (and could drift) twice per release. Instead, copy the already-built,
// already-validated bundle verbatim.

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const inspectorRoot = path.resolve(scriptDirectory, "..");
const sourceDirectory = path.resolve(inspectorRoot, "..", "CStructSharpWeb", "public", "wasm");
const destinationDirectory = path.join(inspectorRoot, "public", "wasm");

if (!fs.existsSync(sourceDirectory)) {
  throw new Error(
    `CStructSharpWeb's published WASM bundle was not found at ${sourceDirectory}. ` +
      "Build CStructSharpWeb first (npm run build, or npm run build:wasm for a dev-only publish) so " +
      "its public/wasm bundle exists before building CStructSharpInspector.",
  );
}

fs.rmSync(destinationDirectory, { recursive: true, force: true });
fs.cpSync(sourceDirectory, destinationDirectory, { recursive: true });

function listFiles(root, current = root) {
  return fs.readdirSync(current, { withFileTypes: true }).flatMap((entry) => {
    const absolute = path.join(current, entry.name);
    return entry.isDirectory() ? listFiles(root, absolute) : [path.relative(root, absolute)];
  });
}

const sourceFiles = listFiles(sourceDirectory).sort();
const destinationFiles = listFiles(destinationDirectory).sort();
if (JSON.stringify(sourceFiles) !== JSON.stringify(destinationFiles)) {
  throw new Error(
    "Copied WASM bundle does not match CStructSharpWeb's published bundle file list.",
  );
}

console.log(`Copied ${sourceFiles.length} WASM files from CStructSharpWeb/public/wasm.`);
