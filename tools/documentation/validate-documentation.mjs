#!/usr/bin/env node
/**
 * The complete documentation gate: ignored-input guard, recipe export, repository Markdown links, the build
 * wrapper, the API/language/canonical/feature-matrix/quality/workflow/Pages validators, language fixture tests on
 * both frameworks, the documentation and memory examples, the docs Node checks (audit, YAML, Markdown, spelling,
 * browser), and the structural checks on DocFX configuration, pages, TOCs, reachability, tracked sources,
 * published contracts, generated pages, the search index, root-absolute URLs, and the site budget.
 *
 *   node tools/documentation/validate-documentation.mjs [--no-build] [--self-test]
 */
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand, runDotnet } from "../lib/tooling.mjs";
import { isFile, isIgnored, lines, listFiles, repositoryFiles, toPosix } from "../lib/files.mjs";
import { apiDirectory, documentationRoot, documentationSourceFiles, relativeLinkTargets, resolveTarget, siteDirectory, sourcePages, sourceTocs, tocHrefs } from "../lib/docs.mjs";
import { findAll, parseXml } from "../lib/xml.mjs";

const options = parseArguments(process.argv.slice(2), { "no-build": "flag", "self-test": "flag" }, { defaults: { "no-build": false, "self-test": false } });
const here = path.join(repositoryRoot, "tools/documentation");
const scripts = {
  build: path.join(here, "build-documentation.mjs"),
  api: path.join(here, "validate-api-documentation.mjs"),
  language: path.join(here, "validate-language-documentation.mjs"),
  quality: path.join(here, "validate-documentation-quality.mjs"),
  externalLinks: path.join(here, "test-documentation-external-links.mjs"),
  workflow: path.join(here, "validate-documentation-workflow.mjs"),
  pages: path.join(here, "validate-pages-artifact.mjs"),
  pagesArtifact: path.join(here, "new-documentation-pages-artifact.mjs"),
  canonical: path.join(here, "validate-canonical-reference.mjs"),
  generatorDiagnostics: path.join(here, "validate-generator-diagnostics.mjs"),
  featureMatrix: path.join(repositoryRoot, "tools/quality/feature-operation-matrix.mjs"),
};
const docfxConfigPath = path.join(documentationRoot, "docfx.json");
const toolManifestPath = path.join(repositoryRoot, ".config/dotnet-tools.json");
const coreProjectPath = path.join(repositoryRoot, "src/CStructSharp/CStructSharp.csproj");
const testProjectPath = path.join(repositoryRoot, "tests/CStructSharpTests/CStructSharpTests.csproj");
const exampleProjectPath = path.join(documentationRoot, "examples/CStructSharp.Docs.Examples.csproj");
const SITE_BUDGET_BYTES = 32 * 1024 * 1024;
const TEXT_EXTENSIONS = new Set([".cs", ".csproj", ".js", ".json", ".md", ".mjs", ".props", ".ps1", ".targets", ".ts", ".txt", ".xml", ".yaml", ".yml"]);
// The directory names are assembled so this file does not itself match the guard it implements.
const IGNORED_DIRECTORIES = ["agent" + "docs", ".local-" + "docs"];
const IGNORED_DOCS_PATTERN = new RegExp(`(?:^|[\\s"'\`()=:])(?:${IGNORED_DIRECTORIES.map((name) => name.replace(".", "\\.")).join("|")})[\\\\/]`, "i");

function runNode(script, args = [], failure) {
  console.log(`==> ${script}${args.length ? ` ${args.join(" ")}` : ""}`);
  const result = runCommand(process.execPath, [script, ...args], { allowFailure: true });
  process.stdout.write(result.stdout ?? "");
  process.stderr.write(result.stderr ?? "");
  assertCondition(result.status === 0, failure);
}

function runNpm(args, cwd, failure) {
  console.log(`==> npm ${args.join(" ")}`);
  const result = runCommand(process.platform === "win32" ? "npm.cmd" : "npm", args, { cwd, allowFailure: true });
  process.stdout.write(result.stdout ?? "");
  process.stderr.write(result.stderr ?? "");
  assertCondition(result.status === 0, failure);
}

/** Repository source that names the ignored local-documentation directories. */
function ignoredDocumentationDependencies() {
  const violations = [];
  for (const relative of repositoryFiles(repositoryRoot)) {
    const normalized = toPosix(relative);
    // Archived agent reports are diagnostic evidence, not inputs to the library, application, package or
    // documentation builds.
    if (normalized.startsWith(`${IGNORED_DIRECTORIES[0]}/`)) continue;
    if (!TEXT_EXTENSIONS.has(path.extname(relative))) continue;
    const fullPath = path.join(repositoryRoot, relative);
    if (!isFile(fullPath)) continue;
    lines(fs.readFileSync(fullPath, "utf8")).forEach((line, index) => {
      if (IGNORED_DOCS_PATTERN.test(line)) violations.push(`${normalized}:${index + 1}:${line.trim()}`);
    });
  }
  return violations;
}

function brokenRepositoryMarkdownLinks() {
  const broken = [];
  for (const relative of repositoryFiles(repositoryRoot).filter((file) => path.extname(file) === ".md")) {
    const fullPath = path.join(repositoryRoot, relative);
    if (!isFile(fullPath)) continue;
    for (const href of relativeLinkTargets(fs.readFileSync(fullPath, "utf8"))) {
      if (!fs.existsSync(path.resolve(path.dirname(fullPath), href))) broken.push(`${relative} -> ${href}`);
    }
  }
  return broken;
}

await main(() => {
  if (options["self-test"]) {
    const testName = `ignored-dependency-self-test-${crypto.randomUUID().replaceAll("-", "")}.txt`;
    const productionProbe = path.join(here, testName);
    try {
      fs.writeFileSync(productionProbe, `${IGNORED_DIRECTORIES[0]}/prohibited-build-input.md\n`);
      const violations = ignoredDocumentationDependencies();
      assertCondition(violations.filter((item) => item.startsWith(`tools/documentation/${testName}:`)).length === 1, "The ignored-input guard must reject production dependencies.");
    } finally {
      fs.rmSync(productionProbe, { force: true });
    }
    let caught = false;
    try {
      assertCondition(false, "expected self-test failure");
    } catch (error) {
      caught = error.message === "expected self-test failure";
    }
    assertCondition(caught, "The documentation assertion self-test did not observe the expected failure.");
    runNode(scripts.build, ["--self-test"], "The documentation build self-test failed.");
    runNode(scripts.quality, ["--self-test"], "The documentation quality self-test failed.");
    runNode(scripts.externalLinks, ["--self-test"], "The external-link self-test failed.");
    runNode(scripts.workflow, ["--self-test"], "The documentation workflow self-test failed.");
    runNode(scripts.pages, ["--self-test"], "The Pages artifact self-test failed.");
    console.log("Validate-Documentation self-test passed.");
    return;
  }

  const ignored = ignoredDocumentationDependencies();
  assertCondition(ignored.length === 0, `Repository source still depends on ignored local-documentation paths:\n${ignored.join("\n")}`);
  runNode(path.join(here, "export-documentation-examples.mjs"), [], "Recipe generation failed.");
  const generatedRecipes = JSON.parse(fs.readFileSync(path.join(documentationRoot, "generated-files.json"), "utf8"));
  assertCondition(generatedRecipes.length === 82, "Expected all 82 recipe exports (40 recipes, a .cs and a .md each, plus the recipe catalog and its toc).");
  for (const generated of generatedRecipes) assertCondition(fs.existsSync(path.join(documentationRoot, generated)), `Missing recipe export: ${generated}`);
  const broken = brokenRepositoryMarkdownLinks();
  assertCondition(broken.length === 0, `Repository Markdown contains missing local link targets:\n${broken.join("\n")}`);
  for (const required of [...Object.values(scripts), docfxConfigPath, toolManifestPath, coreProjectPath, testProjectPath, exampleProjectPath, path.join(documentationRoot, "package.json"), path.join(documentationRoot, "package-lock.json")]) {
    assertCondition(fs.existsSync(required), `Required validation input does not exist: ${required}`);
  }

  runNode(scripts.build, options["no-build"] ? ["--no-build", "--clean"] : [], "The documentation build wrapper failed.");
  runNode(scripts.api, [], "Generated API documentation validation failed.");
  runNode(scripts.language, [], "Language documentation validation failed.");
  runNode(scripts.canonical, [], "Canonical Portable reference validation failed.");
  runNode(scripts.generatorDiagnostics, ["--check"], "Generator diagnostics documentation validation failed.");
  runNode(scripts.featureMatrix, [], "Feature-operation matrix validation failed.");
  runNode(scripts.quality, [], "Documentation quality validation failed.");
  runNode(scripts.workflow, [], "Documentation workflow validation failed.");
  runNode(scripts.pages, [], "Pages output validation failed.");

  runDotnet(["restore", testProjectPath], { label: "language fixture restore" });
  for (const framework of ["net8.0", "net10.0"]) {
    runDotnet(["test", testProjectPath, "-c", "Release", "-f", framework, "--no-restore", "--filter", "FullyQualifiedName~ManualLanguageFixtureTests|FullyQualifiedName~CanonicalPortableReferenceTests"], { label: `language fixtures ${framework}` });
  }
  runDotnet(["restore", exampleProjectPath], { label: "documentation example restore" });
  runDotnet(["run", "--project", exampleProjectPath, "-c", "Release", "--no-restore"], { label: "documentation examples" });
  // The memory articles include regions from this runner; execute their assertions on both supported runtimes.
  for (const framework of ["net8.0", "net10.0"]) {
    runDotnet(["run", "--project", path.join(documentationRoot, "examples/memory-analysis/MemoryAnalysis.csproj"), "-c", "Release", "-f", framework], { label: `memory documentation examples ${framework}` });
  }
  runNpm(["ci", "--ignore-scripts"], documentationRoot, "Pinned documentation Node dependency restore failed.");
  runNpm(["audit", "--audit-level=high"], documentationRoot, "Documentation Node dependency audit failed.");
  for (const script of ["lint:workflow-yaml", "lint:markdown", "lint:spelling", "install:browser", "test:browser"]) {
    runNpm(["run", script], documentationRoot, `Documentation Node script '${script}' failed.`);
  }

  const docfxConfigText = fs.readFileSync(docfxConfigPath, "utf8");
  const docfxConfig = JSON.parse(docfxConfigText);
  const toolManifest = JSON.parse(fs.readFileSync(toolManifestPath, "utf8"));
  const coreProject = parseXml(fs.readFileSync(coreProjectPath, "utf8"));
  const exampleProject = parseXml(fs.readFileSync(exampleProjectPath, "utf8"));
  assertCondition(toolManifest.tools.docfx.version === "2.78.5", "DocFX must remain pinned to reviewed version 2.78.5.");
  assertCondition(!toolManifest.tools.docfx.rollForward, "DocFX tool roll-forward must remain disabled.");
  // The generator is packed from the core project without being referenced as an assembly (ReferenceOutputAssembly=false),
  // so it adds nothing to the API metadata; any other project reference would.
  const apiReferences = findAll(coreProject, "ProjectReference").filter((reference) => String(reference.attributes.ReferenceOutputAssembly ?? "true").toLowerCase() !== "false");
  assertCondition(apiReferences.length === 0, "The API input project must not acquire a project reference that contributes an assembly.");
  // The examples reference the generator as an analyzer only (ReferenceOutputAssembly=false), the way the package consumes it.
  const exampleReferences = findAll(exampleProject, "ProjectReference").filter((reference) => String(reference.attributes.ReferenceOutputAssembly ?? "true").toLowerCase() !== "false");
  assertCondition(exampleReferences.length === 1 && exampleReferences[0].attributes.Include === "..\\..\\src\\CStructSharp\\CStructSharp.csproj", "The documentation examples must reference only the core project as an assembly.");
  const metadataSources = docfxConfig.metadata.flatMap((entry) => (Array.isArray(entry.src) ? entry.src : [entry.src])).map((source) => source.src);
  assertCondition(metadataSources.length === 1, "DocFX must have exactly one managed metadata source root.");
  assertCondition(metadataSources[0] === "../src/CStructSharp/bin/Release/net10.0", `Unexpected DocFX metadata source '${metadataSources[0]}'.`);
  assertCondition(!/apps\/explorer/i.test(docfxConfigText), "DocFX configuration must not select CStructSharpWeb or CStructSharpWeb.Wasm.");
  assertCondition(docfxConfig.build.globalMetadata._enableSearch === true, "DocFX local search must remain enabled.");

  const pages = sourcePages();
  const titles = new Map();
  for (const page of pages) {
    const text = fs.readFileSync(page, "utf8");
    assertCondition(/^---\r?\n[\s\S]*?\r?\n---\r?\n/.test(text), `Content page lacks YAML front matter: ${page}`);
    const title = /^title:\s*(.+?)\s*$/m.exec(text)?.[1]?.trim();
    assertCondition(title, `Content page lacks a title: ${page}`);
    const key = title.toLowerCase();
    assertCondition(!titles.has(key), `Duplicate documentation title '${title}': '${titles.get(key)}' and '${page}'.`);
    titles.set(key, page);
    assertCondition((text.match(/^#\s+/gm) ?? []).length === 1, `Content page must have exactly one H1: ${page}`);
    assertCondition(!/\b(?:TODO|TBD|FIXME|lorem ipsum|coming soon)\b/i.test(text), `Content page contains a prohibited placeholder: ${page}`);
  }
  const tocs = sourceTocs();
  const reachable = new Set();
  for (const toc of tocs) {
    const destinations = new Set();
    for (const href of tocHrefs(toc)) {
      const target = resolveTarget(toc, href);
      assertCondition(fs.existsSync(target), `TOC target '${href}' does not exist for '${toc}'.`);
      assertCondition(!destinations.has(target.toLowerCase()), `TOC '${toc}' contains duplicate destination '${href}'.`);
      destinations.add(target.toLowerCase());
      if (path.extname(target) === ".md") reachable.add(target.toLowerCase());
    }
  }
  for (const page of pages) {
    for (const href of relativeLinkTargets(fs.readFileSync(page, "utf8"))) {
      const target = resolveTarget(page, href);
      if (path.extname(target) === ".md" && isFile(target)) reachable.add(target.toLowerCase());
    }
  }
  const rootPage = path.join(documentationRoot, "index.md");
  const notFoundPage = path.join(documentationRoot, "404.md");
  for (const page of pages) {
    assertCondition(page === rootPage || page === notFoundPage || reachable.has(page.toLowerCase()), `Documentation page is orphaned from authored navigation and links: ${page}`);
  }
  const generatedSet = new Set(generatedRecipes);
  for (const sourceFile of documentationSourceFiles()) {
    if (generatedSet.has(toPosix(path.relative(documentationRoot, sourceFile)))) continue;
    const relative = toPosix(path.relative(repositoryRoot, sourceFile));
    assertCondition(!isIgnored(repositoryRoot, relative), `Documentation source is ignored and cannot be committed: ${relative}`);
  }
  for (const generated of [path.join(siteDirectory, "index.html"), path.join(apiDirectory, "CStructSharp.yml"), path.join(documentationRoot, "docfx-build.log")]) {
    assertCondition(fs.existsSync(generated), `Expected generated file is missing: ${generated}`);
    assertCondition(isIgnored(repositoryRoot, toPosix(path.relative(repositoryRoot, generated))), `Generated documentation file is not ignored: ${toPosix(path.relative(repositoryRoot, generated))}`);
  }

  const contractDirectory = path.join(repositoryRoot, "contracts");
  const contractSources = listFiles(contractDirectory, (file) => [".json", ".txt"].includes(path.extname(file)));
  const expectedContracts = JSON.parse(fs.readFileSync(path.join(contractDirectory, "published-files.json"), "utf8")).map(String).sort();
  const actualContracts = contractSources.map((file) => toPosix(path.relative(contractDirectory, file))).filter((file) => file !== "published-files.json").sort();
  assertCondition(expectedContracts.join("\n") === actualContracts.join("\n"), "Published contract inventory differs from required inputs.");
  for (const source of contractSources) {
    const relative = `contracts/${toPosix(path.relative(contractDirectory, source))}`;
    const published = path.join(siteDirectory, relative);
    assertCondition(isFile(published), `Published documentation contract is missing: ${relative}`);
    const hash = (file) => crypto.createHash("sha256").update(fs.readFileSync(file)).digest("hex");
    assertCondition(hash(source) === hash(published), `Published documentation contract differs from its source: ${relative}`);
  }
  const apiPages = fs.readdirSync(path.join(siteDirectory, "api")).filter((name) => name.endsWith(".html"));
  assertCondition(apiPages.length >= 21, `Expected at least 21 generated API pages, found ${apiPages.length}.`);
  const searchIndexPath = path.join(siteDirectory, "index.json");
  assertCondition(fs.existsSync(searchIndexPath), "Generated search index is missing.");
  const searchIndex = JSON.parse(fs.readFileSync(searchIndexPath, "utf8"));
  for (const expected of [
    "index.html",
    "project/index.html",
    "guides/index.html",
    "language/index.html",
    "language/portable-v1-reference.html",
    "language/tutorial/01-first-layout.html",
    "language/operation-matrix.html",
    "examples/index.html",
    "api/browser-contract.html",
    "api/CStructSharp.CStruct.html",
  ]) {
    assertCondition(Object.hasOwn(searchIndex, expected), `Search index does not contain '${expected}'.`);
  }
  const rootAbsolute = listFiles(siteDirectory, (file) => file.endsWith(".html")).filter((file) => /(?:href|src)="\//.test(fs.readFileSync(file, "utf8")));
  assertCondition(rootAbsolute.length === 0, "Generated HTML contains root-absolute asset or content URLs.");
  const siteFiles = listFiles(siteDirectory);
  const siteBytes = siteFiles.reduce((sum, file) => sum + fs.statSync(file).size, 0);
  assertCondition(siteBytes <= SITE_BUDGET_BYTES, `Documentation artifact exceeds the initial 32 MiB budget: ${siteBytes} bytes.`);
  console.log(`Documentation validation passed: ${pages.length} source pages, ${tocs.length} source TOCs, ${apiPages.length} API pages, ${siteFiles.length} site files, ${siteBytes.toLocaleString("en-US")} bytes.`);
});
