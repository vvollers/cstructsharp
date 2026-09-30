/**
 * Checks that the WASM publication is reproducible across checkout paths and over a dirty destination. It records the
 * manifest (paths, sizes and SHA-256 hashes) of artifacts/wasm, copies the working tree (tracked and untracked,
 * non-ignored files) to a temporary directory at a different path, seeds that copy's artifacts/wasm with the current
 * publication plus stale files, runs the copy's publish-wasm.mjs, and asserts that the copy's manifest equals the
 * original and the stale files are gone. The copy builds from scratch, so the check also covers a clean build.
 * Requires an existing publication (npm run build:wasm), git, the .NET SDK and the wasm-tools workload.
 *
 *   node tools/packaging/test-wasm-reproducibility.mjs    (npm run test:wasm-reproducibility)
 */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { repositoryFiles } from "../lib/files.mjs";
import { repositoryRoot, runCommand } from "../lib/tooling.mjs";
import { validateWasmPublication } from "./wasm-publication.mjs";

const publicationDirectory = path.join(repositoryRoot, "artifacts/wasm");

/**
 * Copies the working tree's tracked and untracked, non-ignored files into `destinationRoot` and points the copy at the
 * original git directory, so the build reads the same commit metadata (SourceLink and the informational version)
 * without copying the repository history. Tracked files deleted from the working tree are skipped.
 * @param {string} destinationRoot Empty directory that becomes the copy's repository root.
 * @throws {Error} When a listed path is neither missing nor a regular file.
 */
function copyWorkingTree(destinationRoot) {
  for (const relativePath of repositoryFiles(repositoryRoot)) {
    const source = path.join(repositoryRoot, ...relativePath.split("/"));
    if (!fs.existsSync(source)) {
      continue;
    }
    const stat = fs.lstatSync(source);
    if (!stat.isFile()) {
      throw new Error(`Cannot copy a working-tree entry that is not a regular file: ${relativePath}`);
    }
    const destination = path.join(destinationRoot, ...relativePath.split("/"));
    fs.mkdirSync(path.dirname(destination), { recursive: true });
    fs.copyFileSync(source, destination);
  }

  // A .git file (the form git uses for linked worktrees) makes the copy a reader of the original repository.
  const gitDirectory = runCommand("git", ["-C", repositoryRoot, "rev-parse", "--absolute-git-dir"]).stdout.trim();
  fs.writeFileSync(path.join(destinationRoot, ".git"), `gitdir: ${gitDirectory}\n`, "utf8");
}

const firstManifest = validateWasmPublication(publicationDirectory);
const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-wasm-repro-"));
try {
  // A different directory name and depth from the original checkout: any path embedded in an output shows up.
  const copyRoot = path.join(temporaryRoot, "relocated", "checkout");
  fs.mkdirSync(copyRoot, { recursive: true });
  copyWorkingTree(copyRoot);

  const copyPublicationDirectory = path.join(copyRoot, "artifacts/wasm");
  fs.cpSync(publicationDirectory, copyPublicationDirectory, { recursive: true });
  const staleRootFile = path.join(copyPublicationDirectory, "stale-output.dll");
  const staleFrameworkFile = path.join(copyPublicationDirectory, "_framework", "stale-output.map");
  fs.writeFileSync(staleRootFile, "stale");
  fs.writeFileSync(staleFrameworkFile, "stale");

  // A direct spawn with inherited stdio streams the long publish (dotnet publish) output live. Without MSBuild node
  // reuse no build process outlives the publish and holds files in the copy, so the cleanup below can remove it.
  const result = spawnSync(process.execPath, [path.join(copyRoot, "tools/packaging/publish-wasm.mjs")], {
    cwd: copyRoot,
    env: { ...process.env, MSBUILDDISABLENODEREUSE: "1" },
    stdio: "inherit",
    shell: false,
  });
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`The relocated rebuild failed with exit code ${result.status}.`);
  }

  const secondManifest = validateWasmPublication(copyPublicationDirectory);
  assert.deepEqual(
    secondManifest,
    firstManifest,
    `Publishing from ${copyRoot} changed the deployable paths, bytes, or SHA-256 hashes.`,
  );
  assert.equal(fs.existsSync(staleRootFile), false);
  assert.equal(fs.existsSync(staleFrameworkFile), false);
  console.log(
    `Cross-path and dirty-destination publication reproducibility passed for ${secondManifest.totals.files} files.`,
  );
} finally {
  try {
    fs.rmSync(temporaryRoot, { recursive: true, force: true, maxRetries: 5, retryDelay: 500 });
  } catch (error) {
    // The check has already passed or failed; a leftover temporary directory must not change that outcome.
    console.warn(`Could not remove ${temporaryRoot}: ${error instanceof Error ? error.message : error}`);
  }
}
