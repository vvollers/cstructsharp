#!/usr/bin/env node
/**
 * Validates the single CStructSharp NuGet package (metadata, license, repository provenance, required entries,
 * one assembly per framework, zero dependencies) and its portable symbol package, without loading assemblies.
 *
 *   node tools/packaging/validate-package.mjs --package-path <.nupkg> --symbol-package-path <.snupkg>
 */
import path from "node:path";
import { assertCondition, main, parseArguments } from "../lib/tooling.mjs";
import { childrenNamed, findAll, findFirst, parseXml } from "../lib/xml.mjs";
import { openZip } from "../lib/zip.mjs";

const options = parseArguments(process.argv.slice(2), { "package-path": "string", "symbol-package-path": "string" });
assertCondition(options["package-path"] && options["symbol-package-path"], "Options --package-path and --symbol-package-path are required.");

await main(() => {
  const packagePath = path.resolve(options["package-path"]);
  const symbolPackagePath = path.resolve(options["symbol-package-path"]);
  const lower = packagePath.toLowerCase();
  if (!lower.endsWith(".nupkg") || lower.endsWith(".snupkg")) throw new Error(`Expected a NuGet .nupkg file, but received '${packagePath}'.`);
  if (!symbolPackagePath.toLowerCase().endsWith(".snupkg")) throw new Error(`Expected a NuGet .snupkg file, but received '${symbolPackagePath}'.`);

  const archive = openZip(packagePath);
  const symbolArchive = openZip(symbolPackagePath);
  const entryNames = archive.entries.map((entry) => entry.name);
  const nuspecName = entryNames.find((name) => name.toLowerCase().endsWith(".nuspec"));
  if (!nuspecName) throw new Error("The package does not contain a .nuspec manifest.");
  const nuspec = parseXml(archive.read(nuspecName).toString("utf8"));
  const packageElement = findFirst(nuspec, "package");
  const metadata = packageElement ? childrenNamed(packageElement, "metadata")[0] : null;
  if (!metadata) throw new Error("The package manifest has no metadata element.");

  const requireText = (name, description) => {
    const node = childrenNamed(metadata, name)[0];
    if (!node || node.text.trim() === "") throw new Error(`Package metadata is missing ${description}.`);
    return node;
  };
  const license = requireText("license", "a license");
  if (license.attributes.type !== "expression" || license.text.trim() !== "MIT") throw new Error(`Expected the MIT license expression, found '${license.text}'.`);
  const repository = childrenNamed(metadata, "repository")[0];
  if (!repository) throw new Error("Package metadata is missing repository provenance.");
  if (repository.attributes.type !== "git" || !(repository.attributes.url ?? "").startsWith("https://github.com/vvollers/CStructSharp")) {
    throw new Error("Repository metadata does not identify the canonical Git repository.");
  }
  if (!(repository.attributes.commit ?? "").trim()) throw new Error("Repository metadata does not identify the source commit.");
  const projectUrl = requireText("projectUrl", "a project URL");
  if (projectUrl.text.trim() !== "https://github.com/vvollers/CStructSharp") throw new Error(`Unexpected project URL '${projectUrl.text}'.`);
  const readme = requireText("readme", "a package readme");
  const releaseNotes = requireText("releaseNotes", "release notes");
  if (!releaseNotes.text.includes("https://github.com/vvollers/CStructSharp/issues")) throw new Error("Package release metadata does not include the canonical issue tracker.");

  const requiredEntries = [
    readme.text.trim(),
    "CHANGELOG.md",
    "LICENSE.txt",
    "MUTATION_TESTING.md",
    "lib/net8.0/CStructSharp.dll",
    "lib/net8.0/CStructSharp.xml",
    "lib/net10.0/CStructSharp.dll",
    "lib/net10.0/CStructSharp.xml",
  ];
  for (const required of requiredEntries) {
    if (!entryNames.includes(required)) throw new Error(`The package is missing required entry '${required}'.`);
  }
  const symbolEntryNames = symbolArchive.entries.map((entry) => entry.name);
  const requiredSymbolEntries = ["lib/net8.0/CStructSharp.pdb", "lib/net10.0/CStructSharp.pdb"];
  for (const required of requiredSymbolEntries) {
    if (!symbolEntryNames.includes(required)) throw new Error(`The symbol package is missing required entry '${required}'.`);
  }
  if (findAll(nuspec, "dependency").length !== 0) throw new Error("CStructSharp must have zero runtime NuGet dependencies, including for memory analysis.");
  const assemblies = entryNames.filter((name) => /^lib\/.*\.dll$/.test(name)).sort();
  const expectedAssemblies = ["lib/net10.0/CStructSharp.dll", "lib/net8.0/CStructSharp.dll"];
  if (assemblies.join(",") !== expectedAssemblies.join(",")) throw new Error("The package must contain exactly one CStructSharp assembly per supported framework.");
  console.log(`Validated package metadata, ${requiredEntries.length} package entries, and ${requiredSymbolEntries.length} portable symbol entries.`);
});
