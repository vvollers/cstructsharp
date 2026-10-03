#!/usr/bin/env node
/**
 * Runs `npm audit` for one lockfile directory and fails on any high or critical advisory, except those recorded for
 * that directory in contracts/quality/npm-audit-exceptions.json. An exception that no longer matches a reported
 * advisory also fails, so the list cannot outlive the fix it waits for.
 *
 *   node tools/quality/npm-audit.mjs [--directory <path relative to the repository root>]   (default: .)
 *
 * The directory's dependencies need not be installed: npm audit reads the lockfile.
 */
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { main, parseArguments, repositoryRoot, runNpm } from "../lib/tooling.mjs";

/** Severities that fail the audit, matching `npm audit --audit-level=high`. */
const FAILING = new Set(["high", "critical"]);

/**
 * Lists the distinct advisories in an `npm audit --json` report. Each vulnerable package names its own advisories as
 * objects in `via` (packages that are only affected through a dependency name it as a string), so collecting the
 * objects yields every root advisory once.
 * @param {object} report The parsed `npm audit --json` output.
 * @returns {{id: string, package: string, severity: string, title: string}[]} The advisories, identified by their
 *   GitHub advisory ID when the URL carries one, else by npm's numeric source.
 */
export function advisories(report) {
  const found = new Map();
  for (const vulnerability of Object.values(report.vulnerabilities ?? {})) {
    for (const via of vulnerability.via ?? []) {
      if (typeof via !== "object") continue;
      const id = /GHSA-[\w-]+/.exec(via.url ?? "")?.[0] ?? String(via.source);
      found.set(`${id}|${via.name}`, { id, package: via.name, severity: via.severity, title: via.title });
    }
  }
  return [...found.values()];
}

/**
 * Compares a directory's advisories with its recorded exceptions.
 * @param {object} report The parsed `npm audit --json` output.
 * @param {{directory: string, advisory: string, package: string}[]} exceptions Every recorded exception.
 * @param {string} directory The audited directory, relative to the repository root in POSIX form ("." for the root).
 * @returns {{failing: object[], excepted: object[], stale: object[]}} High or critical advisories without an
 *   exception, those with one, and this directory's exceptions that matched nothing.
 */
export function evaluate(report, exceptions, directory) {
  const own = exceptions.filter((exception) => exception.directory === directory);
  /**
   * Whether an exception names this advisory and package.
   * @param {{id: string, package: string}} advisory A reported advisory.
   * @param {{advisory: string, package: string}} exception A recorded exception.
   * @returns {boolean} True when both the advisory ID and the package match.
   */
  const matches =(advisory, exception) => exception.advisory === advisory.id && exception.package === advisory.package;
  const severe = advisories(report).filter((advisory) => FAILING.has(advisory.severity));
  return {
    failing: severe.filter((advisory) => !own.some((exception) => matches(advisory, exception))),
    excepted: severe.filter((advisory) => own.some((exception) => matches(advisory, exception))),
    stale: own.filter((exception) => !severe.some((advisory) => matches(advisory, exception))),
  };
}

/** Audits the requested directory and throws with every unexcepted advisory or stale exception. */
async function audit() {
  const options = parseArguments(process.argv.slice(2), { directory: "string" }, { defaults: { directory: "." } });
  const directory = path.posix.normalize(options.directory.replaceAll("\\", "/"));
  const contract = JSON.parse(fs.readFileSync(path.join(repositoryRoot, "contracts/quality/npm-audit-exceptions.json"), "utf8"));

  // npm audit exits nonzero whenever it reports anything, so its status says nothing; a missing report does.
  const result = runNpm(["audit", "--json"], { cwd: path.join(repositoryRoot, directory), allowFailure: true });
  if (result.error) throw result.error;
  let report;
  try {
    report = JSON.parse(result.stdout);
  } catch {
    throw new Error(`npm audit in ${directory} returned no JSON report:\n${result.stdout ?? ""}\n${result.stderr ?? ""}`);
  }
  if (report.error) throw new Error(`npm audit in ${directory} failed: ${report.error.summary ?? JSON.stringify(report.error)}`);

  const { failing, excepted, stale } = evaluate(report, contract.exceptions, directory);
  for (const advisory of excepted) console.log(`Accepted (recorded exception): ${advisory.id} ${advisory.package} - ${advisory.title}`);
  const problems = [
    ...failing.map((advisory) => `${advisory.severity} advisory ${advisory.id} in ${advisory.package}: ${advisory.title}`),
    ...stale.map((exception) => `Exception ${exception.advisory} (${exception.package}) no longer matches an advisory in ${directory}; remove it from contracts/quality/npm-audit-exceptions.json.`),
  ];
  if (problems.length) throw new Error(`npm audit failed in ${directory}:\n${problems.join("\n")}`);
  console.log(`npm audit passed in ${directory} (${excepted.length} recorded exception(s)).`);
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main(audit);
