/** NuGet package helpers shared by the packaging checks: locate the one .nupkg in a feed and read its manifest. */
import fs from "node:fs";
import path from "node:path";
import { childrenNamed, findFirst, parseXml } from "./xml.mjs";
import { openZip } from "./zip.mjs";

/** The single `.nupkg` (not `.snupkg`) in a directory, as an absolute path. */
export function singlePackage(directory, message) {
  const resolved = path.resolve(directory);
  const packages = fs
    .readdirSync(resolved)
    .filter((name) => name.toLowerCase().endsWith(".nupkg") && !name.toLowerCase().endsWith(".snupkg"))
    .map((name) => path.join(resolved, name));
  if (packages.length !== 1) throw new Error(message ?? `Expected exactly one package under '${resolved}', found ${packages.length}.`);
  return packages[0];
}

/** The `.nuspec` manifest of a package: `{ id, version, archive, manifest, nuspecName }`. */
export function readManifest(packagePath) {
  const archive = openZip(packagePath);
  const nuspecName = archive.entries.map((entry) => entry.name).find((name) => name.toLowerCase().endsWith(".nuspec"));
  if (!nuspecName) throw new Error(`Package '${packagePath}' has no .nuspec manifest.`);
  const manifest = parseXml(archive.read(nuspecName).toString("utf8"));
  const metadata = childrenNamed(findFirst(manifest, "package") ?? { children: [] }, "metadata")[0];
  const text = (name) => childrenNamed(metadata ?? { children: [] }, name)[0]?.text?.trim() ?? "";
  return { id: text("id"), version: text("version"), archive, manifest, nuspecName };
}
