import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { root, npmArtifacts, run } from "./npm-package-utils.mjs";
import { validatePackageInfo } from "./npm-release.mjs";
import { tagRelease } from "./release-state.mjs";

const manifestPath = path.join(root, "artifacts", "release-manifest.json");
const versionFiles = [
  "CStructSharp/CStructSharp.csproj",
  "CStructSharpWeb/package.json",
  "CStructSharpWeb/package-lock.json",
  "CStructSharpInspector/package.json",
  "CStructSharpInspector/package-lock.json",
  "packages/cstructsharp/package.json",
];
const hash = (bytes) => crypto.createHash("sha256").update(bytes).digest("hex");
const git = (...args) => run("git", args, { cwd: root }).trim();
const read = (file) => fs.readFileSync(path.join(root, file));
const info = JSON.parse(fs.readFileSync(path.join(npmArtifacts, "package-info.json"), "utf8"));
validatePackageInfo(info, fs.readFileSync(path.join(npmArtifacts, info.filename)));

function list(directory) {
  return fs
    .readdirSync(path.join(root, directory), { recursive: true, withFileTypes: true })
    .filter((item) => item.isFile())
    .map((item) => path.relative(root, path.join(item.parentPath, item.name)).replaceAll("\\", "/"))
    .sort();
}
if (process.argv[2] === "create") {
  const files = [
    ...list("artifacts/package"),
    ...list("artifacts/pages"),
    `artifacts/cstructsharp-wasm-v${info.version}.zip`,
    `artifacts/npm/${info.filename}`,
    "artifacts/npm/package-info.json",
  ];
  const manifest = {
    schemaVersion: 1,
    version: info.version,
    sourceSha: info.sourceSha,
    runId: process.env.GITHUB_RUN_ID,
    files: Object.fromEntries(files.map((file) => [file, hash(read(file))])),
    versionFiles: Object.fromEntries(
      versionFiles.map((file) => [file, read(file).toString("utf8")]),
    ),
  };
  fs.writeFileSync(manifestPath, `${JSON.stringify(manifest, null, 2)}\n`);
} else if (process.argv[2] === "verify") {
  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  assert.equal(manifest.schemaVersion, 1);
  assert.equal(manifest.version, info.version);
  assert.equal(manifest.sourceSha, info.sourceSha);
  assert.match(manifest.sourceSha, /^[a-f0-9]{40}$/);
  assert.equal(manifest.runId, process.env.ARTIFACT_RUN_ID);
  assert.deepEqual(Object.keys(manifest.versionFiles), versionFiles);
  for (const [file, digest] of Object.entries(manifest.files)) {
    assert.ok(file.startsWith("artifacts/") && !file.includes("..") && !file.includes("\\"));
    assert.equal(hash(read(file)), digest, `Release artifact changed: ${file}`);
  }
  for (const file of versionFiles)
    assert.equal(
      read(file).toString("utf8"),
      manifest.versionFiles[file],
      `Version-bump file changed: ${file}`,
    );
  // Only an original main-branch release run with all consumer gates passed can be resumed.
  const repository = process.env.GITHUB_REPOSITORY;
  const originRun = JSON.parse(
    run("gh", ["api", `repos/${repository}/actions/runs/${manifest.runId}`]),
  );
  assert.equal(originRun.head_sha, manifest.sourceSha);
  assert.equal(originRun.head_branch, "main");
  assert.equal(originRun.event, "workflow_dispatch");
  assert.equal(originRun.path, ".github/workflows/release.yml");
  const jobPages = JSON.parse(
    run("gh", [
      "api",
      "--paginate",
      "--slurp",
      `repos/${repository}/actions/runs/${manifest.runId}/jobs?per_page=100`,
    ]),
  );
  const jobs = jobPages.flatMap((page) => page.jobs);
  assert.ok(
    jobs.some(
      (job) =>
        job.name === "Build, test, and verify release artifacts" && job.conclusion === "success",
    ),
  );
  const nodeJobs = jobs.filter((job) => job.name.startsWith("npm on "));
  assert.equal(nodeJobs.length, 6, "All six OS/Node consumer checks must be present.");
  assert.ok(nodeJobs.every((job) => job.conclusion === "success"));
  // Record outputs only after validation, before any repository or registry write.
  fs.appendFileSync(
    process.env.GITHUB_OUTPUT,
    `version=${manifest.version}\ntag=v${manifest.version}\nsource_sha=${manifest.sourceSha}\n`,
  );
} else if (process.argv[2] === "tag") {
  const manifest = JSON.parse(fs.readFileSync(manifestPath, "utf8"));
  tagRelease(git, manifest, versionFiles);
} else {
  throw new Error("Expected create, verify, or tag");
}
