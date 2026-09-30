/**
 * Checks exact-source release eligibility without publishing: the publication gates derived from the real release
 * workflow and the workflows it calls, and the fail-closed comparison with a run's jobs.
 *
 *   node --test tools/quality/release-verification.test.mjs
 */
import assert from "node:assert/strict";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import test from "node:test";
import { expectedReleaseJobs, verifyReleaseJobs } from "../lib/release-verification.mjs";
import { jobDisplayNames, readWorkflow } from "../lib/workflow-yaml.mjs";
import { repositoryRoot } from "../lib/tooling.mjs";

const gates = expectedReleaseJobs(repositoryRoot);

// The derived list must cover each reused verification workflow and the release's own artifact job, fully expanded.
test("the real release workflow yields every shared source gate and the artifact job", () => {
  const release = readWorkflow(repositoryRoot, ".github/workflows/release.yml");
  const reused = Object.values(release.jobs).filter((job) => job.uses !== undefined);
  assert.ok(reused.length >= 3, "The release must reuse the managed, web and documentation workflows.");
  for (const job of reused) {
    const called = readWorkflow(repositoryRoot, job.uses.slice(2));
    for (const [calledId, calledJob] of Object.entries(called.jobs)) {
      for (const name of jobDisplayNames(calledId, calledJob)) assert.ok(gates.includes(`${job.name} / ${name}`), `Missing gate: ${job.name} / ${name}`);
    }
  }
  for (const expected of ["Managed verification / Build and test", "Documentation verification / Validate and package documentation", release.jobs.verify.name]) {
    assert.ok(gates.includes(expected), `Missing gate: ${expected}`);
  }
  assert.ok(gates.every((name) => !name.includes("${{")), "Every matrix expression must be expanded.");
  assert.equal(new Set(gates).size, gates.length);
});

// The npm consumer matrix must reach the package's declared minimum Node and the repository's pinned newest Node.
test("the npm consumer gates span the supported Node range on every platform", () => {
  const consumers = gates.filter((name) => /\bnpm\b.*Node \d/.test(name));
  assert.ok(consumers.length > 0, "No npm consumer gates were derived.");
  const versions = consumers.map((name) => /Node (\d+\.\d+\.\d+)/.exec(name)[1]);
  const minimum = /^>=(\d+\.\d+\.\d+)$/.exec(JSON.parse(fs.readFileSync(path.join(repositoryRoot, "packages/cstructsharp/package.json"), "utf8")).engines.node)[1];
  const newest = fs.readFileSync(path.join(repositoryRoot, ".node-version"), "utf8").trim();
  assert.ok(versions.includes(minimum), `No consumer gate runs the package's minimum Node ${minimum}.`);
  assert.ok(versions.includes(newest), `No consumer gate runs the pinned Node ${newest}.`);
  for (const platform of ["ubuntu-latest", "windows-latest", "macos-latest"]) {
    assert.ok(consumers.some((name) => name.includes(platform) && name.includes(`Node ${minimum}`)), `No minimum-Node consumer gate on ${platform}.`);
  }
});

// A synthetic release workflow proves transitive needs, reusable-workflow prefixes, matrices and unrelated jobs.
test("gates follow needs transitively and expand called workflows and matrices", (t) => {
  const root = fs.mkdtempSync(path.join(os.tmpdir(), "cstruct-release-gates-"));
  // Remove only this test's temporary workflows, including when an assertion fails.
  t.after(() => fs.rmSync(root, { recursive: true, force: true }));
  fs.mkdirSync(path.join(root, ".github/workflows"), { recursive: true });
  fs.writeFileSync(path.join(root, ".github/workflows/called.yml"), [
    "on:",
    "  workflow_call:",
    "jobs:",
    "  one:",
    "    name: First",
    "  two:",
    "    name: Second on ${{ matrix.os }}",
    "    strategy:",
    "      matrix:",
    "        os: [a, b]",
    "",
  ].join("\n"));
  fs.writeFileSync(path.join(root, ".github/workflows/release.yml"), [
    "jobs:",
    "  shared:",
    "    name: Shared",
    "    uses: ./.github/workflows/called.yml",
    "  build:",
    "    needs: shared",
    "  optional:",
    "    name: Not a gate",
    "  publish:",
    "    needs: [build]",
    "",
  ].join("\n"));
  assert.deepEqual(expectedReleaseJobs(root), ["Shared / First", "Shared / Second on a", "Shared / Second on b", "build"]);

  fs.writeFileSync(path.join(root, ".github/workflows/called.yml"), "on:\n  push:\njobs:\n  one:\n    name: First\n");
  assert.throws(() => expectedReleaseJobs(root), /must declare workflow_call/);
  fs.writeFileSync(path.join(root, ".github/workflows/release.yml"), "jobs:\n  publish:\n    needs: [missing]\n");
  assert.throws(() => expectedReleaseJobs(root), /'missing' is needed but not defined/);
});

// Every selected gate must be present once, successful, and tied to the immutable manifest commit.
test("release gates fail closed for missing, failed, skipped, duplicate or wrong-source evidence", () => {
  const sha = "a".repeat(40);
  const jobs = gates.map((name) => ({ name, conclusion: "success", head_sha: sha }));
  verifyReleaseJobs(jobs, sha, gates);
  assert.throws(() => verifyReleaseJobs(jobs, sha, []), /No release gates/);
  for (let index = 0; index < jobs.length; index++) {
    assert.throws(() => verifyReleaseJobs(jobs.filter((_, position) => position !== index), sha, gates), /exactly one/);
    assert.throws(() => verifyReleaseJobs([...jobs, jobs[index]], sha, gates), /exactly one/);
    for (const conclusion of ["failure", "skipped", "cancelled", null]) {
      const changed = jobs.map((job, position) => (position === index ? { ...job, conclusion } : job));
      assert.throws(() => verifyReleaseJobs(changed, sha, gates), /did not pass/);
    }
    const wrongSource = jobs.map((job, position) => (position === index ? { ...job, head_sha: "b".repeat(40) } : job));
    assert.throws(() => verifyReleaseJobs(wrongSource, sha, gates), /different commit/);
  }
});
