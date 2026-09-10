import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = fileURLToPath(new URL("../../", import.meta.url));
const destination = path.join(root, "artifacts/pages");
const sections = { docs: "docs/_site", explorer: "apps/workshop/dist", inspector: "apps/inspector/dist" };
for (const source of Object.values(sections)) {
  assert.ok(fs.statSync(path.join(root, source, "index.html")).size > 0, `Missing site: ${source}`);
}
fs.rmSync(destination, { recursive: true, force: true });
fs.mkdirSync(destination, { recursive: true });
for (const [section, source] of Object.entries(sections)) {
  fs.cpSync(path.join(root, source), path.join(destination, section), { recursive: true });
}
fs.copyFileSync(path.join(root, "docs/landing/index.html"), path.join(destination, "index.html"));
fs.writeFileSync(path.join(destination, ".nojekyll"), "");
console.log("Assembled complete website: landing page, docs, workshop, and inspector.");
