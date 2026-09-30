#!/usr/bin/env node
/**
 * Checks the repository's GitHub Actions workflows.
 *
 * `.github/workflows/docs.yml` is checked against contracts/documentation/pages-v1.json: no web targets, every
 * action pinned to an immutable commit SHA (Dependabot moves the pins; the contract names only the actions that must
 * be used), a read-only `build` job, no `deploy` job or deployment permissions, the push/pull_request/
 * workflow_dispatch triggers, and the validator and artifact boundaries. Every workflow is also parsed
 * (tools/lib/workflow-yaml.mjs), and each `.csproj` it names and each literal `working-directory` must exist, so a
 * repository move cannot break a workflow that ordinary pushes never run (release, mutation). `--self-test` proves the
 * rules on corrupted copies.
 *
 *   node tools/documentation/validate-documentation-workflow.mjs [--self-test]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { isFile } from "../lib/files.mjs";
import { parseWorkflow } from "../lib/workflow-yaml.mjs";

const options = parseArguments(process.argv.slice(2), { "self-test": "flag" }, { defaults: { "self-test": false } });
const workflowDirectory = path.join(repositoryRoot, ".github/workflows");
const workflowPath = path.join(workflowDirectory, "docs.yml");
const contractPath = path.join(repositoryRoot, "contracts/documentation/pages-v1.json");
const VALIDATOR = "node tools/documentation/validate-documentation.mjs";

/**
 * Checks the documentation workflow's text against the Pages contract.
 * @param {string} text The workflow YAML.
 * @param {object} contract The parsed contracts/documentation/pages-v1.json.
 * @returns {string[]} One code per broken rule, such as `web-target` or `missing-action:<name>`; empty when the
 *   workflow complies.
 */
export function workflowRuleCodes(text, contract) {
  const codes = [];
  if (/(?:CStructSharpWeb|apps\/explorer|src\/CStructSharp\.Wasm)/i.test(text)) codes.push("web-target");
  if (/uses:\s+[^@\s]+@v\d+/.test(text)) codes.push("moving-action-tag");
  for (const use of text.matchAll(/^\s*uses:\s+([^@\s]+)@([^\s#]+)/gm)) {
    if (!/^[0-9a-f]{40}$/.test(use[2])) codes.push("non-immutable-action");
  }
  for (const name of contract.requiredActions ?? []) {
    if (!new RegExp(`uses:\\s+${name.replace(/[.*+?^${}()|[\]\\/]/g, "\\$&")}@`).test(text)) codes.push(`missing-action:${name}`);
  }

  // The structural rules read the parsed workflow, so a trigger or job named only in a comment does not count.
  let workflow;
  try {
    workflow = parseWorkflow(text);
  } catch {
    return [...codes, "unparsable-workflow"];
  }
  const build = workflow.jobs?.build;
  const buildPermissions = build?.permissions ?? {};
  if (!build || buildPermissions.contents !== "read" || Object.keys(buildPermissions).some((scope) => scope !== "contents")) codes.push("build-permissions");
  if (workflow.jobs?.deploy || text.includes("pages: write") || text.includes("id-token: write")) codes.push("deploy-boundary");
  for (const trigger of ["push", "pull_request", "workflow_dispatch"]) {
    if (!workflow.on || !Object.hasOwn(workflow.on, trigger)) codes.push(`missing-trigger:${trigger}`);
  }
  for (const required of [VALIDATOR, "docs/_site/", "actions/upload-artifact@"]) {
    if (!text.includes(required)) codes.push(`missing-boundary:${required}`);
  }
  return codes;
}

/**
 * Finds the project files and working directories a workflow names that do not exist in the repository.
 * @param {string} name The workflow file name, used in the messages.
 * @param {string} text The workflow YAML.
 * @param {string} root The repository root the paths are relative to.
 * @returns {string[]} One message per missing path, or per workflow that cannot be parsed; empty when all exist.
 */
export function workflowPathErrors(name, text, root) {
  const errors = [];
  for (const match of text.matchAll(/(?:\.\/)?(?:src|tests|benchmarks|docs)\/[A-Za-z0-9_./\\-]+\.csproj/g)) {
    if (!fs.existsSync(path.join(root, match[0]))) errors.push(`${name}: missing project ${match[0]}`);
  }
  let workflow;
  try {
    workflow = parseWorkflow(text);
  } catch (error) {
    return [...errors, `${name}: ${error.message}`];
  }
  for (const job of Object.values(workflow.jobs ?? {})) {
    for (const step of job?.steps ?? []) {
      const directory = step?.["working-directory"] ?? job.defaults?.run?.["working-directory"] ?? ".";
      // An expression is resolved at run time, so only literal directories can be checked here.
      if (!directory.includes("${{") && !fs.existsSync(path.join(root, directory))) errors.push(`${name}: missing working directory ${directory}`);
    }
  }
  return errors;
}

await main(() => {
  assertCondition(isFile(contractPath), `Pages contract does not exist: ${contractPath}`);
  const contract = JSON.parse(fs.readFileSync(contractPath, "utf8"));
  assertCondition(contract.schemaVersion === 1, `Unsupported Pages contract schema '${contract.schemaVersion}'.`);
  assertCondition(isFile(workflowPath), `Documentation workflow does not exist: ${workflowPath}`);
  const text = fs.readFileSync(workflowPath, "utf8");
  if (options["self-test"]) {
    const invalid = `${text.replace(/actions\/checkout@[0-9a-f]{40}/, "actions/checkout@v6").replace(/uses:\s+actions\/setup-node@[^\s#]+/g, "run: echo no node").replace(VALIDATOR, "missing-validator.mjs").replace(/^ {2}workflow_dispatch:/m, "  # workflow_dispatch:")}\n# apps/explorer`;
    const codes = workflowRuleCodes(invalid, contract);
    for (const expected of ["web-target", "moving-action-tag", "non-immutable-action", "missing-trigger:workflow_dispatch"]) {
      assertCondition(codes.includes(expected), `Documentation workflow fail-first fixture did not trigger '${expected}'.`);
    }
    assertCondition(codes.includes("missing-action:actions/setup-node"), "Documentation workflow fail-first fixture did not reject the removed required action.");
    assertCondition(codes.some((code) => code.startsWith("missing-boundary:")), "Documentation workflow fail-first fixture did not reject the removed validator boundary.");
    const deploying = text.replace(/^jobs:\r?\n/m, "jobs:\n  deploy:\n    permissions:\n      pages: write\n");
    assertCondition(workflowRuleCodes(deploying, contract).includes("deploy-boundary"), "Documentation workflow fail-first fixture did not reject a deploy job.");
    const moved = "jobs:\n  a:\n    steps:\n      - run: dotnet build tests/Missing/Missing.csproj\n        working-directory: missing-directory\n";
    const pathErrors = workflowPathErrors("moved.yml", moved, repositoryRoot);
    assertCondition(pathErrors.length === 2, `Workflow path fail-first fixture reported ${pathErrors.length} errors instead of 2.`);
    console.log("Documentation workflow self-test passed: moving pin, Web target, trigger, deploy-boundary and missing-path defects rejected.");
    return;
  }

  const errors = workflowRuleCodes(text, contract);
  assertCondition(errors.length === 0, `Documentation workflow validation failed:\n${errors.join("\n")}`);
  const workflows = fs.readdirSync(workflowDirectory).filter((name) => /\.ya?ml$/.test(name));
  const pathErrors = workflows.flatMap((name) => workflowPathErrors(name, fs.readFileSync(path.join(workflowDirectory, name), "utf8"), repositoryRoot));
  assertCondition(pathErrors.length === 0, `Workflow validation failed:\n${pathErrors.join("\n")}`);
  console.log(`Documentation workflow validation passed: ${(contract.requiredActions ?? []).length} required actions, every action SHA-pinned, read-only build, no deployment permissions; ${workflows.length} workflows parsed with every project path and working directory present.`);
});
