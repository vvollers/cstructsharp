#!/usr/bin/env node
/**
 * Documentation quality rules (front matter, title, description, one H1, no placeholders, unique titles and URLs,
 * link and TOC targets, reachability, tracked sources, search coverage) with a fail-first fixture set that proves
 * each rule rejects its invalid case (`--self-test`).
 *
 *   node tools/documentation/validate-documentation-quality.mjs [--self-test]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { isFile, isIgnored, toPosix } from "../lib/files.mjs";
import { documentationRoot, documentationSourceFiles, relativeLinkTargets, resolveTarget, siteDirectory, sourcePages, sourceTocs, tocHrefs } from "../lib/docs.mjs";

const options = parseArguments(process.argv.slice(2), { "self-test": "flag" }, { defaults: { "self-test": false } });
const fixturePath = path.join(repositoryRoot, "contracts/documentation/validator-fixtures.json");
const lower = (items) => items.map((item) => String(item).toLowerCase());

export function pageRuleCodes(text) {
  const codes = [];
  const frontMatter = /^---\r?\n([\s\S]*?)\r?\n---(?:\r?\n|$)/.exec(text);
  if (!frontMatter) return ["front-matter"];
  const yaml = frontMatter[1];
  if (!/^title:\s*\S.+$/m.test(yaml)) codes.push("title");
  if (!/^description:\s*\S.+$/m.test(yaml)) codes.push("description");
  if ((text.match(/^#\s+/gm) ?? []).length !== 1) codes.push("h1");
  if (/\b(?:TODO|TBD|FIXME|lorem ipsum|coming soon)\b/i.test(text)) codes.push("placeholder");
  return codes;
}

export function collectionRuleCodes(pages) {
  const codes = [];
  const titles = new Set();
  const urls = new Set();
  for (const page of pages) {
    const title = String(page.title).toLowerCase();
    const url = String(page.url).toLowerCase();
    if (titles.has(title)) codes.push("duplicate-title");
    titles.add(title);
    if (urls.has(url)) codes.push("duplicate-url");
    urls.add(url);
  }
  return codes;
}

export function linkRuleCodes(targets, existing) {
  const existingSet = new Set(lower(existing));
  return targets.some((target) => !existingSet.has(String(target).toLowerCase())) ? ["broken-link"] : [];
}

export function tocRuleCodes(targets, existing) {
  const codes = [];
  const existingSet = new Set(lower(existing));
  const destinations = new Set();
  for (const target of targets) {
    const key = String(target).toLowerCase();
    if (!existingSet.has(key)) codes.push("missing-toc-target");
    if (destinations.has(key)) codes.push("duplicate-toc-target");
    destinations.add(key);
  }
  return codes;
}

export function reachabilityRuleCodes(pages, reachable, exempt) {
  const reachableSet = new Set(lower(reachable));
  const exemptSet = new Set(lower(exempt));
  return pages.some((page) => !reachableSet.has(String(page).toLowerCase()) && !exemptSet.has(String(page).toLowerCase())) ? ["orphan"] : [];
}

export function searchRuleCodes(entries) {
  const codes = [];
  if (!entries.some((entry) => /^(?:project|guides|language|examples)\//.test(entry))) codes.push("search-conceptual");
  if (!entries.some((entry) => /^api\/CStructSharp(?:\.|\/)/.test(entry))) codes.push("search-api");
  return codes;
}

export const trackingRuleCodes = (ignored) => (ignored ? ["ignored-source"] : []);

function caseRuleCodes(fixture) {
  switch (fixture.kind) {
    case "page": return pageRuleCodes(String(fixture.text));
    case "collection": return collectionRuleCodes(fixture.pages ?? []);
    case "links": return linkRuleCodes(fixture.targets ?? [], fixture.existing ?? []);
    case "toc": return tocRuleCodes(fixture.targets ?? [], fixture.existing ?? []);
    case "reachability": return reachabilityRuleCodes(fixture.pages ?? [], fixture.reachable ?? [], fixture.exempt ?? []);
    case "search": return searchRuleCodes(fixture.entries ?? []);
    case "tracking": return trackingRuleCodes(Boolean(fixture.ignored));
    default: throw new Error(`Unknown documentation validator fixture kind '${fixture.kind}'.`);
  }
}

await main(() => {
  assertCondition(isFile(fixturePath), `Documentation validator fixture does not exist: ${fixturePath}`);
  const fixtures = JSON.parse(fs.readFileSync(fixturePath, "utf8"));
  assertCondition(fixtures.schemaVersion === 1, `Unsupported documentation validator fixture schema '${fixtures.schemaVersion}'.`);
  assertCondition((fixtures.cases ?? []).length === 14, `Expected 14 fail-first documentation validator cases, found ${(fixtures.cases ?? []).length}.`);
  if (options["self-test"]) {
    for (const fixture of fixtures.cases) {
      const codes = caseRuleCodes(fixture);
      assertCondition(codes.includes(String(fixture.expected)), `Fail-first fixture '${fixture.id}' did not trigger '${fixture.expected}'; got '${codes.join(", ")}'.`);
    }
    console.log("Documentation quality validator self-test passed: 14/14 invalid fixtures rejected.");
    return;
  }

  const pages = sourcePages();
  const pageRecords = [];
  const errors = [];
  for (const page of pages) {
    const text = fs.readFileSync(page, "utf8");
    for (const code of pageRuleCodes(text)) errors.push(`${code}: ${page}`);
    const title = /^title:\s*(.+?)\s*$/m.exec(text)?.[1]?.trim();
    const relativePath = toPosix(path.relative(documentationRoot, page));
    pageRecords.push({ path: relativePath, title: title ?? relativePath, url: relativePath.replace(/\.md$/, ".html") });
    const targets = relativeLinkTargets(text);
    const existing = targets.filter((href) => fs.existsSync(resolveTarget(page, href)));
    if (linkRuleCodes(targets, existing).length > 0) errors.push(`broken-link: ${page}`);
  }
  for (const code of collectionRuleCodes(pageRecords)) errors.push(`${code}: documentation page collection`);

  const tocs = sourceTocs();
  const reachable = new Set();
  for (const toc of tocs) {
    const targets = tocHrefs(toc);
    const existing = [];
    for (const href of targets) {
      const target = resolveTarget(toc, href);
      if (fs.existsSync(target)) {
        existing.push(href);
        if (path.extname(target) === ".md") reachable.add(target.toLowerCase());
      }
    }
    for (const code of tocRuleCodes(targets, existing)) errors.push(`${code}: ${toc}`);
  }
  for (const page of pages) {
    for (const href of relativeLinkTargets(fs.readFileSync(page, "utf8"))) {
      const target = resolveTarget(page, href);
      if (path.extname(target) === ".md" && isFile(target)) reachable.add(target.toLowerCase());
    }
  }
  const exempt = [path.join(documentationRoot, "index.md"), path.join(documentationRoot, "404.md")];
  for (const code of reachabilityRuleCodes(pages, [...reachable], exempt)) errors.push(`${code}: documentation page collection`);

  const generatedFiles = new Set(JSON.parse(fs.readFileSync(path.join(documentationRoot, "generated-files.json"), "utf8")));
  for (const sourceFile of documentationSourceFiles({ excludeReports: true })) {
    if (generatedFiles.has(toPosix(path.relative(documentationRoot, sourceFile)))) continue;
    const relative = toPosix(path.relative(repositoryRoot, sourceFile));
    for (const code of trackingRuleCodes(isIgnored(repositoryRoot, relative))) errors.push(`${code}: ${relative}`);
  }

  const searchIndexPath = path.join(siteDirectory, "index.json");
  assertCondition(isFile(searchIndexPath), `Generated search index does not exist: ${searchIndexPath}`);
  const searchEntries = Object.keys(JSON.parse(fs.readFileSync(searchIndexPath, "utf8")));
  for (const code of searchRuleCodes(searchEntries)) errors.push(`${code}: ${searchIndexPath}`);
  assertCondition(errors.length === 0, `Documentation quality validation failed:\n${errors.join("\n")}`);
  console.log(`Documentation quality passed: ${pages.length} pages, ${tocs.length} TOCs, ${searchEntries.length} search entries, 14 fail-first rules available.`);
});
