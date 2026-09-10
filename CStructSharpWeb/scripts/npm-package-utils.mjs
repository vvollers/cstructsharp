import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";

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
export function npm(args, options = {}) {
  if (!process.env.npm_execpath)
    throw new Error("Start this command through npm run so the pinned npm CLI can be located.");
  return run(process.execPath, [process.env.npm_execpath, ...args], options);
}
export function releaseVersion() {
  const project = fs.readFileSync(path.join(root, "CStructSharp", "CStructSharp.csproj"), "utf8");
  const version = project.match(/<VersionPrefix>([^<]+)<\/VersionPrefix>/)?.[1];
  const suffix = project.match(/<VersionSuffix>([^<]+)<\/VersionSuffix>/)?.[1];
  if (!version || suffix)
    throw new Error("npm release packaging requires a stable VersionPrefix without VersionSuffix.");
  return version;
}
