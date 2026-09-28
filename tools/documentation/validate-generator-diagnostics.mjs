#!/usr/bin/env node
/**
 * Keeps the generator diagnostics lesson (docs/guides/generated/diagnostics.md) in step with the analyzer's release
 * files (src/CStructSharp.Generators/AnalyzerReleases.Shipped.md and .Unshipped.md): the id/severity/title table
 * between the `generator-diagnostics` markers is generated from the rules the two files add (minus any they remove),
 * every listed id must have its own `## CSGnnn` section with the cause and the fix, and no section may describe an
 * id the analyzer does not ship.
 *
 *   node tools/documentation/validate-generator-diagnostics.mjs [--check] [--release-dir ...] [--page-path ...]
 *
 * Without --check the table is rewritten in place; with --check a stale table fails.
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { isFile } from "../lib/files.mjs";

const options = parseArguments(process.argv.slice(2), { check: "flag", "release-dir": "string", "page-path": "string" }, {
  defaults: {
    check: false,
    "release-dir": path.join(repositoryRoot, "src/CStructSharp.Generators"),
    "page-path": path.join(repositoryRoot, "docs/guides/generated/diagnostics.md"),
  },
});
const START = "<!-- generator-diagnostics:start -->";
const END = "<!-- generator-diagnostics:end -->";
const SEVERITIES = new Set(["Error", "Warning", "Info", "Hidden"]);

/**
 * The rules a release file adds and removes: `Rule ID | Category | Severity | Notes` rows under `### New Rules`
 * and `### Removed Rules`, across every release section.
 * @param {string} text The release file's text.
 * @returns {{ added: Array<{ id: string, severity: string, title: string }>, removed: string[] }} The rules in file order.
 */
export function parseReleaseRules(text) {
  const rules = [];
  const removed = [];
  let inTable = false;
  let section = "";
  for (const line of text.split(/\r?\n/)) {
    const heading = /^###\s+(.+?)\s*$/.exec(line);
    if (heading) {
      section = heading[1];
      continue;
    }
    if (/^Rule ID\s*\|/.test(line)) {
      inTable = true;
      continue;
    }
    if (!inTable || /^-+\s*\|/.test(line)) continue;
    if (!line.trim()) {
      inTable = false;
      continue;
    }
    const cells = line.split("|").map((cell) => cell.trim());
    assertCondition(cells.length === 4, `Unexpected release-file row: ${line}`);
    const [id, category, severity, title] = cells;
    assertCondition(/^CSG\d{3}$/.test(id), `Diagnostic id '${id}' is not CSGnnn.`);
    assertCondition(category === "CStructSharp", `Diagnostic ${id} has category '${category}'; expected CStructSharp.`);
    assertCondition(SEVERITIES.has(severity), `Diagnostic ${id} has an unknown severity '${severity}'.`);
    assertCondition(title.length > 0, `Diagnostic ${id} has no title.`);
    if (section === "Removed Rules") removed.push(id);
    else if (section === "New Rules") rules.push({ id, severity, title });
  }
  return { added: rules, removed };
}

/**
 * The rules the analyzer ships now: every rule the shipped and unshipped release files add, minus the removed ones.
 * @param {string} directory The generator project directory holding both release files.
 * @returns {Array<{ id: string, severity: string, title: string }>} The rules, sorted by id.
 */
export function currentRules(directory) {
  const rules = new Map();
  for (const name of ["AnalyzerReleases.Shipped.md", "AnalyzerReleases.Unshipped.md"]) {
    const file = path.join(directory, name);
    assertCondition(isFile(file), `Release file '${file}' does not exist.`);
    const { added, removed } = parseReleaseRules(fs.readFileSync(file, "utf8"));
    for (const rule of added) {
      assertCondition(!rules.has(rule.id), `The release files add diagnostic ${rule.id} twice.`);
      rules.set(rule.id, rule);
    }
    for (const id of removed) rules.delete(id);
  }
  return [...rules.values()].sort((a, b) => a.id.localeCompare(b.id));
}

/** The anchor DocFX (markdig) gives a heading: lowercase, punctuation dropped, whitespace runs joined by '-'. */
const anchorOf = (heading) => heading.trim().toLowerCase().replace(/[^a-z0-9 _-]/g, "").replace(/\s+/g, "-").replace(/^-+|-+$/g, "");

/**
 * Renders the generator diagnostics summary table, linking each rule id to its section heading.
 * @param {{id: string, severity: string, title: string}[]} rules Diagnostic rules.
 * @returns {string} Markdown table.
 */
export function renderTable(rules) {
  const lines = ["| Id | Severity | Title |", "| --- | --- | --- |"];
  for (const rule of rules) lines.push(`| [${rule.id}](#${anchorOf(`${rule.id} - ${rule.title}`)}) | ${rule.severity} | ${rule.title} |`);
  return lines.join("\n");
}

await main(() => {
  assertCondition(isFile(options["page-path"]), `Diagnostics page '${options["page-path"]}' does not exist.`);
  const rules = currentRules(options["release-dir"]);
  assertCondition(rules.length > 0, "The release files list no rules.");
  const ids = rules.map((rule) => rule.id);

  const page = fs.readFileSync(options["page-path"], "utf8");
  const start = page.indexOf(START);
  const end = page.indexOf(END);
  assertCondition(start >= 0 && end > start, `The diagnostics page must contain the ${START} … ${END} markers.`);
  const table = renderTable(rules);
  const updated = `${page.slice(0, start + START.length)}\n${table}\n${page.slice(end)}`;

  const sections = [...page.matchAll(/^## (CSG\d{3})\b/gm)].map((match) => match[1]);
  assertCondition(new Set(sections).size === sections.length, "The diagnostics page describes an id twice.");
  for (const id of ids) assertCondition(sections.includes(id), `The diagnostics page has no '## ${id}' section.`);
  for (const id of sections) assertCondition(ids.includes(id), `The diagnostics page describes '${id}', which the analyzer does not ship.`);
  for (const rule of rules) {
    const heading = `## ${rule.id} - ${rule.title}`;
    assertCondition(page.includes(heading), `The section for ${rule.id} must be headed '${heading}' (the release file's title).`);
    const body = page.slice(page.indexOf(heading) + heading.length);
    const next = body.search(/^## /m);
    const section = next >= 0 ? body.slice(0, next) : body;
    for (const part of ["**Cause.**", "**Fix.**"]) assertCondition(section.includes(part), `The section for ${rule.id} lacks a '${part}' paragraph.`);
    assertCondition(section.includes(`\`${rule.severity.toLowerCase()}\``) || section.includes(rule.severity.toLowerCase()), `The section for ${rule.id} must state its severity (${rule.severity}).`);
  }

  if (updated !== page) {
    assertCondition(!options.check, `The diagnostics table in '${path.relative(repositoryRoot, options["page-path"])}' is stale; run without --check to regenerate it.`);
    fs.writeFileSync(options["page-path"], updated);
    console.log(`Regenerated the diagnostics table (${rules.length} rules).`);
  } else {
    console.log(`Generator diagnostics page is current (${rules.length} rules, ${sections.length} sections).`);
  }
});
