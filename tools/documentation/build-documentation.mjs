#!/usr/bin/env node
/**
 * Builds the documentation: restores the pinned DocFX tool, exports the recipes, builds the core library (or
 * verifies its output is current with --no-build), runs DocFX metadata + content (content only when the metadata
 * is current), enforces the DocFX time and site size budgets, and optionally rewrites explorer links for a
 * preview and serves the site. `--self-test` proves the Web/WASM command guard and the generated-path guard.
 *
 *   node tools/documentation/build-documentation.mjs [--no-build] [--clean] [--serve] [--port 8080]
 *     [--explorer-url <https://host/path/>] [--self-test]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand, runDotnet } from "../lib/tooling.mjs";
import { isFile, listFiles } from "../lib/files.mjs";

const options = parseArguments(
  process.argv.slice(2),
  { "no-build": "flag", clean: "flag", serve: "flag", port: "number", "explorer-url": "string", "self-test": "flag" },
  { defaults: { "no-build": false, clean: false, serve: false, port: 8080, "self-test": false } },
);
assertCondition(Number.isInteger(options.port) && options.port >= 1 && options.port <= 65535, "--port must be between 1 and 65535.");
const coreDirectory = path.join(repositoryRoot, "src/CStructSharp");
const coreProject = path.join(coreDirectory, "CStructSharp.csproj");
const coreOutput = path.join(coreDirectory, "bin/Release/net10.0");
const coreAssembly = path.join(coreOutput, "CStructSharp.dll");
const documentationRoot = path.join(repositoryRoot, "docs");
const docfxConfig = path.join(documentationRoot, "docfx.json");
const apiDirectory = path.join(documentationRoot, "api");
const siteDirectory = path.join(documentationRoot, "_site");
const SITE_BUDGET_BYTES = 32 * 1024 * 1024;
process.env.DOTNET_CLI_TELEMETRY_OPTOUT = "1";
process.env.DOTNET_NOLOGO = "1";

export function assertNoWebCommand(args) {
  const commandText = args.join(" ");
  if (/(^|[\\/])CStructSharpWeb(?:[\\/.]|$)/i.test(commandText)) throw new Error(`Documentation commands must not target CStructSharpWeb or CStructSharpWeb.Wasm: dotnet ${commandText}`);
}

function dotnet(args, label) {
  assertNoWebCommand(args);
  return runDotnet(args, { label });
}

export function assertSafeGeneratedDirectory(candidate, expectedLeaf) {
  const fullPath = path.resolve(candidate);
  const relative = path.relative(path.resolve(documentationRoot), fullPath);
  assertCondition(!path.isAbsolute(relative), `Generated path must be below the docs project: ${fullPath}`);
  assertCondition(!relative.startsWith(".."), `Generated path escapes the docs project: ${fullPath}`);
  assertCondition(path.basename(fullPath) === expectedLeaf, `Generated path has unexpected leaf '${path.basename(fullPath)}': ${fullPath}`);
}

function removeGeneratedSite() {
  assertSafeGeneratedDirectory(siteDirectory, "_site");
  if (fs.existsSync(siteDirectory)) {
    console.log(`==> removing generated site ${siteDirectory}`);
    fs.rmSync(siteDirectory, { recursive: true, force: true });
  }
}

function removeGeneratedApiMetadata() {
  assertSafeGeneratedDirectory(apiDirectory, "api");
  if (!fs.existsSync(apiDirectory)) return;
  for (const name of fs.readdirSync(apiDirectory)) {
    if (!name.endsWith(".yml")) continue;
    const fullPath = path.join(apiDirectory, name);
    assertCondition(path.dirname(fullPath) === path.resolve(apiDirectory), `Refusing to remove API metadata outside the generated API directory: ${fullPath}`);
    console.log(`==> removing generated API metadata ${fullPath}`);
    fs.rmSync(fullPath, { force: true });
  }
}

function assertCurrentCoreOutput() {
  for (const required of [coreAssembly, path.join(coreOutput, "CStructSharp.xml"), path.join(coreOutput, "CStructSharp.pdb")]) {
    assertCondition(isFile(required), `Fast documentation build requires '${required}'. Run without --no-build first.`);
  }
  const assemblyTime = fs.statSync(coreAssembly).mtimeMs;
  const newer = listFiles(coreDirectory, (file) => [".cs", ".csproj"].includes(path.extname(file)) && !/[/\\](?:bin|obj)[/\\]/.test(file)).find((file) => fs.statSync(file).mtimeMs > assemblyTime);
  if (newer) throw new Error(`The core assembly is older than source input '${newer}'. Run without --no-build.`);
}

await main(() => {
  if (options["explorer-url"]) {
    let url;
    try {
      url = new URL(options["explorer-url"]);
    } catch {
      url = null;
    }
    if (!url || !["http:", "https:"].includes(url.protocol) || url.search || url.hash || url.username || url.password) {
      throw new Error("ExplorerUrl must be an absolute HTTP(S) directory URL without credentials, query, or fragment.");
    }
  }
  if (options["self-test"]) {
    let rejected = false;
    try {
      assertNoWebCommand(["build", path.join(repositoryRoot, "src/CStructSharp.Wasm/CStructSharpWeb.Wasm.csproj")]);
    } catch {
      rejected = true;
    }
    assertCondition(rejected, "The Web/WASM command guard did not reject a forbidden project.");
    assertNoWebCommand(["build", coreProject, "-f", "net10.0"]);
    assertSafeGeneratedDirectory(siteDirectory, "_site");
    assertSafeGeneratedDirectory(apiDirectory, "api");
    console.log("Build-Documentation self-test passed.");
    return;
  }
  for (const required of [coreProject, docfxConfig, apiDirectory]) assertCondition(fs.existsSync(required), `Required documentation input does not exist: ${required}`);
  dotnet(["tool", "restore"], "tool restore");
  const exported = runCommand(process.execPath, [path.join(repositoryRoot, "tools/documentation/export-documentation-examples.mjs")], { allowFailure: true });
  process.stdout.write(exported.stdout ?? "");
  assertCondition(exported.status === 0, "Documentation example export failed.");
  const noBuild = options["no-build"];
  if (options.clean || !noBuild) removeGeneratedSite();
  if (noBuild) {
    assertCurrentCoreOutput();
  } else {
    removeGeneratedApiMetadata();
    dotnet(["restore", coreProject], "core restore");
    dotnet(["build", coreProject, "-c", "Release", "-f", "net10.0", "--no-restore"], "core build");
  }
  const apiMetadata = fs.existsSync(apiDirectory) ? fs.readdirSync(apiDirectory).filter((name) => name.endsWith(".yml")).map((name) => path.join(apiDirectory, name)) : [];
  let metadataIsStale = apiMetadata.length === 0;
  if (!metadataIsStale) {
    const newest = Math.max(...apiMetadata.map((file) => fs.statSync(file).mtimeMs));
    metadataIsStale = newest < fs.statSync(coreAssembly).mtimeMs;
  }
  let docfxSeconds;
  let docfxBudgetSeconds;
  if (noBuild && !metadataIsStale) {
    docfxSeconds = dotnet(["tool", "run", "docfx", "build", docfxConfig, "--warningsAsErrors", "--log", path.join(documentationRoot, "docfx-content.log")], "DocFX content build");
    docfxBudgetSeconds = 5;
  } else {
    docfxSeconds = dotnet(["tool", "run", "docfx", docfxConfig, "--warningsAsErrors", "--log", path.join(documentationRoot, "docfx-build.log")], "DocFX metadata and content build");
    // Hosted runners can be slower than a warm local build. Keep this as a regression guard without making a valid
    // release depend on runner load.
    docfxBudgetSeconds = 30;
  }
  if (options["explorer-url"]) {
    // Rewrite only generated preview links; source and publication defaults stay canonical.
    const previewExplorer = `${options["explorer-url"].replace(/\/+$/, "")}/`;
    for (const page of listFiles(siteDirectory, (file) => file.endsWith(".html"))) {
      const content = fs.readFileSync(page, "utf8");
      const updated = content.replaceAll("https://vvollers.github.io/cstructsharp/explorer/", previewExplorer);
      if (updated !== content) fs.writeFileSync(page, updated);
    }
  }
  const siteFiles = listFiles(siteDirectory);
  const siteBytes = siteFiles.reduce((sum, file) => sum + fs.statSync(file).size, 0);
  assertCondition(docfxSeconds <= docfxBudgetSeconds, `DocFX exceeded the ${docfxBudgetSeconds} second budget: ${docfxSeconds.toFixed(3)} seconds.`);
  assertCondition(siteBytes <= SITE_BUDGET_BYTES, `Documentation artifact exceeds the 32 MiB budget: ${siteBytes} bytes.`);
  console.log(`Documentation artifact: ${siteFiles.length} files, ${siteBytes.toLocaleString("en-US")} bytes; DocFX ${docfxSeconds.toFixed(3)}/${docfxBudgetSeconds} s budget.`);
  if (options.serve) {
    dotnet(["tool", "run", "docfx", "serve", siteDirectory, "--hostname", "localhost", "--port", String(options.port)], "DocFX local server");
  }
});
