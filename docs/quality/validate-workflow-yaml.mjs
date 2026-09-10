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

// Repository moves must update every workflow, including manually invoked release
// and mutation jobs that are not exercised by an ordinary push.
for (const name of fs.readdirSync(path.join(repositoryRoot, ".github/workflows"))) {
  if (!name.endsWith(".yml")) continue;
  const text = fs.readFileSync(path.join(repositoryRoot, ".github/workflows", name), "utf8");
  const definition = load(text);
  for (const match of text.matchAll(/(?:\.\/)?(?:src|tests|benchmarks|docs)\/[A-Za-z0-9_./\\-]+\.csproj/g)) {
    if (!fs.existsSync(path.join(repositoryRoot, match[0]))) {
      throw new Error(`${name}: missing project ${match[0]}`);
    }
  }
  for (const job of Object.values(definition.jobs ?? {})) {
    for (const step of job.steps ?? []) {
      const directory = step["working-directory"] ?? job.defaults?.run?.["working-directory"] ?? ".";
      if (!directory.includes("${{") && !fs.existsSync(path.join(repositoryRoot, directory))) {
        throw new Error(`${name}: missing working directory ${directory}`);
      }
    }
  }
}
process.stdout.write("All workflow project paths and working directories exist.\n");
