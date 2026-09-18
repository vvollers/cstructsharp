#!/usr/bin/env node
/**
 * Validates the generated Pages site (and optionally its .tar.gz archive) against contracts/documentation/pages-v1.json:
 * required paths, no symbolic links, size budgets, a canonical sitemap, source/edit links, README and package
 * release-note links, contributor and pull-request documentation guidance, no project-owned cache layer, and safe
 * archive entries. `--self-test` proves the archive-entry rule.
 *
 *   node tools/documentation/validate-pages-artifact.mjs [--site-directory docs/_site] [--artifact-path <tar.gz>] [--self-test]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";
import { isDirectory, isFile, listFiles } from "../lib/files.mjs";
import { findAll, parseXml } from "../lib/xml.mjs";

const options = parseArguments(process.argv.slice(2), { "site-directory": "string", "artifact-path": "string", "self-test": "flag" }, {
  defaults: { "site-directory": path.join(repositoryRoot, "docs/_site"), "self-test": false },
});
const documentationRoot = path.join(repositoryRoot, "docs");
const contractPath = path.join(repositoryRoot, "contracts/documentation/pages-v1.json");

export function assertSafeArchiveEntries(entries) {
  for (const entry of entries) {
    let normalized = entry.replaceAll("\\", "/");
    if (normalized === "./") continue;
    while (normalized.startsWith("./")) normalized = normalized.slice(2);
    assertCondition(normalized.trim() !== "", "Pages archive contains an empty entry.");
    assertCondition(!path.isAbsolute(entry) && !/^[A-Za-z]:/.test(entry), `Pages archive contains a rooted entry: ${entry}`);
    assertCondition(!/(^|\/)\.\.(\/|$)/.test(normalized), `Pages archive contains a parent traversal: ${entry}`);
  }
}

function listAllEntries(directory) {
  const entries = [];
  const visit = (current) => {
    for (const entry of fs.readdirSync(current, { withFileTypes: true })) {
      const full = path.join(current, entry.name);
      entries.push({ path: full, symlink: entry.isSymbolicLink(), directory: entry.isDirectory() });
      if (entry.isDirectory() && !entry.isSymbolicLink()) visit(full);
    }
  };
  visit(directory);
  return entries;
}

await main(() => {
  if (options["self-test"]) {
    let rejected = false;
    try {
      assertSafeArchiveEntries(["../outside.txt"]);
    } catch {
      rejected = true;
    }
    assertCondition(rejected, "Pages archive traversal fail-first fixture was not rejected.");
    assertSafeArchiveEntries(["./index.html", "./api/CStructSharp.CStruct.html"]);
    console.log("Pages artifact self-test passed: traversal rejected and safe entries accepted.");
    return;
  }
  const siteDirectory = path.resolve(options["site-directory"]);
  assertCondition(isFile(contractPath), `Pages contract does not exist: ${contractPath}`);
  const contract = JSON.parse(fs.readFileSync(contractPath, "utf8"));
  assertCondition(contract.schemaVersion === 1, `Unsupported Pages contract schema '${contract.schemaVersion}'.`);
  assertCondition(isDirectory(siteDirectory), `Generated Pages directory does not exist: ${siteDirectory}`);
  for (const required of contract.requiredPaths ?? []) assertCondition(isFile(path.join(siteDirectory, required)), `Pages output is missing '${required}'.`);
  const entries = listAllEntries(siteDirectory);
  assertCondition(!entries.some((entry) => entry.symlink), "Pages output must not contain symbolic links or reparse points.");
  const siteFiles = listFiles(siteDirectory);
  const siteBytes = siteFiles.reduce((sum, file) => sum + fs.statSync(file).size, 0);
  assertCondition(siteBytes <= Number(contract.budgets.uncompressedBytes), `Pages output exceeds its uncompressed budget: ${siteBytes} bytes.`);

  const sitemap = parseXml(fs.readFileSync(path.join(siteDirectory, "sitemap.xml"), "utf8"));
  const locations = findAll(sitemap, "loc").map((node) => node.text.trim());
  assertCondition(locations.length >= 80, `Expected at least 80 canonical sitemap URLs, found ${locations.length}.`);
  assertCondition(new Set(locations).size === locations.length, "Canonical sitemap URLs are not unique.");
  for (const location of locations) assertCondition(location.startsWith(String(contract.publicationBaseUrl)), `Unexpected sitemap URL '${location}'.`);
  for (const relative of ["index.html", "404.html", "guides/index.html", "api/CStructSharp.CStruct.html"]) {
    assertCondition(locations.includes(`${contract.publicationBaseUrl}${relative}`), `Sitemap lacks canonical URL '${contract.publicationBaseUrl}${relative}'.`);
  }

  const apiPage = fs.readFileSync(path.join(siteDirectory, "api/CStructSharp.CStruct.html"), "utf8");
  const templateScript = fs.readFileSync(path.join(documentationRoot, "templates/cstructsharp/public/main.js"), "utf8");
  assertCondition(
    /https:\/\/github\.com\/vvollers\/CStructSharp\/blob\/[0-9a-f]{40}\/src\/CStructSharp\/CStruct\.cs/.test(apiPage) ||
      templateScript.includes(`${contract.repositoryUrl}/blob/${contract.defaultBranch}/src/CStructSharp/CStruct.cs`),
    "Generated API reference lacks a source/edit link or reviewed local fallback.",
  );
  assertCondition(templateScript.includes(String(contract.publicationBaseUrl)), "Template script lacks the canonical publication root.");
  assertCondition(templateScript.includes(`${contract.repositoryUrl}/edit/${contract.defaultBranch}/docs/`), "Template script lacks the conceptual edit-link root.");

  const readme = fs.readFileSync(path.join(repositoryRoot, "README.md"), "utf8");
  const contributing = fs.readFileSync(path.join(repositoryRoot, "CONTRIBUTING.md"), "utf8");
  const pullRequestTemplatePath = path.join(repositoryRoot, ".github/pull_request_template.md");
  const coreProject = parseXml(fs.readFileSync(path.join(repositoryRoot, "src/CStructSharp/CStructSharp.csproj"), "utf8"));
  const packageReleaseNotes = findAll(coreProject, "PackageReleaseNotes")[0]?.text ?? "";
  assertCondition(readme.includes(String(contract.publicationBaseUrl)), "README does not link to the canonical documentation site.");
  assertCondition(
    packageReleaseNotes.includes(String(contract.publicationBaseUrl)) && packageReleaseNotes.includes("CHANGELOG.md") && packageReleaseNotes.includes("/issues"),
    "Package release notes do not link documentation, changelog, and issue reporting.",
  );
  assertCondition(contributing.includes("## Documentation ownership and update triggers"), "Contributor guidance lacks documentation ownership and update triggers.");
  assertCondition(isFile(pullRequestTemplatePath), "Pull-request guidance is missing.");
  assertCondition(fs.readFileSync(pullRequestTemplatePath, "utf8").includes("## Documentation impact"), "Pull-request guidance lacks documentation impact review.");

  const cacheLayer = /serviceWorker\.register|service-worker\.js|appcache/i;
  const cacheHits = siteFiles.filter((file) => (file.endsWith(".html") || file.endsWith(".js")) && cacheLayer.test(fs.readFileSync(file, "utf8")));
  assertCondition(cacheHits.length === 0, "Pages output contains an unreviewed project-owned browser cache layer.");

  let archiveBytes = 0;
  if (options["artifact-path"]) {
    const artifact = path.resolve(options["artifact-path"]);
    assertCondition(isFile(artifact), `Pages archive does not exist: ${artifact}`);
    const listing = runCommand("tar", ["-tzf", artifact], { allowFailure: true });
    assertCondition(listing.status === 0, "Could not list the Pages archive.");
    const archiveEntries = listing.stdout.split("\n").filter((line) => line.length > 0);
    assertSafeArchiveEntries(archiveEntries);
    for (const requiredPath of contract.requiredPaths ?? []) {
      const required = String(requiredPath).replaceAll("\\", "/");
      const matches = archiveEntries.filter((entry) => {
        let candidate = entry.replaceAll("\\", "/");
        while (candidate.startsWith("./")) candidate = candidate.slice(2);
        return candidate === required;
      });
      assertCondition(matches.length === 1, `Pages archive is missing '${required}'.`);
    }
    archiveBytes = fs.statSync(artifact).size;
    assertCondition(archiveBytes <= Number(contract.budgets.compressedBytes), `Pages archive exceeds its compressed budget: ${archiveBytes} bytes.`);
  }
  console.log(`Pages artifact validation passed: ${siteFiles.length} files, ${siteBytes.toLocaleString("en-US")} bytes, ${locations.length} canonical URLs${archiveBytes > 0 ? `, ${archiveBytes} compressed bytes` : ""}.`);
});
