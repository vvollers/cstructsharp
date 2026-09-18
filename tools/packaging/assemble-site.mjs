import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const destination = path.join(root, "artifacts/pages");
const sections = { docs: "docs/_site", explorer: "apps/explorer/dist", inspector: "apps/inspector/dist" };
for (const source of Object.values(sections)) {
  assert.ok(fs.statSync(path.join(root, source, "index.html")).size > 0, `Missing site: ${source}`);
}
const badges = path.join(root, "artifacts/readme-badges");
const badgeFiles = ["line-coverage.json", "branch-coverage.json", "tests.json", "nuget-size.json", "index.html"];
for (const name of badgeFiles) {
  assert.ok(fs.statSync(path.join(badges, name)).size > 0, `Missing badge: ${name}`);
}
fs.rmSync(destination, { recursive: true, force: true });
fs.mkdirSync(destination, { recursive: true });
for (const [section, source] of Object.entries(sections)) {
  fs.cpSync(path.join(root, source), path.join(destination, section), { recursive: true });
}
fs.copyFileSync(path.join(root, "docs/landing/index.html"), path.join(destination, "index.html"));
fs.mkdirSync(path.join(destination, "badges"));
for (const name of badgeFiles) {
  fs.copyFileSync(path.join(badges, name), path.join(destination, "badges", name));
}
fs.writeFileSync(path.join(destination, ".nojekyll"), "");
console.log("Assembled complete website: landing page, docs, explorer, inspector, and badges.");
