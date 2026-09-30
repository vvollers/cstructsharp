/** Checks benchmark selection boundaries and documentation-fact drift detection. Usage: node --test tools/quality/documentation-facts.test.mjs. */
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

/** The pages that hold facts, copied into a temporary directory so the tests never edit the repository. */
const FACT_PAGES = ["benchmarks/js/README.md", "docs/guides/performance.md", "docs/guides/generated/mapped-classes.md", "packages/cstructsharp/README.md", "docs/examples/index.md"];

/**
 * Copies every fact page into a new temporary directory (same relative paths) that is removed after the test.
 * @param {import("node:test").TestContext} t The test, which owns the directory's cleanup.
 * @returns {string} The directory.
 */
function copyFactPages(t) {
  const directory = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-documentation-facts-"));
  // Remove only this test's temporary copy; the repository pages are never edited by the tests.
  t.after(() => fs.rmSync(directory, { recursive: true, force: true }));
  for (const page of FACT_PAGES) {
    fs.mkdirSync(path.dirname(path.join(directory, page)), { recursive: true });
    fs.copyFileSync(path.join(repositoryRoot, page), path.join(directory, page));
  }
  return directory;
}

/** Runs the facts tool in one mode against the pages below `directory`. */
const runFacts = (mode, directory) =>
  spawnSync(process.execPath, ["tools/documentation/sync-documentation-facts.mjs", mode, "--documents", directory], { cwd: repositoryRoot, encoding: "utf8" });

/** Rewrites one page of the temporary copy with `change` applied to its text. */
function editPage(directory, page, change) {
  const file = path.join(directory, page);
  const before = fs.readFileSync(file, "utf8");
  const after = change(before);
  assert.notEqual(after, before, `The test edit did not change ${page}.`);
  fs.writeFileSync(file, after);
}

// The committed pages must already match their sources; this is the same check the documentation validator runs.
test("the committed documentation facts match their sources", (t) => {
  const passing = runFacts("--check", copyFactPages(t));
  assert.equal(passing.status, 0, passing.stderr);
  assert.match(passing.stdout, /Documentation facts verified/);
});

// Each kind of source is covered: a count from metadata, a measured table, a quoted comparison number, and a count
// derived from code. The failure must name the page and the fact so the reader knows what to regenerate.
test("drift fails with the page and the fact named, and --write repairs it", (t) => {
  const directory = copyFactPages(t);
  editPage(directory, "benchmarks/js/README.md", (text) => text.replace(/Node correctness gate verifies \d+ of \d+/, "Node correctness gate verifies 0 of 0"));
  editPage(directory, "docs/guides/performance.md", (text) => text.replace(/(<!-- typical-costs:start -->[\s\S]*?\| )[\d.]+ (ns|µs|ms) \|/, (_, before, unit) => `${before}999 ${unit} |`));
  editPage(directory, "docs/guides/performance.md", (text) => text.replace(/(<!-- facts:accessor-costs:start -->[^<]*?took )[\d.,]+ (ns|µs)/, (_, before, unit) => `${before}260 ${unit}`));
  editPage(directory, "docs/examples/index.md", (text) => text.replace(/(<!-- facts:recipe-count:start -->)\d+/, (_, marker) => `${marker}28`));

  const failing = runFacts("--check", directory);
  assert.notEqual(failing.status, 0);
  const output = failing.stderr + failing.stdout;
  assert.ok(output.includes('benchmarks/js/README.md: fact "benchmark-fixture" is stale: found "The Node correctness gate verifies 0 of 0'), output);
  assert.match(output, /docs\/guides\/performance\.md: fact "typical-costs" is stale: found "\| .* \| 999 /);
  assert.match(output, /docs\/guides\/performance\.md: fact "accessor-costs" is stale: found ".*took 260 /);
  assert.match(output, /docs\/examples\/index\.md: fact "recipe-count" is stale: found "28", expected "\d+"/);
  assert.doesNotMatch(output, /mapped-direct-costs|npm-package-size|scenario-count/);
  assert.ok(output.includes("sync-documentation-facts.mjs --write"), output);

  const repaired = runFacts("--write", directory);
  assert.equal(repaired.status, 0, repaired.stderr);
  for (const page of FACT_PAGES) assert.equal(fs.readFileSync(path.join(directory, page), "utf8"), fs.readFileSync(path.join(repositoryRoot, page), "utf8"), page);
});

// A page whose markers were deleted cannot be checked, which must fail rather than pass silently.
test("a missing marker fails and names the fact", (t) => {
  const directory = copyFactPages(t);
  editPage(directory, "packages/cstructsharp/README.md", (text) => text.replace("<!-- facts:npm-package-size:end -->", ""));
  const failing = runFacts("--check", directory);
  assert.notEqual(failing.status, 0);
  assert.ok(failing.stderr.includes('packages/cstructsharp/README.md: fact "npm-package-size" needs'), failing.stderr);
});
