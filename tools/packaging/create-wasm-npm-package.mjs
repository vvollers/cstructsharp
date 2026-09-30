/**
 * Packs the `cstructsharp` npm package. It validates the WASM publication in artifacts/wasm, stages the authored
 * package files from packages/cstructsharp with the runtime, declarations, license, the .NET runtime pack notices and
 * the runtime manifest, checks that the staged package reports the managed release version, and runs `npm pack`. The
 * tarball and its metadata (package-info.json) are written to artifacts/npm; the staging directory is kept only when a
 * step fails.
 *
 *   node tools/packaging/create-wasm-npm-package.mjs    (npm run pack:npm)
 */
import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { validateWasmPublication } from "./wasm-publication.mjs";
import { root, npmArtifacts, npm, run, releaseVersion } from "./npm-package-utils.mjs";

const source = path.join(root, "packages", "cstructsharp");
const adapterSource = path.join(source, "src");
const wasmProject = path.join(root, "src/CStructSharp.Wasm");
const runtime = path.join(root, "artifacts", "wasm");
const version = releaseVersion();
const manifest = validateWasmPublication(runtime);
fs.mkdirSync(npmArtifacts, { recursive: true });
// A fresh staging directory prevents stale files entering a release and needs no recursive deletion.
const stage = fs.mkdtempSync(path.join(npmArtifacts, "stage-"));
// The authored package files are copied as they are; src/ (adapter sources) and standalone/ (ZIP bundle pieces) stay
// out, and the steps below add the generated files. package.json "files" is only npm's publish list; the check before
// packing compares it with the staged files, so the two cannot drift apart silently.
const authoredFiles = ["README.md", "node.js", "browser.js", "runtime-loader.js", "assets.js", "copy-assets.js", "vite.js", "index.d.ts", "vite.d.ts"];
const pkg = JSON.parse(fs.readFileSync(path.join(source, "package.json"), "utf8"));
for (const name of authoredFiles) {
  fs.copyFileSync(path.join(source, name), path.join(stage, name));
}
fs.cpSync(runtime, path.join(stage, "runtime"), { recursive: true });
assert.equal(
  pkg.version,
  version,
  "Source npm package version must match the managed VersionPrefix.",
);
assert.equal(pkg.name, "cstructsharp");
// Source files are deliberately unpublishable; only the complete staged package is public.
delete pkg.private;
fs.writeFileSync(path.join(stage, "package.json"), `${JSON.stringify(pkg, null, 2)}\n`);
// The API module ships in both bundles, and its JSDoc type imports must name that bundle's declaration file. The
// source names the ZIP's (cstructsharp-wasm.js, whose declarations sit beside it as cstructsharp-wasm.d.ts); the
// package publishes the same declarations as its "types" entry index.d.ts, so its copy names that file instead.
// Both declaration files are copies of packages/cstructsharp/index.d.ts.
const apiSource = fs.readFileSync(path.join(adapterSource, "cstructsharp-api.js"), "utf8");
assert.ok(apiSource.includes("./cstructsharp-wasm.js"), "cstructsharp-api.js no longer references its declaration module; update the packaging rewrite.");
fs.writeFileSync(path.join(stage, "cstructsharp-api.js"), apiSource.replaceAll("./cstructsharp-wasm.js", "./index.d.ts"));
// The API module imports its shared constants and helpers from a sibling module; the runtime modules under runtime/
// import their own copy of the same file from the publication.
fs.copyFileSync(path.join(runtime, "cstructsharp-shared.js"), path.join(stage, "cstructsharp-shared.js"));
fs.copyFileSync(path.join(root, "LICENSE.txt"), path.join(stage, "LICENSE.txt"));
fs.writeFileSync(
  path.join(stage, "runtime-manifest.json"),
  `${JSON.stringify(manifest, null, 2)}\n`,
);
// Take notices from the exact runtime pack selected by this project's .NET SDK.
const packs = JSON.parse(
  run("dotnet", [
    "msbuild",
    path.join(wasmProject, "CStructSharpWeb.Wasm.csproj"),
    "-t:ResolveFrameworkReferences",
    "-getItem:ResolvedRuntimePack",
  ]),
);
const pack = packs.Items.ResolvedRuntimePack.find(
  (item) => item.RuntimeIdentifier === "browser-wasm",
);
assert.ok(pack?.PackageDirectory, "Cannot locate runtime license files.");
const notices = ["LICENSE.TXT", "THIRD-PARTY-NOTICES.TXT"].map((name) =>
  fs.readFileSync(path.join(pack.PackageDirectory, name), "utf8"),
);
fs.writeFileSync(path.join(stage, "THIRD-PARTY-NOTICES.txt"), notices.join("\n\n"));
// npm packs only the "files" entries (plus package.json, the README and the license), so each list must cover the other.
for (const entry of pkg.files) {
  assert.ok(fs.existsSync(path.join(stage, entry)), `package.json "files" lists ${entry}, which the pack script does not stage.`);
}
const alwaysPacked = new Set(["package.json", "README.md", "LICENSE.txt"]);
for (const entry of fs.readdirSync(stage)) {
  const listed = alwaysPacked.has(entry) || pkg.files.includes(entry) || pkg.files.includes(`${entry}/`);
  assert.ok(listed, `The pack script stages ${entry}, which package.json "files" does not list.`);
}
// This child exits naturally and checks the real managed version before pack, catching stale WASM builds.
const check = `import { getVersion } from ${JSON.stringify(pathToFileURL(path.join(stage, "node.js")).href)}; console.log(await getVersion());`;
const managedVersion = run(process.execPath, ["--input-type=module", "-e", check]).trim();
assert.match(
  managedVersion,
  new RegExp(`^CStructSharp WASM ${version.replaceAll(".", "\\.")}(?:\\+.*)?$`),
);
const packResult = JSON.parse(
  npm(
    [
      "pack",
      stage,
      "--json",
      "--ignore-scripts",
      "--cache",
      path.join(root, ".npm"),
      "--pack-destination",
      npmArtifacts,
    ],
    { cwd: root },
  ),
);
const packed = Array.isArray(packResult) ? packResult[0] : packResult.cstructsharp;
const sha256 = crypto
  .createHash("sha256")
  .update(fs.readFileSync(path.join(npmArtifacts, packed.filename)))
  .digest("hex");
const metadata = {
  name: pkg.name,
  version,
  filename: packed.filename,
  integrity: packed.integrity,
  sha256,
  size: packed.size,
  unpackedSize: packed.unpackedSize,
  sourceSha: run("git", ["rev-parse", "HEAD"], { cwd: root }).trim(),
  runtimePackVersion: pack.NuGetPackageVersion,
  runtime: manifest,
  files: packed.files,
};
fs.writeFileSync(
  path.join(npmArtifacts, "package-info.json"),
  `${JSON.stringify(metadata, null, 2)}\n`,
);
// The staged copy served its purpose once the tarball exists; a failed run keeps it for inspection.
fs.rmSync(stage, { recursive: true, force: true });
console.log(
  `Packed ${packed.filename}: ${packed.size} compressed / ${packed.unpackedSize} unpacked bytes.\n${packed.integrity}`,
);
