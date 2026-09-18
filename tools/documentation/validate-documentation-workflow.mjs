#!/usr/bin/env node
/**
 * Checks .github/workflows/docs.yml against contracts/documentation/pages-v1.json: no web targets, every action
 * pinned to an immutable commit and to the reviewed pins, a read-only build job, no deployment permissions, and
 * the required triggers, validator, and artifact boundaries. `--self-test` proves the rules on a corrupted copy.
 *
 *   node tools/documentation/validate-documentation-workflow.mjs [--self-test]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { isFile } from "../lib/files.mjs";

const options = parseArguments(process.argv.slice(2), { "self-test": "flag" }, { defaults: { "self-test": false } });
const workflowPath = path.join(repositoryRoot, ".github/workflows/docs.yml");
const contractPath = path.join(repositoryRoot, "contracts/documentation/pages-v1.json");
const VALIDATOR = "node tools/documentation/validate-documentation.mjs";

export function workflowRuleCodes(text, contract) {
  const codes = [];
  if (/(?:CStructSharpWeb|apps\/explorer|src\/CStructSharp\.Wasm)/i.test(text)) codes.push("web-target");
  if (/uses:\s+[^@\s]+@v\d+/.test(text)) codes.push("moving-action-tag");
  for (const use of text.matchAll(/^\s*uses:\s+([^@\s]+)@([^\s#]+)/gm)) {
    if (!/^[0-9a-f]{40}$/.test(use[2])) codes.push("non-immutable-action");
  }
  for (const [name, pin] of Object.entries(contract.actions ?? {})) {
    if (!text.includes(`uses: ${name}@${pin}`)) codes.push(`missing-action:${name}`);
  }
  const build = /^ {2}build:\r?\n([\s\S]*)$/m.exec(text);
  const deploy = /^ {2}deploy:\r?\n([\s\S]*)$/m.exec(text);
  if (!build || !build[1].includes("contents: read") || build[1].includes("pages: write") || build[1].includes("id-token: write")) codes.push("build-permissions");
  if (deploy || text.includes("pages: write") || text.includes("id-token: write")) codes.push("deploy-boundary");
  for (const required of ["push:", "pull_request:", "workflow_dispatch:", VALIDATOR, "docs/_site/", "docs/_site", "actions/upload-artifact@"]) {
    if (!text.includes(required)) codes.push(`missing-boundary:${required}`);
  }
  return codes;
}

await main(() => {
  assertCondition(isFile(contractPath), `Pages contract does not exist: ${contractPath}`);
  const contract = JSON.parse(fs.readFileSync(contractPath, "utf8"));
  assertCondition(contract.schemaVersion === 1, `Unsupported Pages contract schema '${contract.schemaVersion}'.`);
  assertCondition(isFile(workflowPath), `Documentation workflow does not exist: ${workflowPath}`);
  const text = fs.readFileSync(workflowPath, "utf8");
  if (options["self-test"]) {
    const invalid = `${text.replace(`actions/checkout@${contract.actions["actions/checkout"]}`, "actions/checkout@v6").replace(VALIDATOR, "missing-validator.mjs")}\n# apps/explorer`;
    const codes = workflowRuleCodes(invalid, contract);
    for (const expected of ["web-target", "moving-action-tag", "non-immutable-action"]) {
      assertCondition(codes.includes(expected), `Documentation workflow fail-first fixture did not trigger '${expected}'.`);
    }
    assertCondition(codes.some((code) => code.startsWith("missing-action:")), "Documentation workflow fail-first fixture did not reject the replaced action pin.");
    assertCondition(codes.some((code) => code.startsWith("missing-boundary:")), "Documentation workflow fail-first fixture did not reject the removed deployment boundary.");
    console.log("Documentation workflow self-test passed: moving pin, Web target, and deploy-boundary defects rejected.");
    return;
  }
  const errors = workflowRuleCodes(text, contract);
  assertCondition(errors.length === 0, `Documentation workflow validation failed:\n${errors.join("\n")}`);
  console.log(`Documentation workflow validation passed: ${Object.keys(contract.actions ?? {}).length} immutable actions, read-only build, no deployment permissions.`);
});
