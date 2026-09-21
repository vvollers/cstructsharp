#!/usr/bin/env node
/** Checks or refreshes the benchmark README's derived fixture counts. Usage: node tools/documentation/sync-documentation-facts.mjs --check|--write. */
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { browserVerificationFixtures, canVerifyPublicFixture } from "../../benchmarks/js/bench/fixture-eligibility.mjs";

const options = parseArguments(process.argv.slice(2), { check: "flag", write: "flag", "readme-path": "string" }, {
  defaults: { "readme-path": "benchmarks/js/README.md" },
});
assert.ok(Boolean(options.check) !== Boolean(options.write), "Choose exactly one of --check or --write");

// Read only fixture metadata; deriving documentation must not allocate the large benchmark inputs or run timings.
await main(() => {
  const directory = path.join(repositoryRoot, "benchmarks/fixtures");
  const manifest = JSON.parse(fs.readFileSync(path.join(directory, "manifest.json"), "utf8"));
  const documents = new Map();
  for (const entry of manifest.fixtures) {
    assert.ok(!documents.has(entry.id), `Duplicate fixture ${entry.id}`);
    const document = JSON.parse(fs.readFileSync(path.join(directory, entry.file), "utf8"));
    assert.equal(document.id, entry.id);
    assert.equal(document.byteLength, entry.byteLength);
    documents.set(entry.id, document);
  }
  for (const id of browserVerificationFixtures) assert.ok(documents.has(id) && canVerifyPublicFixture(documents.get(id)), `Browser verification fixture is missing or ineligible: ${id}`);
  assert.equal(new Set(browserVerificationFixtures).size, browserVerificationFixtures.length);
  // Use the exact selection function imported by the Node harness, not a parallel implementation of its limits.
  const count = [...documents.values()].filter(canVerifyPublicFixture).length;
  const block = `<!-- benchmark-fixture-facts:start -->\nThe Node correctness gate verifies ${count} of ${documents.size} fixtures; the browser gate verifies ${browserVerificationFixtures.length} representative fixtures.\n<!-- benchmark-fixture-facts:end -->`;
  const filename = path.resolve(repositoryRoot, options["readme-path"]);
  const source = fs.readFileSync(filename, "utf8");
  const pattern = /<!-- benchmark-fixture-facts:start -->[\s\S]*?<!-- benchmark-fixture-facts:end -->/g;
  assert.equal([...source.matchAll(pattern)].length, 1, "Expected one benchmark fixture facts block");
  const expected = source.replace(pattern, block);
  if (options.write) fs.writeFileSync(filename, expected);
  else assert.equal(source, expected, "Benchmark fixture counts are stale; run sync-documentation-facts.mjs --write and review the change");
  console.log(`Documentation fixture facts ${options.write ? "updated" : "verified"}: Node ${count}/${documents.size}, browser ${browserVerificationFixtures.length}.`);
});
