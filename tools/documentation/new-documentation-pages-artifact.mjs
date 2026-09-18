#!/usr/bin/env node
/**
 * Packages the generated site as artifacts/documentation/cstructsharp-pages.tar.gz after validating the site, and
 * validates the archive.
 *
 *   node tools/documentation/new-documentation-pages-artifact.mjs [--output-path <.tar.gz>]
 */
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";
import { isDirectory } from "../lib/files.mjs";

const options = parseArguments(process.argv.slice(2), { "output-path": "string" });
const siteDirectory = path.join(repositoryRoot, "docs/_site");
const artifactDirectory = path.join(repositoryRoot, "artifacts/documentation");
const validator = path.join(repositoryRoot, "tools/documentation/validate-pages-artifact.mjs");

function validate(args) {
  const result = runCommand(process.execPath, [validator, ...args], { allowFailure: true });
  process.stdout.write(result.stdout ?? "");
  if (result.status !== 0) throw new Error((result.stderr ?? "").trim() || "Pages validation failed.");
}

await main(() => {
  const outputPath = path.resolve(options["output-path"] ?? path.join(artifactDirectory, "cstructsharp-pages.tar.gz"));
  const relativeOutput = path.relative(artifactDirectory, outputPath);
  assertCondition(!path.isAbsolute(relativeOutput) && !relativeOutput.startsWith(".."), `Pages artifact must stay below '${artifactDirectory}': ${outputPath}`);
  assertCondition(outputPath.toLowerCase().endsWith(".tar.gz"), "Pages artifact must use the .tar.gz extension.");
  assertCondition(isDirectory(siteDirectory), `Generated site does not exist: ${siteDirectory}`);
  validate(["--site-directory", siteDirectory]);
  fs.mkdirSync(artifactDirectory, { recursive: true });
  if (fs.existsSync(outputPath)) {
    console.log(`==> removing generated Pages archive ${outputPath}`);
    fs.rmSync(outputPath, { force: true });
  }
  console.log(`==> tar -czf ${outputPath} -C ${siteDirectory} .`);
  const tar = runCommand("tar", ["-czf", outputPath, "-C", siteDirectory, "."], { allowFailure: true });
  assertCondition(tar.status === 0, "Creating the Pages archive failed.");
  validate(["--site-directory", siteDirectory, "--artifact-path", outputPath]);
  const hash = crypto.createHash("sha256").update(fs.readFileSync(outputPath)).digest("hex");
  console.log(`Pages artifact created: ${outputPath}, ${fs.statSync(outputPath).size} bytes, SHA-256 ${hash}`);
});
