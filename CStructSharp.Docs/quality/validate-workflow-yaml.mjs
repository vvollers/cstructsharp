import fs from "node:fs";
import path from "node:path";
import process from "node:process";
import { fileURLToPath } from "node:url";
import { load } from "js-yaml";

const documentationRoot = path.dirname(path.dirname(fileURLToPath(import.meta.url)));
const repositoryRoot = path.dirname(documentationRoot);
const workflowPath = path.join(repositoryRoot, ".github", "workflows", "docs.yml");
const workflow = load(fs.readFileSync(workflowPath, "utf8"));

if (!workflow || typeof workflow !== "object") {
  throw new Error("Documentation workflow must parse as a YAML mapping.");
}
if (!workflow.on?.pull_request || !workflow.on?.push || !workflow.on?.workflow_dispatch) {
  throw new Error("Documentation workflow must retain push, pull_request, and workflow_dispatch triggers.");
}
if (!workflow.jobs?.build || !workflow.jobs?.deploy) {
  throw new Error("Documentation workflow must retain separate build and deploy jobs.");
}

process.stdout.write("Documentation workflow YAML parsed with required triggers and jobs.\n");
