#!/usr/bin/env node
/**
 * Guards that CStructSharp.sln and CStructSharp.NonWeb.sln reference the same projects except for the deliberate
 * CStructSharpWeb.Wasm exclusion, and that both package consumers stay excluded from both. The two solution files
 * are hand-maintained; a newly added project could otherwise silently land in only one of them.
 *
 *   node tools/quality/solution-parity.mjs [--full CStructSharp.sln] [--non-web CStructSharp.NonWeb.sln]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";

const options = parseArguments(process.argv.slice(2), { full: "string", "non-web": "string" }, {
  defaults: { full: path.join(repositoryRoot, "CStructSharp.sln"), "non-web": path.join(repositoryRoot, "CStructSharp.NonWeb.sln") },
});
const webOnlyProjects = ["src\\CStructSharp.Wasm\\CStructSharpWeb.Wasm.csproj"];
const deliberatelyExcludedProjects = [
  "tests/CStructSharp.PackageConsumer\\CStructSharp.PackageConsumer.csproj",
  "tests/CStructSharp.Memory.PackageConsumer\\CStructSharp.Memory.PackageConsumer.csproj",
];

function solutionProjectPaths(solutionPath) {
  assertCondition(fs.existsSync(solutionPath), `Solution file not found: ${solutionPath}`);
  const pattern = /Project\("\{[0-9A-Fa-f-]+\}"\)\s*=\s*"[^"]+",\s*"([^"]+\.csproj)"/g;
  return [...fs.readFileSync(solutionPath, "utf8").matchAll(pattern)].map((match) => match[1].toLowerCase());
}

await main(() => {
  const fullProjects = new Set(solutionProjectPaths(options.full));
  const nonWebProjects = new Set(solutionProjectPaths(options["non-web"]));
  const expectedNonWeb = new Set(fullProjects);
  for (const webOnly of webOnlyProjects) {
    assertCondition(fullProjects.has(webOnly.toLowerCase()), `Expected web-only project '${webOnly}' was not found in ${options.full}.`);
    expectedNonWeb.delete(webOnly.toLowerCase());
  }
  const missing = [...expectedNonWeb].filter((project) => !nonWebProjects.has(project));
  assertCondition(
    missing.length === 0,
    `CStructSharp.NonWeb.sln is missing project(s) present in CStructSharp.sln: ${missing.join(", ")}. Add them to CStructSharp.NonWeb.sln, or to the web-only list if the exclusion is deliberate.`,
  );
  const unexpected = [...nonWebProjects].filter((project) => !expectedNonWeb.has(project));
  assertCondition(unexpected.length === 0, `CStructSharp.NonWeb.sln references project(s) not present in CStructSharp.sln: ${unexpected.join(", ")}.`);
  for (const excluded of deliberatelyExcludedProjects) {
    assertCondition(!fullProjects.has(excluded.toLowerCase()), `'${excluded}' is deliberately excluded from both solutions but was found in ${options.full}.`);
    assertCondition(!nonWebProjects.has(excluded.toLowerCase()), `'${excluded}' is deliberately excluded from both solutions but was found in ${options["non-web"]}.`);
  }
  console.log(
    `Solution project-list parity verified: ${fullProjects.size} project(s) in CStructSharp.sln, ${nonWebProjects.size} in CStructSharp.NonWeb.sln, deliberate exclusions confirmed.`,
  );
});
