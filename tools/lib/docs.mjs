/** Helpers shared by the documentation tools: the source page set, Markdown links, TOC hrefs. */
import fs from "node:fs";
import path from "node:path";
import { listFiles } from "./files.mjs";
import { repositoryRoot } from "./tooling.mjs";

export const documentationRoot = path.join(repositoryRoot, "docs");
export const siteDirectory = path.join(documentationRoot, "_site");
export const apiDirectory = path.join(documentationRoot, "api");

/** The authored content pages: every Markdown file under the content directories plus index.md and 404.md. */
export function sourcePages() {
  const pages = [];
  for (const directory of ["project", "guides", "language", "examples", "api"]) {
    pages.push(...listFiles(path.join(documentationRoot, directory), (file) => file.endsWith(".md")));
  }
  pages.push(path.join(documentationRoot, "index.md"), path.join(documentationRoot, "404.md"));
  return pages;
}

/** Authored toc.yml files (the generated API TOC excluded). */
export function sourceTocs() {
  return listFiles(documentationRoot, (file) => path.basename(file) === "toc.yml" && path.dirname(file) !== apiDirectory && !/[/\\](?:_site|node_modules)[/\\]/.test(file));
}

/** Relative Markdown link targets in a page (`[text](target)`), without fragments; external and xref links skipped. */
export function relativeLinkTargets(text) {
  const targets = [];
  for (const match of text.matchAll(/\[[^\]]+\]\(([^)]+)\)/g)) {
    let href = match[1].trim().replace(/^<|>$/g, "");
    if (/^(?:https?:|xref:|mailto:)/.test(href)) continue;
    href = href.split("#", 1)[0];
    if (href.trim() === "") continue;
    targets.push(href);
  }
  return targets;
}

/** Resolves a link or TOC href against its file's directory; a trailing slash means the directory's index.md. */
export function resolveTarget(fromFile, href) {
  const target = path.resolve(path.dirname(fromFile), href);
  return href.endsWith("/") ? path.join(target, "index.md") : target;
}

/** The `href:` values of a toc.yml (external and xref entries skipped). */
export function tocHrefs(tocFile) {
  const hrefs = [];
  for (const line of fs.readFileSync(tocFile, "utf8").split(/\r?\n/)) {
    const match = /^\s*href:\s*(.+?)\s*$/.exec(line);
    if (!match) continue;
    const href = match[1].trim().replace(/^["']|["']$/g, "");
    if (/^(?:https?:|xref:)/.test(href)) continue;
    hrefs.push(href);
  }
  return hrefs;
}

/** Documentation source files that must be tracked: everything under docs/ except build output and generated API metadata. */
export function documentationSourceFiles({ excludeReports = false } = {}) {
  const excluded = excludeReports ? /[/\\](?:_site|\.tmp|bin|obj|browser-report|test-results|node_modules)[/\\]/ : /[/\\](?:_site|\.tmp|bin|obj|browser-report|node_modules|playwright-report|test-results)[/\\]/;
  return listFiles(documentationRoot, (file) => !excluded.test(file) && !(path.dirname(file) === apiDirectory && path.basename(file) !== "index.md") && ![".log", ".binlog"].includes(path.extname(file)));
}
