import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { pathToFileURL } from "node:url";
import { validateWasmPublication } from "./wasm-publication.mjs";
import { root, npmArtifacts, npm, run, releaseVersion } from "./npm-package-utils.mjs";

const source = path.join(root, "packages", "cstructsharp");
const wasmSource = path.join(root, "src/CStructSharp.Wasm");
const runtime = path.join(root, "artifacts", "wasm");
const version = releaseVersion();
const manifest = validateWasmPublication(runtime);
fs.mkdirSync(npmArtifacts, { recursive: true });
// A fresh staging directory prevents stale files entering a release and needs no recursive deletion.
const stage = fs.mkdtempSync(path.join(npmArtifacts, "stage-"));
fs.cpSync(source, stage, { recursive: true });
fs.cpSync(runtime, path.join(stage, "runtime"), { recursive: true });
const pkg = JSON.parse(fs.readFileSync(path.join(stage, "package.json"), "utf8"));
assert.equal(
  pkg.version,
  version,
  "Source npm package version must match the managed VersionPrefix.",
);
assert.equal(pkg.name, "cstructsharp");
// Source files are deliberately unpublishable; only the complete staged package is public.
delete pkg.private;
fs.writeFileSync(path.join(stage, "package.json"), `${JSON.stringify(pkg, null, 2)}\n`);
fs.writeFileSync(
  path.join(stage, "cstructsharp-api.js"),
  fs
    .readFileSync(path.join(wasmSource, "cstructsharp-api.js"), "utf8")
    .replaceAll("./cstructsharp-wasm.js", "./index.d.ts"),
);
const types = fs
  .readFileSync(path.join(wasmSource, "cstructsharp-wasm.d.ts"), "utf8")
  .replace("Public browser bundle.", "Public Node.js and browser package.")
  .replace("loadCStructSharpWasm():", "loadCStructSharpWasm(options?: { runtimeUrl?: string }):");
fs.writeFileSync(path.join(stage, "index.d.ts"), types);
fs.copyFileSync(path.join(root, "LICENSE.txt"), path.join(stage, "LICENSE.txt"));
fs.writeFileSync(
  path.join(stage, "runtime-manifest.json"),
  `${JSON.stringify(manifest, null, 2)}\n`,
);
// Take notices from the exact runtime pack selected by this project's .NET SDK.
const packs = JSON.parse(
  run("dotnet", [
    "msbuild",
    path.join(wasmSource, "CStructSharpWeb.Wasm.csproj"),
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
notices.push(fs.readFileSync(path.join(source, "PIDGIN-LICENSE.txt"), "utf8"));
fs.writeFileSync(path.join(stage, "THIRD-PARTY-NOTICES.txt"), notices.join("\n\n"));
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
console.log(
  `Packed ${packed.filename}: ${packed.size} compressed / ${packed.unpackedSize} unpacked bytes.\n${packed.integrity}`,
);
