/** Validates merged coverage evidence and qualifies exact, reviewed compile-time declaration files. */
import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { findAll, parseXml } from "./xml.mjs";

/** Returns a file's byte-exact SHA-256 digest for evidence identity checks. */
export function coverageFileHash(filename) {
  return crypto.createHash("sha256").update(fs.readFileSync(filename)).digest("hex");
}

/** Hashes reviewed declaration text with LF endings so checkout newline conversion does not change its identity. */
export function declarationSourceHash(filename) {
  return crypto.createHash("sha256").update(fs.readFileSync(filename, "utf8").replaceAll("\r\n", "\n")).digest("hex");
}

/** Requires a non-empty, completely passing TRX report and returns its per-case results. */
export function passingTestResults(filename) {
  const results = findAll(parseXml(fs.readFileSync(filename, "utf8")), "UnitTestResult");
  assert.ok(results.length > 0, `No executed tests in ${filename}`);
  // These three suites have no deliberately skipped cases; incomplete evidence must not qualify metadata.
  assert.ok(results.every((result) => result.attributes.outcome === "Passed"), `Incomplete or failed tests in ${filename}`);
  return results;
}

/**
 * Returns coverage filenames qualified as compile-time declarations, with reasons and test names.
 * Requires all three hashed suite reports, the exact merged report, passing generator cases and unchanged
 * declaration sources. It never changes measured hits or removes files from aggregate coverage.
 */
export function qualifyCompileTimeDeclarations(root, policyPath, collectionPath, coveragePath, filenames) {
  assert.ok(collectionPath, "A coverage collection manifest is required for compile-time qualification");
  const collection = JSON.parse(fs.readFileSync(collectionPath, "utf8"));
  const directory = path.dirname(collectionPath);
  assert.equal(collection.schemaVersion, 1);
  assert.equal(collection.coverageSha256, coverageFileHash(coveragePath), "Merged coverage differs from the collected report");
  assert.deepEqual(collection.suites.map((suite) => suite.name), ["runtime", "parity", "generator"], "All three suites must contribute coverage");
  let generatorResults;
  for (const suite of collection.suites) {
    assert.equal(coverageFileHash(path.resolve(directory, suite.report)), suite.sha256, `${suite.name} coverage changed`);
    const tests = path.resolve(directory, suite.tests);
    assert.equal(coverageFileHash(tests), suite.testsSha256, `${suite.name} test report changed`);
    const results = passingTestResults(tests);
    if (suite.name === "generator") generatorResults = results;
  }

  const policy = JSON.parse(fs.readFileSync(policyPath, "utf8"));
  assert.equal(policy.schemaVersion, 1);
  const qualified = new Map();
  for (const entry of policy.compileTimeDeclarations) {
    assert.ok(entry.source.startsWith("src/") && !entry.source.includes("..") && !entry.source.includes("*"), "Population entries must be exact source paths");
    assert.equal(declarationSourceHash(path.resolve(root, entry.source)), entry.sha256, `${entry.source} changed: review its compile-time-only classification before updating the policy`);
    assert.ok(entry.reason?.trim() && entry.requiredTests?.length > 0, "A declaration requires its rationale and generator tests");
    const suffix = entry.source.slice(4);
    // Cobertura can use source-root-relative or absolute paths; ambiguity is an error, not an exclusion.
    const matches = filenames.filter((filename) => filename === suffix || filename.endsWith(`/${suffix}`));
    assert.equal(matches.length, 1, `Expected one measured file for ${entry.source}`);
    assert.ok(!qualified.has(matches[0]), `Duplicate population entry ${entry.source}`);
    for (const required of entry.requiredTests) {
      // Data-driven cases append their arguments to the method name in TRX.
      assert.ok(generatorResults.some((result) => result.attributes.testName === required || result.attributes.testName.startsWith(`${required} (`)), `Missing passing generator evidence: ${required}`);
    }
    qualified.set(matches[0], { reason: entry.reason, requiredTests: entry.requiredTests });
  }
  return qualified;
}
