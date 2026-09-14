import assert from "node:assert/strict";

/** Commit/tag exactly the verified snapshot; resume only matching partial releases. */
export function tagRelease(git, manifest, versionFiles) {
  const tag = `v${manifest.version}`;
  git("fetch", "origin", "main", "--tags");
  function verifyCommit(ref) {
    assert.equal(
      git("rev-parse", `${ref}^`),
      manifest.sourceSha,
      "Release commit must have the verified source as its parent.",
    );
    const changed = git("diff", "--name-only", `${ref}^`, ref).split("\n").sort();
    assert.deepEqual(
      changed,
      [...versionFiles].sort(),
      "Release commit contains unexpected changes.",
    );
    for (const file of versionFiles)
      assert.equal(
        git("show", `${ref}:${file}`),
        manifest.versionFiles[file].trim(),
        `Release commit differs: ${file}`,
      );
  }
  const tagExists = git("tag", "--list", tag) === tag;
  if (tagExists) {
    verifyCommit(tag);
    console.log(`Verified existing tag ${tag}`);
  } else {
    const mainSha = git("rev-parse", "origin/main");
    if (mainSha !== manifest.sourceSha) {
      // Handles a previous run that pushed the version commit but failed before pushing its tag.
      verifyCommit("origin/main");
      git("tag", "-a", tag, mainSha, "-m", `Release ${manifest.version}`);
    } else {
      assert.equal(
        git("rev-parse", "HEAD"),
        manifest.sourceSha,
        "Check out the verified source before committing the version files.",
      );
      git("add", ...versionFiles);
      git("commit", "-m", `chore: release ${manifest.version}`);
      verifyCommit("HEAD");
      git("push", "origin", "HEAD:main");
      git("tag", "-a", tag, "-m", `Release ${manifest.version}`);
    }
    git("push", "origin", tag);
  }
}
