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
if (!["pull_request", "push", "workflow_dispatch"].every((event) => Object.hasOwn(workflow.on ?? {}, event))) {
  throw new Error("Documentation workflow must retain push, pull_request, and workflow_dispatch triggers.");
}
if (!workflow.jobs?.build || workflow.jobs?.deploy) {
  throw new Error("Documentation workflow must validate without deploying the combined website.");
}

process.stdout.write("Documentation workflow YAML parsed with required triggers and jobs.\n");
