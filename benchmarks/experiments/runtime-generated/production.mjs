// Standard out-of-process BDN confirmation, with each revision built from its own checkout.
// Usage: node benchmarks/experiments/runtime-generated/production.mjs BEFORE_CHECKOUT AFTER_CHECKOUT OUTPUT [FILTER ...]
// PERF_WARMUPS overrides six warmups when investigating tier transitions; every launch is retained.
// Build both benchmark projects first. Runs, including BDN's generated builds, are strictly serial.
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { readRun, compareRuns, renderReport } from "../../../tools/lib/perf-report.mjs";
import { sourceIdentity } from "../../../tools/lib/perf-bundles.mjs";

const [beforeArg, afterArg, outputArg, ...requested] = process.argv.slice(2);
if (!beforeArg || !afterArg || !outputArg)
  throw new Error("Expected two checkouts and a new output directory.");
const warmups = Number(process.env.PERF_WARMUPS ?? 6);
if (!Number.isInteger(warmups) || warmups < 6 || warmups > 100)
  throw new Error("PERF_WARMUPS must be an integer in 6..100.");
const checkouts = { before: path.resolve(beforeArg), after: path.resolve(afterArg) };
const output = path.resolve(outputArg);
if (fs.existsSync(output)) throw new Error(`Refusing to overwrite ${output}`);
fs.mkdirSync(output, { recursive: true });
const identities = {};
for (const [side, cwd] of Object.entries(checkouts)) {
  const sdk = spawnSync("dotnet", ["--version"], { cwd, encoding: "utf8", windowsHide: true });
  if (sdk.status !== 0) throw new Error(`Cannot resolve SDK in ${cwd}`);
  identities[side] = sourceIdentity(cwd, sdk.stdout.trim());
}
fs.writeFileSync(path.join(output, "sources.json"), JSON.stringify(identities, null, 2));
const filters = requested.length
  ? requested
  : [
      "*GeneratedBenchmarks.*PrimRecord_Parse",
      "*GeneratedBenchmarks.*PrimRecord_Serialize",
      "*GeneratedBenchmarks.*Nested256_Parse",
      "*GeneratedBenchmarks.*Nested256_Serialize",
      "*GeneratedBenchmarks.HandWritten_PrimRecord",
    ];
const runs = { before: [], after: [] };
const order = [];
const start = performance.now();
for (let round = 1; round <= 3; round++) {
  for (const side of round % 2 ? ["before", "after"] : ["after", "before"]) {
    const directory = path.join(output, `${side}-${round}`);
    fs.mkdirSync(directory);
    const fd = fs.openSync(path.join(directory, "host.log"), "wx");
    const env = {
      ...process.env,
      DOTNET_TieredCompilation: "1",
      DOTNET_TieredPGO: "1",
      CSTRUCTSHARP_BENCHMARK_JOB: "Short",
      CSTRUCTSHARP_BENCHMARK_RUNTIMES: "net10.0",
      CSTRUCTSHARP_BENCHMARK_ARTIFACTS: directory,
      CSTRUCTSHARP_FIXTURES: path.join(checkouts.before, "benchmarks/fixtures"),
    };
    delete env.COMPlus_TieredCompilation;
    delete env.COMPlus_TieredPGO;
    const benchmarkDirectory = path.join(checkouts[side], "benchmarks/CStructSharp.Benchmarks");
    const args = [
      path.join(benchmarkDirectory, "bin/Release/net10.0/CStructSharp.Benchmarks.dll"),
      "--filter",
      ...filters,
      "--warmupCount",
      String(warmups),
      "--iterationCount",
      "15",
      "--iterationTime",
      "250",
      "--affinity",
      "65536",
    ];
    console.log(`Production out-of-process: ${side} round ${round}/3`);
    order.push({ side, round, cwd: benchmarkDirectory, args });
    fs.writeFileSync(path.join(output, "order.json"), JSON.stringify(order, null, 2));
    // Use isolated checkouts: BDN also searches their solution roots when discovering the project.
    const result = spawnSync("dotnet", args, {
      cwd: benchmarkDirectory,
      env,
      stdio: ["ignore", fd, fd],
      windowsHide: true,
    });
    fs.closeSync(fd);
    if (result.status !== 0) throw new Error(`Host failed: ${directory}`);
    fs.writeFileSync(
      path.join(directory, "development-environment.json"),
      JSON.stringify({
        protocol: 1,
        selectedCpu: 16,
        tiering: "1",
        pgo: "1",
        job: `Short, 15 x 250 ms, ${warmups} warmups, out-of-process`,
      }),
    );
    runs[side].push(readRun(directory, 15));
  }
}
const report = {
  mode: "Production out-of-process",
  rounds: 3,
  threshold: 0.03,
  environment: runs.before[0].environment,
  ...compareRuns(runs.before, runs.after, 0.03, true),
  setupSeconds: 0,
  measureSeconds: (performance.now() - start) / 1000,
  runs,
};
fs.writeFileSync(path.join(output, "report.json"), JSON.stringify(report, null, 2));
fs.writeFileSync(path.join(output, "report.md"), renderReport(report));
console.log(renderReport(report));
