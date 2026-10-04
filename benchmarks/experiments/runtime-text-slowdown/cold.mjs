// Records three-way first-use comparisons through the existing OrdinaryNext probe; never builds while measuring.
// Usage: node cold.mjs ORIGINAL_CHECKOUT START_CHECKOUT FINAL_CHECKOUT NEW_OUTPUT
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { sourceIdentity } from "../../../tools/lib/perf-bundles.mjs";

const [original, start, final, outputArg] = process.argv.slice(2);
if (!original || !start || !final || !outputArg) throw new Error("Expected three built checkouts and a new output directory.");
const output = path.resolve(outputArg);
if (fs.existsSync(output)) throw new Error(`Refusing to overwrite ${output}`);
fs.mkdirSync(output, { recursive: true });
const checkouts = { original: path.resolve(original), start: path.resolve(start), final: path.resolve(final) };
const sources = {};
for (const [side, checkout] of Object.entries(checkouts)) {
  const sdk = spawnSync("dotnet", ["--version"], { cwd: checkout, encoding: "utf8", windowsHide: true });
  if (sdk.status !== 0) throw new Error(sdk.stderr);
  sources[side] = sourceIdentity(checkout, sdk.stdout.trim());
}
fs.writeFileSync(path.join(output, "sources.json"), JSON.stringify(sources, null, 2));
const rows = [];
const sides = Object.keys(checkouts);
for (const tiering of ["0", "1"]) {
  for (let round = 0; round < 10; round++) {
    for (let index = 0; index < sides.length; index++) {
      const side = sides[(round + index) % sides.length];
      for (const mode of ["write-256", "compile", "read-256"]) {
        const env = {
          ...process.env,
          DOTNET_TieredCompilation: tiering,
          DOTNET_TieredPGO: tiering,
          CSTRUCTSHARP_FIXTURES: path.join(checkouts.original, "benchmarks/fixtures"),
        };
        delete env.COMPlus_TieredCompilation;
        delete env.COMPlus_TieredPGO;
        const dll = path.join(checkouts[side], "benchmarks/experiments/ordinary-next/bin/Release/net10.0/OrdinaryNext.dll");
        const result = spawnSync("dotnet", [dll, mode], { env, encoding: "utf8", windowsHide: true });
        const stem = `${tiering}-${round}-${side}-${mode}`;
        fs.writeFileSync(path.join(output, `${stem}.stdout`), result.stdout ?? "");
        fs.writeFileSync(path.join(output, `${stem}.stderr`), result.stderr ?? "");
        if (result.status !== 0) throw new Error(`Cold probe failed: ${stem}`);
        rows.push({ side, round, tiering, ...JSON.parse(result.stdout) });
        fs.writeFileSync(path.join(output, "cold.json"), JSON.stringify(rows, null, 2));
      }
    }
  }
}
console.log(`Preserved ${rows.length} cold observations in ${output}`);
