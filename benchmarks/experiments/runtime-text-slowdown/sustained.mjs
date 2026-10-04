// Sustained complete-write observations with optional production disassembly; never overwrites artifacts or builds during timing.
// Usage: node sustained.mjs BEFORE_CHECKOUT AFTER_CHECKOUT NEW_OUTPUT MODE ROUNDS SECONDS [disasm]
import fs from "node:fs";
import path from "node:path";
import { spawnSync } from "node:child_process";
import { sourceIdentity } from "../../../tools/lib/perf-bundles.mjs";

const [before, after, outputArg, mode, roundsArg, secondsArg, diagnostic] = process.argv.slice(2);
const rounds = Number(roundsArg);
const seconds = Number(secondsArg);
if (!before || !after || !outputArg || !mode || !Number.isInteger(rounds) || rounds < 1 || rounds > 10 ||
    !Number.isInteger(seconds) || seconds < 1 || seconds > 120 || (diagnostic && diagnostic !== "disasm"))
  throw new Error("Expected two checkouts, new output, mode, rounds, seconds and optional disasm.");
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
const launches = [];
for (let round = 1; round <= rounds; round++) {
  for (const side of round % 2 ? ["before", "after"] : ["after", "before"]) {
    const stem = `${side}-${round}`;
    const env = {
      ...process.env,
      DOTNET_TieredCompilation: "1",
      DOTNET_TieredPGO: "1",
      CSTRUCTSHARP_FIXTURES: path.join(checkouts.before, "benchmarks/fixtures"),
    };
    delete env.COMPlus_TieredCompilation;
    delete env.COMPlus_TieredPGO;
    if (diagnostic) {
      env.DOTNET_JitDisasm = "WriteText TryWriteSmallWideText WriteSmallBoundedText EncodeValidated GetBytes";
      env.DOTNET_JitStdOutFile = path.join(output, `${stem}.asm`);
    }
    const dll = path.join(checkouts[side], "benchmarks/experiments/runtime-text-slowdown/bin/Release/net10.0/RuntimeTextSlowdown.dll");
    console.log(`${diagnostic ? "Profile" : "Observe"} ${mode}: ${side} ${round}/${rounds}`);
    const result = spawnSync("dotnet", [dll, mode, String(seconds)], { env, encoding: "utf8", windowsHide: true });
    fs.writeFileSync(path.join(output, `${stem}.stdout`), result.stdout ?? "");
    fs.writeFileSync(path.join(output, `${stem}.stderr`), result.stderr ?? "");
    if (result.status !== 0) throw new Error(`Probe failed: ${stem}`);
    const data = JSON.parse(result.stdout);
    launches.push({ side, round, ...data });
    fs.writeFileSync(path.join(output, "launches.json"), JSON.stringify(launches, null, 2));
  }
}

/** Returns the middle observation, averaging the central pair for an even count. */
function median(values) {
  const sorted = values.toSorted((a, b) => a - b);
  const middle = Math.floor(sorted.length / 2);
  return sorted.length % 2 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2;
}

// Report every window and the predeclared second half separately; no sample is discarded from the raw observations.
const summary = launches.map(({ side, round, samples }) => ({
  side, round,
  medianNs: median(samples.map(s => s.nanoseconds)),
  firstHalfNs: median(samples.slice(0, samples.length / 2).map(s => s.nanoseconds)),
  secondHalfNs: median(samples.slice(samples.length / 2).map(s => s.nanoseconds)),
  minNs: Math.min(...samples.map(s => s.nanoseconds)),
  maxNs: Math.max(...samples.map(s => s.nanoseconds)),
  allocated: median(samples.map(s => s.bytesPerOperation)),
  jitMs: samples.reduce((sum, s) => sum + s.jitMs, 0),
  ilBytes: samples.reduce((sum, s) => sum + s.ilBytes, 0),
  gcPauseMs: samples.reduce((sum, s) => sum + s.gcPauseMs, 0),
  cpuRatio: samples.reduce((sum, s) => sum + s.cpuMs, 0) / samples.reduce((sum, s) => sum + s.elapsedMs, 0),
}));
fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify(summary, null, 2));
console.log(JSON.stringify(summary, null, 2));
