import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const runtimeDirectory = fileURLToPath(
  new URL("./runtime/", import.meta.url),
);
export const manifest = JSON.parse(
  fs.readFileSync(new URL("./runtime-manifest.json", import.meta.url), "utf8"),
);

// All destinations are new files/directories; never remove an application's existing assets.
export function copyRuntime(destination) {
  const target = path.resolve(destination);
  if (fs.existsSync(target))
    throw new Error(
      `Destination already exists: ${target}. Choose an empty, new runtime directory.`,
    );
  fs.cpSync(runtimeDirectory, target, {
    recursive: true,
    errorOnExist: true,
    force: false,
  });
  return target;
}
