#!/usr/bin/env node
/**
 * Proves the documentation gate passes on the prospective source alone: clones the repository into a temporary
 * checkout, copies every tracked and untracked-but-not-ignored file over it, removes files deleted in the working
 * tree, then runs the documentation validation and Pages packaging there. A failed snapshot is retained for
 * diagnosis; a successful one is removed.
 *
 *   node tools/documentation/test-documentation-source-snapshot.mjs
 */
import crypto from "node:crypto";
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { assertCondition, main, repositoryRoot, runCommand } from "../lib/tooling.mjs";
import { isFile, repositoryFiles } from "../lib/files.mjs";

const snapshotRoot = path.join(fs.realpathSync(os.tmpdir()), `CStructSharp-documentation-snapshot-${crypto.randomUUID().replaceAll("-", "")}`);

function assertSnapshotPath(candidate) {
  const relative = path.relative(snapshotRoot, path.resolve(candidate));
  assertCondition(!path.isAbsolute(relative) && !relative.startsWith(".."), `Snapshot path escapes its root: ${path.resolve(candidate)}`);
}

function runInSnapshot(args, failure) {
  const result = runCommand(process.execPath, args, { cwd: snapshotRoot, allowFailure: true });
  process.stdout.write(result.stdout ?? "");
  process.stderr.write(result.stderr ?? "");
  assertCondition(result.status === 0, failure);
}

await main(() => {
  let completed = false;
  try {
    console.log(`==> git clone --no-hardlinks ${repositoryRoot} ${snapshotRoot}`);
    assertCondition(runCommand("git", ["clone", "--no-hardlinks", "--quiet", "--", repositoryRoot, snapshotRoot], { allowFailure: true }).status === 0, "Could not create the isolated repository checkout.");
    assertCondition(runCommand("git", ["-C", snapshotRoot, "remote", "set-url", "origin", "https://github.com/vvollers/CStructSharp.git"], { allowFailure: true }).status === 0, "Could not set the publication repository URL in the isolated checkout.");
    const sourceFiles = repositoryFiles(repositoryRoot);
    for (const relative of sourceFiles) {
      const sourcePath = path.join(repositoryRoot, relative);
      const targetPath = path.join(snapshotRoot, relative);
      assertSnapshotPath(targetPath);
      if (!isFile(sourcePath)) continue;
      fs.mkdirSync(path.dirname(targetPath), { recursive: true });
      fs.copyFileSync(sourcePath, targetPath);
    }
    const deleted = runCommand("git", ["-C", repositoryRoot, "diff", "HEAD", "--no-renames", "--name-only", "--diff-filter=D"]).stdout.split("\n").filter(Boolean);
    for (const relative of deleted) {
      const targetPath = path.join(snapshotRoot, relative);
      assertSnapshotPath(targetPath);
      if (isFile(targetPath)) fs.rmSync(targetPath, { force: true });
    }
    assertCondition(!fs.existsSync(path.join(snapshotRoot, "agentdocs", "DOCUMENTATION_PLAN.md")), "The isolated source snapshot unexpectedly contains ignored local planning documentation.");
    for (const required of ["docs/docfx.json", "docs/package-lock.json", ".github/workflows/docs.yml", "tools/documentation/validate-documentation.mjs"]) {
      assertCondition(isFile(path.join(snapshotRoot, required)), `The isolated source snapshot is missing '${required}'.`);
    }
    console.log("==> prospective source snapshot documentation validation");
    runInSnapshot([path.join(snapshotRoot, "tools/documentation/validate-documentation.mjs")], "Documentation validation failed in the isolated source snapshot.");
    const apiPage = fs.readFileSync(path.join(snapshotRoot, "docs/_site/api/CStructSharp.CStruct.html"), "utf8");
    assertCondition(/https:\/\/github\.com\/vvollers\/CStructSharp\/blob\/[0-9a-f]{40}\/src\/CStructSharp\/CStruct\.cs/.test(apiPage), "The Git-visible source snapshot did not produce a commit-pinned API source link.");
    console.log("==> prospective source snapshot Pages artifact");
    runInSnapshot([path.join(snapshotRoot, "tools/documentation/new-documentation-pages-artifact.mjs")], "Pages artifact creation failed in the isolated source snapshot.");
    const artifact = path.join(snapshotRoot, "artifacts/documentation/cstructsharp-pages.tar.gz");
    const hash = crypto.createHash("sha256").update(fs.readFileSync(artifact)).digest("hex");
    console.log(`Prospective source snapshot passed: ${sourceFiles.length} source files, Pages artifact ${fs.statSync(artifact).size} bytes, SHA-256 ${hash}.`);
    completed = true;
  } finally {
    if (completed && fs.existsSync(snapshotRoot)) {
      assertSnapshotPath(snapshotRoot);
      console.log(`==> removing successful isolated snapshot ${snapshotRoot}`);
      fs.rmSync(snapshotRoot, { recursive: true, force: true });
    } else if (fs.existsSync(snapshotRoot)) {
      console.warn(`Retained failed isolated snapshot for diagnosis: ${snapshotRoot}`);
    }
  }
});
