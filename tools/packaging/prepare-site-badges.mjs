import assert from "node:assert/strict";
import { execFileSync } from "node:child_process";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const repository = process.env.GITHUB_REPOSITORY || "vvollers/cstructsharp";
const output = path.join(root, "artifacts/readme-badges");
fs.mkdirSync(output, { recursive: true });
const gh = (...args) => execFileSync("gh", args, { cwd: root, encoding: "utf8" }).trim();

// CI remains the producer of quality measurements. Site builds only retrieve data, never execute it.
const runs = JSON.parse(gh("run", "list", "--repo", repository, "--workflow", "ci.yml",
  "--branch", "main", "--event", "push", "--status", "success", "--limit", "1", "--json", "databaseId,url"));
assert.equal(runs.length, 1, "No successful main CI run found");
const run = runs[0];
const temporary = fs.mkdtempSync(path.join(root, "artifacts/badge-download-"));
try {
  gh("run", "download", String(run.databaseId), "--repo", repository,
    "--name", "readme-badges", "--dir", temporary);
  for (const name of ["line-coverage", "branch-coverage", "tests"]) {
    const file = `${name}.json`;
    const badge = JSON.parse(fs.readFileSync(path.join(temporary, file), "utf8"));
    assert.ok(badge.schemaVersion === 1 && typeof badge.label === "string" && typeof badge.message === "string",
      `Invalid badge: ${name}`);
    fs.writeFileSync(path.join(output, file), JSON.stringify(badge) + "\n");
  }
} finally {
  assert.equal(path.dirname(fs.realpathSync(temporary)), fs.realpathSync(path.join(root, "artifacts")));
  fs.rmSync(temporary, { recursive: true, force: true });
}

// The release's own package is measured before publication; a website-only build uses the last release.
let size;
if (process.argv[2]) {
  size = fs.statSync(path.resolve(root, process.argv[2])).size;
} else {
  const release = JSON.parse(gh("api", `repos/${repository}/releases/latest`));
  const asset = release.assets.find(a => a.name === `CStructSharp.${release.tag_name.slice(1)}.nupkg`);
  assert.ok(asset, "Released NuGet package is missing");
  size = asset.size;
}
assert.ok(Number.isSafeInteger(size) && size > 0, "Invalid NuGet package size");
fs.writeFileSync(path.join(output, "nuget-size.json"), JSON.stringify({
  schemaVersion: 1, label: "NuGet download", message: `${(size / 1024).toFixed(1)} KiB`, color: "blue",
}) + "\n");
fs.writeFileSync(path.join(output, "index.html"), `<!doctype html>
<html lang="en"><meta charset="utf-8"><title>CStructSharp badge statistics</title>
<h1>README statistics</h1>
<p>Coverage measures the CStructSharp managed library on .NET 10. Test counts include parameterized cases
once and exclude the Vue and browser suites. Statistics refresh when the complete website is deployed.</p>
<p><a href="https://github.com/${repository}/actions/runs/${run.databaseId}">Source CI run and full reports</a></p>
<p>NuGet download: ${size} bytes.</p></html>\n`);
console.log(`Prepared Pages badges from CI run ${run.databaseId}.`);
