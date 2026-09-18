#!/usr/bin/env node
/**
 * Restores and runs tests/CStructSharp.PackageConsumer against the one package in a directory from an isolated
 * package cache, checking that the consumer selected the package's own assemblies for net8.0 and net10.0 and that
 * the package came from the feed under test.
 *
 *   node tools/packaging/test-package-consumer.mjs [--package-directory artifacts/package]
 */
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { main, parseArguments, repositoryRoot, runDotnet } from "../lib/tooling.mjs";
import { readManifest, singlePackage } from "../lib/nuget.mjs";

const options = parseArguments(process.argv.slice(2), { "package-directory": "string" }, { defaults: { "package-directory": path.join(repositoryRoot, "artifacts", "package") } });

await main(() => {
  const packageDirectory = path.resolve(options["package-directory"]);
  const packagePath = singlePackage(packageDirectory);
  const { id, version } = readManifest(packagePath);
  if (id !== "CStructSharp" || version.trim() === "") throw new Error(`Expected a versioned CStructSharp package, found '${id}' '${version}'.`);

  const projectDirectory = path.join(repositoryRoot, "tests/CStructSharp.PackageConsumer");
  const project = path.join(projectDirectory, "CStructSharp.PackageConsumer.csproj");
  const nugetConfig = path.join(projectDirectory, "NuGet.config");
  const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-package-consumer-"));
  const packageCache = path.join(temporaryRoot, "packages");
  fs.mkdirSync(packageCache);
  const env = { ...process.env, CSTRUCTSHARP_PACKAGE_SOURCE: packageDirectory, NUGET_PACKAGES: packageCache, CStructSharpPackageVersion: version };
  try {
    runDotnet(["restore", project, "--configfile", nugetConfig, "--force", "--no-cache"], { env });
    const assets = JSON.parse(fs.readFileSync(path.join(projectDirectory, "obj", "project.assets.json"), "utf8"));
    const libraryKey = `CStructSharp/${version}`;
    for (const framework of ["net8.0", "net10.0"]) {
      const target = assets.targets?.[framework]?.[libraryKey];
      if (!target) throw new Error(`Restored assets do not contain '${libraryKey}' for '${framework}'.`);
      const expectedAssembly = `lib/${framework}/CStructSharp.dll`;
      if (!Object.hasOwn(target.compile ?? {}, expectedAssembly) || !Object.hasOwn(target.runtime ?? {}, expectedAssembly)) {
        throw new Error(`The '${framework}' consumer did not select '${expectedAssembly}'.`);
      }
    }
    const metadata = JSON.parse(fs.readFileSync(path.join(packageCache, "cstructsharp", version.toLowerCase(), ".nupkg.metadata"), "utf8"));
    if (!metadata.source || String(metadata.source).trim() === "") throw new Error("The restored package metadata does not identify its source.");
    const sourcePath = path.resolve(metadata.source);
    if (sourcePath !== packagePath && sourcePath !== packageDirectory) throw new Error(`CStructSharp restored from '${metadata.source}' instead of the package under test.`);
    runDotnet(["format", project, "--no-restore", "--verify-no-changes"], { env });
    for (const framework of ["net8.0", "net10.0"]) {
      runDotnet(["run", "--project", project, "-c", "Release", "-f", framework, "--no-restore"], { env });
    }
    console.log(`Validated package consumer behavior for net8.0 and net10.0 against '${path.basename(packagePath)}' from an isolated package cache.`);
  } finally {
    fs.rmSync(temporaryRoot, { recursive: true, force: true });
  }
});
