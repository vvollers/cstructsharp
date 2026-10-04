// First-use and generated-writer size observations using the existing fixtures, one operation per fresh process.
// Usage: node benchmarks/experiments/overall-next/cold.mjs BEFORE_CHECKOUT AFTER_CHECKOUT NEW_OUTPUT
// Build OverallNext.csproj in both checkouts first; run alone without builds, tests or agent work.
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { sourceIdentity } from "../../../tools/lib/perf-bundles.mjs";

const [before, after, outputArg] = process.argv.slice(2);
if (!before || !after || !outputArg) throw new Error("Expected two built checkouts and a new output directory.");
const output = path.resolve(outputArg);
if (fs.existsSync(output)) throw new Error(`Refusing to overwrite ${output}`);
fs.mkdirSync(output, { recursive: true });
const checkouts = { before: path.resolve(before), after: path.resolve(after) };
const sources = {};
for (const [side, checkout] of Object.entries(checkouts)) {
  const sdk = spawnSync("dotnet", ["--version"], { cwd: checkout, encoding: "utf8", windowsHide: true });
  if (sdk.status !== 0) throw new Error(sdk.stderr);
  sources[side] = sourceIdentity(checkout, sdk.stdout.trim());
}
fs.writeFileSync(path.join(output, "sources.json"), JSON.stringify(sources, null, 2));

/**
 * Runs the selected side and mode under the requested tiering and optional environment/argument overrides.
 * Returns its standard output or rejects a failed process; never compiles or reuses a measured process.
 */
function run(side, mode, tiering, extra = {}, args = []) {
  const env = {
    ...process.env,
    DOTNET_TieredCompilation: tiering,
    DOTNET_TieredPGO: tiering,
    CSTRUCTSHARP_FIXTURES: path.join(checkouts.before, "benchmarks/fixtures"),
    ...extra,
  };
  delete env.COMPlus_TieredCompilation;
  delete env.COMPlus_TieredPGO;
  const dll = path.join(checkouts[side], "benchmarks/experiments/overall-next/bin/Release/net10.0/OverallNext.dll");
  const result = spawnSync("dotnet", [dll, mode, ...args], { env, encoding: "utf8", windowsHide: true });
  if (result.status !== 0) throw new Error(result.stderr || result.stdout);
  return result.stdout;
}

const rows = [];
for (const tiering of ["0", "1"]) {
  for (let round = 1; round <= 10; round++) {
    for (const side of round % 2 ? ["before", "after"] : ["after", "before"]) {
      for (const mode of ["nested", "matrix"]) {
        rows.push({ side, round, tiering, ...JSON.parse(run(side, mode, tiering)) });
        fs.writeFileSync(path.join(output, "cold.json"), JSON.stringify(rows, null, 2));
      }
    }
  }
}
for (const side of ["before", "after"]) {
  fs.writeFileSync(path.join(output, `${side}-writer-il.jsonl`), run(side, "sizes", "0"));
  for (const mode of ["nested", "matrix"]) {
    fs.writeFileSync(path.join(output, `${side}-${mode}-allocations.json`), run(side, "allocations", "0", {}, [mode]));
  }
  run(side, "matrix", "0", {
    DOTNET_JitDisasm: "EncodeRoot",
    DOTNET_JitDisasmAssemblies: "CStructSharp.Benchmarks",
    DOTNET_JitStdOutFile: path.join(output, `${side}-matrix.asm`),
  });
  run(side, "nested", "0", {
    DOTNET_JitDisasm: "RunStaticPlan",
    DOTNET_JitDisasmAssemblies: "CStructSharp",
    DOTNET_JitStdOutFile: path.join(output, `${side}-nested.asm`),
  });
}
console.log(`Retained ${rows.length} first-use observations in ${output}`);
