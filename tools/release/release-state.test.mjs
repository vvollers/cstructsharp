/**
 * Tests `tagRelease` against temporary git repositories with a bare origin (created under artifacts/release-tests): a
 * first release and an identical retry, recovery of a missing tag, and refusal of an unrelated main commit or a
 * modified tag.
 *
 *   node --test tools/release/release-state.test.mjs    (npm run test:npm-release runs it with npm-release.test.mjs)
 */
import test from "node:test";
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { root, run } from "../packaging/npm-package-utils.mjs";
import { tagRelease } from "./release-state.mjs";

/**
 * Creates a temporary git repository with a bare `origin`, a pushed source commit, and version files changed in the
 * working tree.
 * @returns {{git: Function, manifest: object, files: string[], repo: string}} A git runner for the repository, the
 *   release manifest, the version file names, and the repository path.
 */
function fixture() {
  const parent = path.join(root, "artifacts", "release-tests");
  fs.mkdirSync(parent, { recursive: true });
  const repo = fs.mkdtempSync(path.join(parent, "repo-"));
  /** Runs a git command in the fixture repository and returns its trimmed output. */
  const git = (...args) => run("git", args, { cwd: repo }).trim();
  git("init", "-b", "main");
  git("config", "user.name", "Release Test");
  git("config", "user.email", "test@example.invalid");
  git("config", "commit.gpgsign", "false");
  const files = ["version.json", "version.csproj"];
  for (const file of files) fs.writeFileSync(path.join(repo, file), "old\n");
  git("add", ".");
  git("commit", "-m", "source");
  const sourceSha = git("rev-parse", "HEAD");
  git("init", "--bare", "origin.git");
  git("remote", "add", "origin", path.join(repo, "origin.git"));
  git("push", "origin", "main");
  const manifest = {
    version: "1.2.3",
    sourceSha,
    versionFiles: Object.fromEntries(files.map((file) => [file, "new\n"])),
  };
  for (const file of files) fs.writeFileSync(path.join(repo, file), "new\n");
  return { git, manifest, files, repo };
}
test("release commits only version files, pushes a tag, and an identical retry is a no-op", () => {
  const { git, manifest, files } = fixture();
  tagRelease(git, manifest, files);
  const sha = git("rev-parse", "v1.2.3^{commit}");
  tagRelease(git, manifest, files);
  assert.equal(git("rev-parse", "v1.2.3^{commit}"), sha);
  assert.equal(git("rev-parse", "origin/main"), sha);
});
test("recovery completes a tag after the version commit was already pushed", () => {
  const { git, manifest, files } = fixture();
  git("add", ...files);
  git("commit", "-m", "version");
  git("push", "origin", "main");
  tagRelease(git, manifest, files);
  assert.equal(git("rev-parse", "v1.2.3^{commit}"), git("rev-parse", "HEAD"));
});
test("release refuses an unrelated advance on main", () => {
  const { git, manifest, files, repo } = fixture();
  fs.writeFileSync(path.join(repo, "unverified.txt"), "unverified");
  git("add", "unverified.txt");
  git("commit", "-m", "unverified change");
  git("push", "origin", "main");
  assert.throws(() => tagRelease(git, manifest, files), /unexpected changes/);
  assert.equal(git("tag", "--list", "v1.2.3"), "");
});
test("recovery refuses a tag containing modified version data", () => {
  const { git, manifest, files, repo } = fixture();
  fs.writeFileSync(path.join(repo, files[0]), "wrong\n");
  git("add", ...files);
  git("commit", "-m", "wrong version");
  git("tag", "v1.2.3");
  assert.throws(() => tagRelease(git, manifest, files), /Release commit differs/);
});
