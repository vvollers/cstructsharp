/** Tests the advisory filtering of npm-audit.mjs. Usage: node --test tools/quality/npm-audit.test.mjs */
import assert from "node:assert/strict";
import test from "node:test";
import { advisories, evaluate } from "./npm-audit.mjs";

/** An audit report in which braces carries one advisory and micromatch is affected only through it. */
const report = {
  vulnerabilities: {
    braces: { via: [{ source: 1, name: "braces", url: "https://github.com/advisories/GHSA-aaaa-bbbb-cccc", severity: "high", title: "Nested patterns" }] },
    micromatch: { via: ["braces"] },
    other: { via: [{ source: 2, name: "other", url: "https://github.com/advisories/GHSA-dddd-eeee-ffff", severity: "moderate", title: "Minor" }] },
  },
};

// Packages affected only through a dependency name it as a string, so each advisory is listed once.
test("advisories lists each root advisory once", () => {
  assert.deepEqual(advisories(report).map((advisory) => advisory.id), ["GHSA-aaaa-bbbb-cccc", "GHSA-dddd-eeee-ffff"]);
});

// The exception applies only to its own directory, and moderate advisories never fail.
test("an exception accepts its advisory in its directory only", () => {
  const exceptions = [{ directory: "docs", advisory: "GHSA-aaaa-bbbb-cccc", package: "braces" }];
  const docs = evaluate(report, exceptions, "docs");
  assert.equal(docs.failing.length, 0);
  assert.equal(docs.excepted.length, 1);
  assert.equal(docs.stale.length, 0);
  assert.deepEqual(evaluate(report, exceptions, ".").failing.map((advisory) => advisory.package), ["braces"]);
});

// Once the advisory disappears (a fixed release reached the lockfile) its exception is reported as stale.
test("an exception that matches nothing is stale", () => {
  const exceptions = [{ directory: "docs", advisory: "GHSA-aaaa-bbbb-cccc", package: "braces" }];
  const result = evaluate({ vulnerabilities: {} }, exceptions, "docs");
  assert.deepEqual(result.stale, exceptions);
  assert.equal(result.failing.length, 0);
});
