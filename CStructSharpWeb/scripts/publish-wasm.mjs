import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { fileURLToPath } from "node:url";
import {
  getRequiredFrameworkFiles,
  runtimeConfigName,
  validateWasmPublication,
} from "./wasm-publication.mjs";

const scriptDirectory = path.dirname(fileURLToPath(import.meta.url));
const webRoot = path.resolve(scriptDirectory, "..");
const repositoryRoot = path.resolve(webRoot, "..");
const projectPath = path.join(webRoot, "wasm", "CStructSharpWeb.Wasm.csproj");
const appBundle = path.join(
  webRoot,
  "wasm",
  "bin",
  "Release",
  "net10.0",
  "browser-wasm",
  "AppBundle",
);
const sourceFramework = path.join(appBundle, "_framework");
const publicRoot = path.join(webRoot, "public");
const manifestPath = path.join(repositoryRoot, "artifacts", "baseline", "wasm-publication.json");

function pathsEqual(first, second) {
  const normalizedFirst = path.resolve(first);
  const normalizedSecond = path.resolve(second);
  return process.platform === "win32"
    ? normalizedFirst.toLowerCase() === normalizedSecond.toLowerCase()
    : normalizedFirst === normalizedSecond;
}

function resolveSafeDestination() {
  fs.mkdirSync(publicRoot, { recursive: true });
  const resolvedPublicRoot = fs.realpathSync.native(publicRoot);
  const destination = path.join(resolvedPublicRoot, "wasm");
  if (
    !pathsEqual(path.dirname(destination), resolvedPublicRoot) ||
    path.basename(destination) !== "wasm"
  ) {
    throw new Error(`Refusing to publish outside the expected public directory: ${destination}`);
  }

  if (fs.existsSync(destination)) {
    const stat = fs.lstatSync(destination);
    if (stat.isSymbolicLink() || !stat.isDirectory()) {
      throw new Error(
        `Refusing to replace a non-directory or symbolic-link destination: ${destination}`,
      );
    }
    if (!pathsEqual(fs.realpathSync.native(destination), destination)) {
      throw new Error(`Refusing to replace a destination that resolves elsewhere: ${destination}`);
    }
  }

  return { destination, resolvedPublicRoot };
}

function runPublish(temporaryPublishDirectory) {
  const result = spawnSync(
    "dotnet",
    [
      "publish",
      projectPath,
      "-c",
      "Release",
      "--nologo",
      "-o",
      temporaryPublishDirectory,
      "-p:WasmDebugLevel=0",
      "-p:WasmEmitSourceMap=false",
    ],
    {
      cwd: repositoryRoot,
      stdio: "inherit",
      shell: false,
    },
  );
  if (result.error) {
    throw result.error;
  }
  if (result.status !== 0) {
    throw new Error(`dotnet publish failed with exit code ${result.status}.`);
  }
}

function copyFile(source, destination) {
  const stat = fs.lstatSync(source);
  if (stat.isSymbolicLink() || !stat.isFile()) {
    throw new Error(`Publication source must be a regular file: ${source}`);
  }
  fs.mkdirSync(path.dirname(destination), { recursive: true });
  fs.copyFileSync(source, destination);
}

function stagePublication(stagingDirectory) {
  if (!fs.existsSync(sourceFramework) || !fs.statSync(sourceFramework).isDirectory()) {
    throw new Error(`dotnet publish did not produce its AppBundle framework: ${sourceFramework}`);
  }

  const stagedFramework = path.join(stagingDirectory, "_framework");
  fs.mkdirSync(stagedFramework, { recursive: true });
  for (const fileName of getRequiredFrameworkFiles(sourceFramework)) {
    copyFile(path.join(sourceFramework, fileName), path.join(stagedFramework, fileName));
  }

  copyFile(path.join(webRoot, "wasm", "main.js"), path.join(stagingDirectory, "main.js"));
  copyFile(path.join(webRoot, "wasm", "bootstrap.js"), path.join(stagingDirectory, "bootstrap.js"));
  copyFile(path.join(appBundle, runtimeConfigName), path.join(stagingDirectory, runtimeConfigName));
  return validateWasmPublication(stagingDirectory);
}

function copyDirectory(source, destination) {
  fs.mkdirSync(destination, { recursive: true });
  for (const entry of fs.readdirSync(source, { withFileTypes: true })) {
    const sourcePath = path.join(source, entry.name);
    const destinationPath = path.join(destination, entry.name);
    if (entry.isDirectory()) {
      copyDirectory(sourcePath, destinationPath);
    } else if (entry.isFile()) {
      copyFile(sourcePath, destinationPath);
    } else {
      throw new Error(`Staged publication contains an unsupported entry: ${sourcePath}`);
    }
  }
}

function replaceDestination(stagedPublication, destination, resolvedPublicRoot) {
  const identifier = crypto.randomUUID();
  const deploymentStage = path.join(resolvedPublicRoot, `wasm-stage-${identifier}`);
  const backup = path.join(resolvedPublicRoot, `wasm-backup-${identifier}`);
  let destinationMoved = false;
  let deploymentMoved = false;

  try {
    copyDirectory(stagedPublication, deploymentStage);
    validateWasmPublication(deploymentStage);

    if (fs.existsSync(destination)) {
      fs.renameSync(destination, backup);
      destinationMoved = true;
    }
    fs.renameSync(deploymentStage, destination);
    deploymentMoved = true;
    validateWasmPublication(destination);

    if (destinationMoved) {
      fs.rmSync(backup, { recursive: true, force: true });
    }
  } catch (cause) {
    if (deploymentMoved && fs.existsSync(destination)) {
      fs.rmSync(destination, { recursive: true, force: true });
      deploymentMoved = false;
    }
    if (destinationMoved && !fs.existsSync(destination) && fs.existsSync(backup)) {
      fs.renameSync(backup, destination);
    }
    throw cause;
  } finally {
    if (!deploymentMoved && fs.existsSync(deploymentStage)) {
      fs.rmSync(deploymentStage, { recursive: true, force: true });
    }
  }
}

const temporaryRoot = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-wasm-publish-"));
try {
  const temporaryPublishDirectory = path.join(temporaryRoot, "publish");
  const stagedPublication = path.join(temporaryRoot, "staged");
  fs.mkdirSync(stagedPublication);

  runPublish(temporaryPublishDirectory);
  stagePublication(stagedPublication);

  const { destination, resolvedPublicRoot } = resolveSafeDestination();
  replaceDestination(stagedPublication, destination, resolvedPublicRoot);
  const manifest = validateWasmPublication(destination);

  fs.mkdirSync(path.dirname(manifestPath), { recursive: true });
  fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`, "utf8");
  console.log(
    `Published ${manifest.totals.files} WASM files (${manifest.totals.bytes} bytes) to ${destination}.`,
  );
  console.log(`Publication manifest: ${manifestPath}`);
} finally {
  fs.rmSync(temporaryRoot, { recursive: true, force: true });
}
