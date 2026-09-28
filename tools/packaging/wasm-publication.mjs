/**
 * Validates a WASM publication directory (the `main.js` entry point, its bootstrap, the source adapter and worker, the
 * module of constants and helpers they share, the runtime configuration, and a flat `_framework` holding exactly the resources `dotnet.boot.js` lists) and describes it
 * as a manifest of file sizes and SHA-256 hashes. It reads the directory and writes nothing.
 * publish-wasm.mjs uses it to stage and check the publication; the other packaging tools and the apps' shared build
 * scripts use `validateWasmPublication` to check it before use. It has no command line of its own.
 */
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";

export const runtimeConfigName = "CStructSharpWeb.Wasm.runtimeconfig.json";
export const defaultRawByteLimit = 6 * 1024 * 1024;

const requiredRootFiles = new Set([
  "bootstrap.js",
  "main.js",
  "large-source.js",
  "source-worker.js",
  "cstructsharp-shared.js",
  runtimeConfigName,
]);
const requiredFrameworkEntrypoints = new Set(["dotnet.boot.js", "dotnet.js"]);
const rejectedProductionExtensions = new Set([
  ".a",
  ".c",
  ".dat",
  ".dll",
  ".h",
  ".map",
  ".pdb",
  ".rsp",
  ".ts",
  ".xml",
]);

/** Converts a platform path to forward slashes. */
function toPosix(value) {
  return value.replaceAll(path.sep, "/");
}

/**
 * Lists every file below a directory as sorted root-relative POSIX paths.
 * @param {string} root The directory to list.
 * @returns {string[]} The relative file paths.
 * @throws {Error} When the tree contains a symbolic link or an entry that is neither a file nor a directory.
 */
function listFiles(root) {
  const files = [];

  /** Adds the files of one directory to `files`, recursing into subdirectories in name order. */
  function visit(directory) {
    for (const entry of fs
      .readdirSync(directory, { withFileTypes: true })
      .sort((a, b) => a.name.localeCompare(b.name))) {
      const absolutePath = path.join(directory, entry.name);
      const relativePath = toPosix(path.relative(root, absolutePath));
      const stat = fs.lstatSync(absolutePath);
      if (stat.isSymbolicLink()) {
        throw new Error(`Published output must not contain symbolic links: ${relativePath}`);
      }

      if (entry.isDirectory()) {
        visit(absolutePath);
      } else if (entry.isFile()) {
        files.push(relativePath);
      } else {
        throw new Error(`Published output contains an unsupported entry: ${relativePath}`);
      }
    }
  }

  visit(root);
  return files;
}

/**
 * Reads the resource JSON embedded between the markers of `_framework/dotnet.boot.js`.
 * @param {string} frameworkDirectory The publication's `_framework` directory.
 * @returns {object} The parsed boot configuration.
 * @throws {Error} When the file, its markers or valid JSON between them is missing.
 */
function readBootConfig(frameworkDirectory) {
  const bootPath = path.join(frameworkDirectory, "dotnet.boot.js");
  if (!fs.existsSync(bootPath)) {
    throw new Error("Published output is missing _framework/dotnet.boot.js.");
  }

  const source = fs.readFileSync(bootPath, "utf8");
  const startMarker = "/*json-start*/";
  const endMarker = "/*json-end*/";
  const start = source.indexOf(startMarker);
  const end = source.indexOf(endMarker, start + startMarker.length);
  if (start < 0 || end < 0) {
    throw new Error("dotnet.boot.js does not contain its expected JSON resource markers.");
  }

  try {
    return JSON.parse(source.slice(start + startMarker.length, end).trim());
  } catch (cause) {
    throw new Error("dotnet.boot.js contains invalid resource JSON.", { cause });
  }
}

/**
 * Adds every `name` found anywhere in a boot resource tree to a set.
 * @param {unknown} value A boot configuration value.
 * @param {Set<string>} names Receives the resource file names.
 * @throws {Error} When a name is empty, absolute, contains a path separator, or is `.` or `..`.
 */
function collectResourceNames(value, names) {
  if (Array.isArray(value)) {
    for (const item of value) {
      collectResourceNames(item, names);
    }
    return;
  }

  if (!value || typeof value !== "object") {
    return;
  }

  if (typeof value.name === "string") {
    const name = value.name;
    if (
      name.length === 0 ||
      path.isAbsolute(name) ||
      name.includes("/") ||
      name.includes("\\") ||
      name === "." ||
      name === ".."
    ) {
      throw new Error(`dotnet.boot.js contains an unsafe resource name: '${name}'.`);
    }
    names.add(name);
  }

  for (const child of Object.values(value)) {
    collectResourceNames(child, names);
  }
}

/**
 * Lists the files `_framework` must contain: the runtime entry points and every resource the boot configuration names.
 * @param {string} frameworkDirectory The publication's `_framework` directory.
 * @returns {string[]} The sorted file names.
 * @throws {Error} When the boot configuration is missing, malformed or has no resources object.
 */
export function getRequiredFrameworkFiles(frameworkDirectory) {
  const bootConfig = readBootConfig(frameworkDirectory);
  if (!bootConfig.resources || typeof bootConfig.resources !== "object") {
    throw new Error("dotnet.boot.js does not contain a resources object.");
  }

  const required = new Set(requiredFrameworkEntrypoints);
  collectResourceNames(bootConfig.resources, required);
  return [...required].sort();
}

/**
 * Appends a message for every expected item that is missing and every actual item that is not expected.
 * @param {Set<string>} actual The items found.
 * @param {Set<string>} expected The items required.
 * @param {string} description What an item is, used in the messages.
 * @param {string[]} errors Receives the messages.
 */
function compareSets(actual, expected, description, errors) {
  for (const item of [...expected].sort()) {
    if (!actual.has(item)) {
      errors.push(`Missing ${description}: ${item}`);
    }
  }
  for (const item of [...actual].sort()) {
    if (!expected.has(item)) {
      errors.push(`Unreferenced ${description}: ${item}`);
    }
  }
}

/** Returns the lowercase hexadecimal SHA-256 hash of a file's contents. */
function sha256(filePath) {
  return crypto.createHash("sha256").update(fs.readFileSync(filePath)).digest("hex");
}

/**
 * Validates a WASM publication directory and returns its manifest.
 * @param {string} publicationDirectory The publication root.
 * @param {{maxRawBytes?: number}} [options] `maxRawBytes` bounds the total uncompressed size (default 6 MiB).
 * @returns {object} The manifest: entry point, runtime configuration, each file's path, size in bytes and SHA-256,
 *   totals and limits.
 * @throws {Error} Listing every structural problem, or when the total size exceeds the limit.
 */
export function validateWasmPublication(
  publicationDirectory,
  { maxRawBytes = defaultRawByteLimit } = {},
) {
  const root = path.resolve(publicationDirectory);
  if (!fs.existsSync(root) || !fs.statSync(root).isDirectory()) {
    throw new Error(`WASM publication directory does not exist: ${root}`);
  }

  const errors = [];
  const rootEntries = fs.readdirSync(root, { withFileTypes: true });
  const rootFiles = new Set(
    rootEntries.filter((entry) => entry.isFile()).map((entry) => entry.name),
  );
  const rootDirectories = new Set(
    rootEntries.filter((entry) => entry.isDirectory()).map((entry) => entry.name),
  );
  compareSets(rootFiles, requiredRootFiles, "publication root file", errors);
  compareSets(rootDirectories, new Set(["_framework"]), "publication root directory", errors);

  const frameworkDirectory = path.join(root, "_framework");
  if (fs.existsSync(frameworkDirectory) && fs.statSync(frameworkDirectory).isDirectory()) {
    const expectedFrameworkFiles = getRequiredFrameworkFiles(frameworkDirectory);
    const frameworkEntries = fs.readdirSync(frameworkDirectory, { withFileTypes: true });
    const nestedDirectories = frameworkEntries
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name);
    if (nestedDirectories.length > 0) {
      errors.push(
        `_framework must be flat; found directories: ${nestedDirectories.sort().join(", ")}`,
      );
    }

    const frameworkFiles = new Set(
      frameworkEntries.filter((entry) => entry.isFile()).map((entry) => entry.name),
    );
    compareSets(
      frameworkFiles,
      new Set(expectedFrameworkFiles),
      "_framework boot resource",
      errors,
    );
    for (const fileName of frameworkFiles) {
      const extension = path.extname(fileName).toLowerCase();
      if (rejectedProductionExtensions.has(extension)) {
        errors.push(`Non-deployable production file: _framework/${fileName}`);
      }
    }
  } else {
    errors.push("Missing publication root directory: _framework");
  }

  const mainPath = path.join(root, "main.js");
  if (fs.existsSync(mainPath)) {
    const mainSource = fs.readFileSync(mainPath, "utf8");
    if (!/from\s+["']\.\/_framework\/dotnet\.js["']/.test(mainSource)) {
      errors.push("main.js does not reference ./_framework/dotnet.js.");
    }
    if (!/from\s+["']\.\/bootstrap\.js["']/.test(mainSource)) {
      errors.push("main.js does not reference ./bootstrap.js.");
    }
  }

  const runtimeConfigPath = path.join(root, runtimeConfigName);
  if (fs.existsSync(runtimeConfigPath)) {
    try {
      JSON.parse(fs.readFileSync(runtimeConfigPath, "utf8"));
    } catch {
      errors.push(`${runtimeConfigName} is not valid JSON.`);
    }
  }

  const files = listFiles(root);
  for (const relativePath of files) {
    if (relativePath.startsWith("_framework/")) {
      continue;
    }
    if (!requiredRootFiles.has(relativePath)) {
      const extension = path.extname(relativePath).toLowerCase();
      errors.push(
        rejectedProductionExtensions.has(extension)
          ? `Non-deployable production file: ${relativePath}`
          : `Unreferenced publication file: ${relativePath}`,
      );
    }
  }

  if (errors.length > 0) {
    throw new Error(`Invalid WASM publication:\n- ${[...new Set(errors)].join("\n- ")}`);
  }

  const entries = files.map((relativePath) => {
    const filePath = path.join(root, ...relativePath.split("/"));
    return {
      path: relativePath,
      bytes: fs.statSync(filePath).size,
      sha256: sha256(filePath),
    };
  });
  const totalBytes = entries.reduce((sum, entry) => sum + entry.bytes, 0);
  if (totalBytes > maxRawBytes) {
    throw new Error(
      `WASM publication is ${totalBytes} bytes, above the ${maxRawBytes}-byte raw payload limit.`,
    );
  }

  return {
    schemaVersion: 1,
    entrypoint: "main.js",
    runtimeConfig: runtimeConfigName,
    files: entries,
    totals: {
      files: entries.length,
      bytes: totalBytes,
    },
    limits: {
      rawBytes: maxRawBytes,
    },
  };
}
