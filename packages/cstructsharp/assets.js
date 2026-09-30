import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

/** The absolute path of the installed runtime files. */
export const runtimeDirectory = fileURLToPath(
  new URL("./runtime/", import.meta.url),
);
/** The installed runtime's manifest: each file's path and SHA-256. */
export const manifest = JSON.parse(
  fs.readFileSync(new URL("./runtime-manifest.json", import.meta.url), "utf8"),
);

/**
 * Copies the complete runtime into a new directory, for hosting it as static files.
 * All destinations are new files/directories; an application's existing assets are never removed or replaced.
 * @param {string} destination The directory to create, relative to the working directory or absolute.
 * @returns {string} The absolute path of the created directory.
 * @throws {Error} When the destination already exists, or a copy fails.
 */
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
