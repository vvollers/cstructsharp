/**
 * Shared helpers for the npm packaging and release tools: the repository root, the `artifacts/npm` output directory, a
 * checked process runner, a shell-free npm runner, and the release version read from the core project.
 * create-wasm-npm-package, test-npm-package, npm-release, release-artifacts and the release-state tests import it; it
 * has no command line of its own.
 */
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { npmCommand, repositoryRoot } from "../lib/tooling.mjs";

export const root = repositoryRoot;
export const npmArtifacts = path.join(root, "artifacts", "npm");
/**
 * Runs a command without a shell and returns its standard output.
 * @param {string} command The executable.
 * @param {string[]} args Its arguments.
 * @param {object} [options] `spawnSync` options; the default timeout is 120 seconds.
 * @returns {string} The standard output.
 * @throws {Error} When the command cannot start or exits with a nonzero status; the message includes its output.
 */
export function run(command, args, options = {}) {
  // Calls spawnSync directly because the package tests need a timeout, which the shared runCommand does not offer.
  const result = spawnSync(command, args, { encoding: "utf8", timeout: 120000, ...options });
  if (result.error) throw result.error;
  if (result.status !== 0)
    throw new Error(
      `${command} ${args.join(" ")} failed (${result.status}):\n${result.stdout ?? ""}\n${result.stderr ?? ""}`,
    );
  return result.stdout;
}
/** Runs npm with `args` without a shell (see `npmCommand`) and returns its stdout; throws on failure. */
export function npm(args, options = {}) {
  const { command, prefix } = npmCommand();
  return run(command, [...prefix, ...args], options);
}
/**
 * Reads the release version from the `VersionPrefix` of src/CStructSharp/CStructSharp.csproj.
 * @returns {string} The version.
 * @throws {Error} When the prefix is missing or a `VersionSuffix` marks a prerelease.
 */
export function releaseVersion() {
  const project = fs.readFileSync(path.join(root, "src/CStructSharp", "CStructSharp.csproj"), "utf8");
  const version = project.match(/<VersionPrefix>([^<]+)<\/VersionPrefix>/)?.[1];
  const suffix = project.match(/<VersionSuffix>([^<]+)<\/VersionSuffix>/)?.[1];
  if (!version || suffix)
    throw new Error("npm release packaging requires a stable VersionPrefix without VersionSuffix.");
  return version;
}
