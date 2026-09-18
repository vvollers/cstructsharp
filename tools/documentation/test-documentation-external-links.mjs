#!/usr/bin/env node
/**
 * Checks every external Markdown link under docs/ (HEAD, then GET, with retries) except the reviewed exceptions in
 * contracts/documentation/external-link-allowlist.json, whose entries must be exact HTTPS URLs with an owner, a
 * reason, and an unexpired review date. `--self-test` exercises the extraction and allowlist rules on fixtures.
 *
 *   node tools/documentation/test-documentation-external-links.mjs [--retries 3] [--timeout-seconds 20] [--self-test]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { isFile, listFiles } from "../lib/files.mjs";

const options = parseArguments(process.argv.slice(2), { retries: "number", "timeout-seconds": "number", "self-test": "flag" }, { defaults: { retries: 3, "timeout-seconds": 20, "self-test": false } });
assertCondition(options.retries >= 1 && options.retries <= 5, "--retries must be between 1 and 5.");
assertCondition(options["timeout-seconds"] >= 5 && options["timeout-seconds"] <= 120, "--timeout-seconds must be between 5 and 120.");
const documentationRoot = path.join(repositoryRoot, "docs");
const allowlistPath = path.join(repositoryRoot, "contracts/documentation/external-link-allowlist.json");

export function externalUrls(text) {
  return [...new Set([...text.matchAll(/\[[^\]]+\]\((https?:\/\/[^\s)>]+)/g)].map((match) => match[1].replace(/\.+$/, "")))].sort();
}

export function allowlistErrors(exceptions) {
  const errors = [];
  const urls = new Set();
  const today = new Date().toISOString().slice(0, 10);
  for (const exception of exceptions) {
    const url = String(exception.url);
    let wellFormed;
    try {
      wellFormed = new URL(url).protocol === "https:";
    } catch {
      wellFormed = false;
    }
    if (!wellFormed || !/^https:\/\//.test(url)) errors.push(`invalid-url:${url}`);
    if (url.includes("*") || urls.has(url)) errors.push(`non-exact-or-duplicate:${url}`);
    urls.add(url);
    if (!exception.owner || String(exception.owner).trim() === "") errors.push(`missing-owner:${url}`);
    if (!exception.reason || String(exception.reason).trim() === "") errors.push(`missing-reason:${url}`);
    const reviewAfter = String(exception.reviewAfter ?? "");
    if (!/^\d{4}-\d{2}-\d{2}$/.test(reviewAfter) || Number.isNaN(Date.parse(`${reviewAfter}T00:00:00Z`))) errors.push(`invalid-review-date:${url}`);
    else if (reviewAfter < today) errors.push(`expired-review-date:${url}`);
  }
  return errors;
}

async function probe(url, method, timeoutMs) {
  const controller = new AbortController();
  const timer = setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await fetch(url, { method, redirect: "follow", signal: controller.signal, headers: { "user-agent": "CStructSharp-documentation-link-check/1.0" } });
    await response.body?.cancel();
    return { ok: response.ok, result: `HTTP ${response.status}` };
  } catch (error) {
    return { ok: false, result: error instanceof Error ? error.message : String(error) };
  } finally {
    clearTimeout(timer);
  }
}

await main(async () => {
  if (options["self-test"]) {
    const urls = externalUrls("[First](https://example.test/one)\n[Duplicate](https://example.test/one)\n[Second](https://example.test/two)\n");
    assertCondition(urls.length === 2, "External-link extraction did not deduplicate the fail-first fixture.");
    const errors = allowlistErrors([{ url: "https://example.test/*", owner: "", reason: "", reviewAfter: "2000-01-01" }]);
    for (const expected of ["non-exact-or-duplicate:", "missing-owner:", "missing-reason:", "expired-review-date:"]) {
      assertCondition(errors.filter((error) => error.startsWith(expected)).length === 1, `External-link allowlist fail-first fixture did not trigger '${expected}'.`);
    }
    console.log("External-link validator self-test passed: extraction and 4 invalid allowlist rules rejected.");
    return;
  }
  assertCondition(isFile(allowlistPath), `External-link allowlist does not exist: ${allowlistPath}`);
  const allowlist = JSON.parse(fs.readFileSync(allowlistPath, "utf8"));
  assertCondition(allowlist.schemaVersion === 1, `Unsupported external-link allowlist schema '${allowlist.schemaVersion}'.`);
  const errors = allowlistErrors(allowlist.exceptions ?? []);
  assertCondition(errors.length === 0, `External-link allowlist is invalid:\n${errors.join("\n")}`);
  const allowed = new Set((allowlist.exceptions ?? []).map((exception) => String(exception.url)));
  const urls = [
    ...new Set(
      listFiles(documentationRoot, (file) => file.endsWith(".md") && !/[/\\](?:_site|bin|obj|node_modules|test-results|playwright-report)[/\\]/.test(file)).flatMap((file) => externalUrls(fs.readFileSync(file, "utf8"))),
    ),
  ].sort();
  const failures = [];
  let checked = 0;
  let allowedCount = 0;
  const timeoutMs = options["timeout-seconds"] * 1000;
  for (const url of urls) {
    if (allowed.has(url)) {
      allowedCount++;
      console.log(`ALLOW ${url}`);
      continue;
    }
    let success = false;
    let lastResult = "no response";
    for (let attempt = 1; attempt <= options.retries && !success; attempt++) {
      const head = await probe(url, "HEAD", timeoutMs);
      if (head.ok) {
        success = true;
        break;
      }
      const get = await probe(url, "GET", timeoutMs);
      success = get.ok;
      lastResult = get.result;
    }
    if (success) {
      checked++;
      console.log(`OK    ${url}`);
    } else {
      failures.push(`${url} (${lastResult} after ${options.retries} attempts)`);
    }
  }
  assertCondition(failures.length === 0, `External documentation links failed:\n${failures.join("\n")}`);
  console.log(`External-link validation passed: ${checked} checked, ${allowedCount} reviewed exceptions, ${urls.length} total.`);
});
