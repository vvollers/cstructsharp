import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import { npmCommand } from "../lib/tooling.mjs";

export const root = fileURLToPath(new URL("../../", import.meta.url));
export const npmArtifacts = path.join(root, "artifacts", "npm");
export function run(command, args, options = {}) {
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
export function releaseVersion() {
  const project = fs.readFileSync(path.join(root, "src/CStructSharp", "CStructSharp.csproj"), "utf8");
  const version = project.match(/<VersionPrefix>([^<]+)<\/VersionPrefix>/)?.[1];
  const suffix = project.match(/<VersionSuffix>([^<]+)<\/VersionSuffix>/)?.[1];
  if (!version || suffix)
    throw new Error("npm release packaging requires a stable VersionPrefix without VersionSuffix.");
  return version;
}
