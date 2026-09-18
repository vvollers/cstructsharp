#!/usr/bin/env node
/**
 * Runs the synthetic memory consumer (tests/CStructSharp.Memory.PackageConsumer) using only the CStructSharp
 * NuGet package from a fresh package cache, verifying provenance and that no separate memory package is restored.
 * Without --package-directory it packs a local package first. Evidence lands under artifacts/memory-package.
 *
 *   node tools/packaging/test-memory-package-consumer.mjs [--package-directory <dir>]
 */
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { main, parseArguments, repositoryRoot, runCommand, runDotnet } from "../lib/tooling.mjs";
import { readManifest, singlePackage } from "../lib/nuget.mjs";

const options = parseArguments(process.argv.slice(2), { "package-directory": "string" });

await main(() => {
  const output = path.join(repositoryRoot, "artifacts/memory-package", crypto.randomUUID().replaceAll("-", ""));
  const consumer = path.join(repositoryRoot, "tests/CStructSharp.Memory.PackageConsumer/CStructSharp.Memory.PackageConsumer.csproj");
  let packageDirectory = options["package-directory"];
  if (!packageDirectory) {
    packageDirectory = path.join(output, "feed");
    const pack = runCommand("dotnet", ["pack", path.join(repositoryRoot, "src/CStructSharp/CStructSharp.csproj"), "-c", "Release", "-o", packageDirectory], { allowFailure: true });
    process.stdout.write(pack.stdout ?? "");
    if (pack.status !== 0) throw new Error("CStructSharp package creation failed.");
  }
  const feed = path.resolve(packageDirectory);
  const packagePath = singlePackage(feed, `Expected one CStructSharp package in ${feed}.`);
  const { version } = readManifest(packagePath);
  const validate = runCommand(process.execPath, [path.join(repositoryRoot, "tools/packaging/validate-package.mjs"), "--package-path", packagePath, "--symbol-package-path", path.join(feed, `CStructSharp.${version}.snupkg`)], { allowFailure: true });
  process.stdout.write(validate.stdout ?? "");
  if (validate.status !== 0) throw new Error((validate.stderr ?? "").trim() || "Package validation failed.");

  const restore = runCommand(
    "dotnet",
    ["restore", consumer, "--configfile", path.join(repositoryRoot, "tests/CStructSharp.Memory.PackageConsumer/NuGet.config"), "--packages", path.join(output, "packages"), `-p:MemoryPackageVersion=${version}`],
    { env: { ...process.env, CSTRUCTSHARP_MEMORY_PACKAGE_SOURCE: feed }, allowFailure: true },
  );
  process.stdout.write(restore.stdout ?? "");
  if (restore.status !== 0) throw new Error("Memory consumer restore failed; framework reference packs may require network access.");

  const metadata = JSON.parse(fs.readFileSync(path.join(output, "packages/cstructsharp", version, ".nupkg.metadata"), "utf8"));
  if (path.resolve(metadata.source) !== feed) throw new Error("Unexpected source for CStructSharp.");
  if (fs.existsSync(path.join(output, "packages/cstructsharp.memory"))) throw new Error("Consumer restored a separate memory package.");
  for (const framework of ["net8.0", "net10.0"]) {
    runDotnet(["run", "--project", consumer, "-c", "Release", "-f", framework, "--no-restore", `-p:MemoryPackageVersion=${version}`], { label: `Memory consumer on ${framework}` });
  }
  console.log(`Memory consumer passed using only CStructSharp. Evidence: ${output}`);
});
