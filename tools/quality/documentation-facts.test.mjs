/** Checks benchmark selection boundaries and derived prose drift. Usage: node --test tools/quality/documentation-facts.test.mjs. */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { spawnSync } from "node:child_process";
import test from "node:test";
import { canVerifyPublicFixture, browserVerificationFixtures } from "../../benchmarks/js/bench/fixture-eligibility.mjs";
import { repositoryRoot } from "../lib/tooling.mjs";

// Moving the selection predicate into a shared module must preserve the benchmark's existing inclusion rules.
test("fixture eligibility preserves expected-value and exact byte/element boundaries", () => {
  const document = { expected: {}, byteLength: 4 * 1024 * 1024, readOptions: { maxArrayElements: 1_000_000 } };
  assert.ok(canVerifyPublicFixture(document));
  assert.ok(!canVerifyPublicFixture({ ...document, byteLength: document.byteLength + 1 }));
  assert.ok(!canVerifyPublicFixture({ ...document, readOptions: { maxArrayElements: 1_000_001 } }));
  assert.ok(!canVerifyPublicFixture({ ...document, expected: null }));
  assert.ok(canVerifyPublicFixture({ ...document, expected: null, expectedSha256: "fixture digest" }));
  assert.ok(canVerifyPublicFixture({ ...document, expected: false }));
  assert.ok(canVerifyPublicFixture({ ...document, expected: 0 }));
  const root = path.join(repositoryRoot, "benchmarks/fixtures");
  const fixtures = JSON.parse(fs.readFileSync(path.join(root, "manifest.json"))).fixtures;
  for (const entry of fixtures) {
    const fixture = JSON.parse(fs.readFileSync(path.join(root, entry.file)));
    const previouslySelected = !((fixture.expected === null || fixture.expected === undefined) && !fixture.expectedSha256) &&
      !(fixture.byteLength > 4 * 1024 * 1024 || (fixture.readOptions?.maxArrayElements ?? 0) > 1_000_000);
    assert.equal(canVerifyPublicFixture(fixture), previouslySelected, entry.id);
  }
  assert.deepEqual(browserVerificationFixtures, ["prim-le-record", "nested-x256", "array-u8-1024", "union-x1k", "strings-1024", "pointer-depth-8", "cond-if128", "real-png", "real-pe-exe"]);
});

// Exercise the real documentation checker on both current prose and a stale temporary copy.
test("the derived-count check fails when prose stops matching the harness", (t) => {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-documentation-facts-"));
  // Clean only this test's temporary copy; the repository README is never edited by the test.
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  const filename = path.join(directory, "README.md");
  const original = fs.readFileSync(path.join(repositoryRoot, "benchmarks/js/README.md"), "utf8");
  fs.writeFileSync(filename, original);
  const args = ["tools/documentation/sync-documentation-facts.mjs", "--check", "--readme-path", filename];
  const passing = spawnSync(process.execPath, args, { cwd: repositoryRoot, encoding: "utf8" });
  assert.equal(passing.status, 0, passing.stderr);
  fs.writeFileSync(filename, original.replace(/Node correctness gate verifies \d+ of \d+/, "Node correctness gate verifies 0 of 0"));
  const failing = spawnSync(process.execPath, args, { cwd: repositoryRoot, encoding: "utf8" });
  assert.notEqual(failing.status, 0);
  assert.match(failing.stderr + failing.stdout, /fixture counts are stale/);
});
