#!/usr/bin/env node
/**
 * Records or checks the golden manifests of the engine differential tests (tests/CStructSharpTests/Engine/Golden/):
 * the canonical outcomes every differential comparison checks the compiled engine against. Regenerate them only for an
 * intended, explained behaviour change, never to hide a difference (CONTRIBUTING.md).
 *
 * `record` builds the managed test project, runs its tests on net10.0 with CSTRUCTSHARP_ENGINE_GOLDEN_RECORD=1 (every
 * differential case also runs the interpreter, which must agree with the engine, and its outcome is recorded), checks
 * the new manifests on net8.0 and net10.0, and lists the added, removed and changed test sections per manifest.
 * `check` runs the two checks alone. `--filter` passes a dotnet test filter to every run, so a recording rewrites only
 * the sections of the tests it ran; `--no-build` skips the build.
 *
 *   node tools/quality/engine-golden.mjs record [--filter <expression>] [--no-build]
 *   node tools/quality/engine-golden.mjs check [--filter <expression>] [--no-build]
 */
import path from "node:path";
import { compareGoldenManifests, formatGoldenChanges, readGoldenManifests } from "../lib/golden-manifests.mjs";
import { assertCondition, main, parseArguments, repositoryRoot, runDotnet } from "../lib/tooling.mjs";

const project = "tests/CStructSharpTests/CStructSharpTests.csproj";
const goldenDirectory = path.join(repositoryRoot, "tests/CStructSharpTests/Engine/Golden");
const recordVariable = "CSTRUCTSHARP_ENGINE_GOLDEN_RECORD";
const usage = "Usage: node tools/quality/engine-golden.mjs record|check [--filter <expression>] [--no-build]";
const options = parseArguments(process.argv.slice(2), { filter: "string", "no-build": "flag" }, { positionals: true });

/**
 * Runs the managed tests on one target framework, recording the manifests or checking them.
 * @param {string} framework The target framework, such as net10.0.
 * @param {boolean} record Whether the run records the manifests; a check clears the record switch.
 */
function runTests(framework, record) {
  const filter = options.filter ? ["--filter", options.filter] : [];
  const env = { ...process.env, [recordVariable]: record ? "1" : "0" };
  runDotnet(["test", project, "-c", "Release", "--no-build", "-f", framework, ...filter], {
    env,
    label: `${record ? "record" : "check"} on ${framework}`,
  });
}

/** Records or checks the manifests as the command line asks, and reports what a recording changed. */
function recordOrCheck() {
  const [command] = options._;
  assertCondition(options._.length === 1 && (command === "record" || command === "check"), usage);
  if (!options["no-build"]) runDotnet(["build", project, "-c", "Release"]);

  const before = readGoldenManifests(goldenDirectory);
  if (command === "record") runTests("net10.0", true);
  for (const framework of ["net8.0", "net10.0"]) runTests(framework, false);
  if (command !== "record") return;

  const after = readGoldenManifests(goldenDirectory);
  const bytes = [...after.values()].reduce((total, text) => total + Buffer.byteLength(text, "utf8"), 0);
  process.stdout.write(formatGoldenChanges(compareGoldenManifests(before, after)));
  console.log(`${after.size} manifests, ${(bytes / 1024).toFixed(1)} KiB. Review them with: git diff -- tests/CStructSharpTests/Engine/Golden`);
  console.log("Explain every changed entry in the commit message (CONTRIBUTING.md).");
}

await main(recordOrCheck);
