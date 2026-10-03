#!/usr/bin/env node
// Development performance screen with immutable binaries, full BDN samples and optional targeted confirmation.
// Usage: node tools/quality/perf-check.mjs --capture before
//        node tools/quality/perf-check.mjs --baseline before [--no-build] [--cpu auto|none|N]
//        node tools/quality/perf-check.mjs --baseline before --confirm --filter '*PacketBenchmarks.ParseSpan*'
// Run serially on a quiet machine. Never interpret a short-run timing signal as a confirmed regression.
import fs from "node:fs";
import path from "node:path";
import crypto from "node:crypto";
import { spawnSync } from "node:child_process";
import { performance } from "node:perf_hooks";
import { fileURLToPath } from "node:url";
import { main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import {
  buildBundle,
  sourceIdentity,
  validateBundle,
  validateFixtures,
} from "../lib/perf-bundles.mjs";
import { compareRuns, readRun, renderReport } from "../lib/perf-report.mjs";

const usage =
  "perf-check.mjs --capture <name> | --baseline <name-or-bundle-path> [--confirm --filter <pattern>] [--rounds N] [--cpu auto|none|N] [--no-build] [--label name] [--checkout path]";

/** Validates bounded execution options before reading bundles, building or starting benchmarks. */
export function optionsFor(argv) {
  const options = parseArguments(
    argv,
    {
      capture: "string",
      baseline: "string",
      confirm: "flag",
      filter: "repeat",
      rounds: "number",
      cpu: "string",
      "no-build": "flag",
      label: "string",
      checkout: "string",
      threshold: "number",
      help: "flag",
    },
    {
      defaults: {
        confirm: false,
        filter: [],
        cpu: "auto",
        "no-build": false,
        threshold: 0.03,
        checkout: repositoryRoot,
      },
    },
  );
  if (options.help) return options;
  if (Boolean(options.capture) === Boolean(options.baseline))
    throw new Error(`Choose capture or baseline. ${usage}`);
  for (const name of [options.capture, options.label])
    if (name !== undefined && !/^[a-zA-Z0-9][a-zA-Z0-9_-]{0,70}$/.test(name))
      throw new Error("Capture/label must be a simple name of at most 71 characters.");
  if (
    !/^(auto|none|\d+)$/.test(options.cpu) ||
    (/^\d+$/.test(options.cpu) && Number(options.cpu) > 62)
  )
    throw new Error("CPU must be auto, none, or a logical index in 0..62.");
  options.rounds ??= options.confirm ? 3 : 1;
  if (
    !Number.isInteger(options.rounds) ||
    options.rounds < (options.confirm ? 3 : 1) ||
    options.rounds > 20
  )
    throw new Error("Rounds must be an integer in 1..20 (at least 3 for confirmation).");
  if (!(options.threshold > 0 && options.threshold < 1))
    throw new Error("Threshold must be between zero and one.");
  if (options.confirm && !options.filter.length)
    throw new Error(
      "Confirmation requires an explicit --filter; use '*' only when a long full-suite run is intended.",
    );
  if (options.capture && (options.confirm || options["no-build"] || options.filter.length))
    throw new Error("Capture builds a complete bundle and does not accept measurement options.");
  return options;
}

/** Starts one completed BDN host, preserving its log on failure; no builds occur during measurement. */
function measure(bundle, output, options, round, side) {
  fs.mkdirSync(output, { recursive: true });
  const log = path.join(output, "host.log");
  const fd = fs.openSync(log, "wx");
  const args = [
    path.join(bundle, "host/CStructSharp.Benchmarks.dll"),
    "--filter",
    ...(options.filter.length ? options.filter : ["*"]),
    "--anyCategories",
    "Impact",
  ];
  const env = {
    ...process.env,
    DOTNET_TieredCompilation: "0",
    DOTNET_TieredPGO: "0",
    CSTRUCTSHARP_BENCHMARK_JOB: options.confirm ? "Confirm" : "Screen",
    CSTRUCTSHARP_BENCHMARK_RUNTIMES: "net10.0",
    CSTRUCTSHARP_BENCHMARK_CPU: options.cpu,
    CSTRUCTSHARP_BENCHMARK_ARTIFACTS: output,
    CSTRUCTSHARP_FIXTURES: path.join(bundle, "fixtures"),
  };
  delete env.CSTRUCTSHARP_BENCHMARK_PROFILE;
  delete env.COMPlus_TieredCompilation;
  delete env.COMPlus_TieredPGO;
  const started = performance.now();
  const result = spawnSync("dotnet", args, {
    cwd: bundle,
    env,
    stdio: ["ignore", fd, fd],
    windowsHide: true,
    timeout: options.confirm ? 60 * 60 * 1000 : 60 * 1000,
  });
  fs.closeSync(fd);
  if (result.status !== 0)
    throw new Error(
      `Benchmark host failed (${result.error?.message ?? result.status}); see ${log}`,
    );
  const run = readRun(output, options.confirm ? 30 : 12);
  if (
    run.environment.protocol !== 1 ||
    run.environment.tiering !== "0" ||
    run.environment.requestedCpu !== options.cpu
  )
    throw new Error("Host did not apply the requested development controls.");
  // Record runtime overrides so a later inspection can distinguish diagnostic mode from deployment settings.
  const runtimeOverrides = Object.fromEntries(
    Object.entries(env).filter(([key]) => /^(DOTNET_|COMPlus_)/.test(key)),
  );
  return {
    ...run,
    round,
    side,
    seconds: (performance.now() - started) / 1000,
    command: { executable: "dotnet", args },
    runtimeOverrides,
  };
}

/** Captures a baseline or builds/reuses a verified candidate and serially compares it with the immutable baseline. */
export function checkPerformance(argv = process.argv.slice(2)) {
  const invoked = performance.now() - process.uptime() * 1000;
  const options = optionsFor(argv);
  if (options.help) {
    console.log(usage);
    return;
  }
  const checkout = path.resolve(options.checkout);
  const root = path.join(repositoryRoot, "artifacts/perf/development");
  const bundles = path.join(root, "bundles");
  fs.mkdirSync(bundles, { recursive: true });
  // A repository-wide run lock prevents this tool from measuring while another invocation is building or measuring.
  const lock = path.join(root, "active.lock");
  let lockFile;
  try {
    lockFile = fs.openSync(lock, "wx");
  } catch {
    throw new Error(
      `Another performance operation is active. If it crashed, check the PID in ${lock} and verify no benchmark/build remains before removing that lock.`,
    );
  }
  fs.writeFileSync(
    lockFile,
    JSON.stringify({ pid: process.pid, startedUtc: new Date().toISOString() }),
  );
  try {
    const sdkResult = spawnSync("dotnet", ["--version"], {
      cwd: checkout,
      encoding: "utf8",
      windowsHide: true,
    });
    if (sdkResult.status !== 0)
      throw sdkResult.error ?? new Error("Cannot resolve the SDK selected by global.json.");
    const source = sourceIdentity(checkout, sdkResult.stdout.trim());
    const runName =
      options.label ??
      `${new Date().toISOString().replaceAll(/[:.]/g, "-")}-${crypto.randomBytes(3).toString("hex")}`;
    const output = path.join(root, "runs", runName);
    if (fs.existsSync(output)) throw new Error(`Report directory already exists: ${output}`);
    fs.mkdirSync(output, { recursive: true });
    if (options.capture) {
      const destination = path.join(bundles, options.capture);
      buildBundle(checkout, destination, source, path.join(output, "build.log"));
      // This same fresh build is safe to reuse as the candidate until any source/SDK input changes.
      fs.writeFileSync(
        path.join(root, "current.json"),
        JSON.stringify({ bundle: destination, sourceDigest: source.digest }),
      );
      console.log(
        `Captured ${destination}\nSetup/build: ${((performance.now() - invoked) / 1000).toFixed(2)} s. No measurements were reused.`,
      );
      return;
    }
    const baseline = /^[a-zA-Z0-9][a-zA-Z0-9_-]*$/.test(options.baseline)
      ? path.join(bundles, options.baseline)
      : path.resolve(options.baseline);
    const baselineManifest = validateBundle(baseline);
    const cachePath = path.join(root, "current.json");
    const cache = fs.existsSync(cachePath) ? JSON.parse(fs.readFileSync(cachePath, "utf8")) : null;
    let candidate;
    if (cache?.sourceDigest === source.digest) {
      if (validateBundle(cache.bundle).source.digest !== source.digest)
        throw new Error("Candidate cache points to a different source snapshot.");
      candidate = cache.bundle;
    } else {
      if (options["no-build"])
        throw new Error(
          "Candidate build is stale or absent. Rerun without --no-build to rebuild and capture current sources.",
        );
      candidate = path.join(bundles, `candidate-${crypto.randomUUID()}`);
      console.log("Building the changed candidate before measurement…");
      buildBundle(checkout, candidate, source, path.join(output, "build.log"));
      fs.writeFileSync(
        cachePath,
        JSON.stringify({ bundle: candidate, sourceDigest: source.digest }),
      );
    }
    const candidateManifest = validateBundle(candidate);
    validateFixtures(baselineManifest, candidateManifest);
    const setupSeconds = (performance.now() - invoked) / 1000;
    const started = performance.now();
    const before = [],
      after = [],
      launches = [];
    const firstBefore = crypto.randomInt(2) === 0;
    for (let round = 0; round < options.rounds; round++) {
      const order = (round % 2 === 0) === firstBefore ? ["before", "after"] : ["after", "before"];
      for (const side of order) {
        const run = measure(
          side === "before" ? baseline : candidate,
          path.join(output, `${side}-${round + 1}`),
          options,
          round,
          side,
        );
        (side === "before" ? before : after).push(run);
        launches.push({ side, round, seconds: run.seconds, command: run.command });
      }
    }
    // Report only immutable inputs; an overwritten bundle or a concurrent source edit invalidates this invocation.
    validateBundle(baseline);
    validateBundle(candidate);
    if (sourceIdentity(checkout, source.sdk).digest !== source.digest)
      throw new Error(
        "Sources changed during measurement; retained raw runs refer to the recorded bundle, not the new edits.",
      );
    const comparison = compareRuns(before, after, options.threshold, options.confirm);
    const report = {
      schemaVersion: 1,
      mode: options.confirm ? "Confirm" : "Screen",
      threshold: options.threshold,
      rounds: options.rounds,
      selection: options.filter.length ? options.filter : ["all Impact"],
      environment: before[0].environment,
      baseline: { path: baseline, manifest: baselineManifest },
      candidate: { path: candidate, manifest: candidateManifest },
      currentSource: source,
      setupSeconds,
      measureSeconds: (performance.now() - started) / 1000,
      launches,
      before,
      after,
      ...comparison,
    };
    fs.writeFileSync(path.join(output, "report.json"), JSON.stringify(report, null, 2) + "\n");
    const markdown = renderReport(report);
    fs.writeFileSync(path.join(output, "report.md"), markdown);
    console.log(markdown);
    console.log(`Report: ${path.join(output, "report.md")}`);
    // Suggest the canary when it drifted; otherwise show a runnable command for the largest observed timing delta.
    const target =
      comparison.canary === "drift"
        ? comparison.rows.find((row) => row.method === "HandWritten_PrimRecord")
        : [...comparison.rows].sort((a, b) => Math.abs(b.delta) - Math.abs(a.delta))[0];
    console.log(
      `Suggested confirmation (all parameters of this method): node tools/quality/perf-check.mjs --baseline '${options.baseline.replaceAll("'", "''")}' --confirm --cpu ${options.cpu} --filter '*${target.type}.${target.method}*'`,
    );
    const wallSeconds = (performance.now() - invoked) / 1000;
    fs.writeFileSync(
      path.join(output, "wall.json"),
      JSON.stringify(
        { setupSeconds, comparisonSeconds: wallSeconds - setupSeconds, wallSeconds },
        null,
        2,
      ),
    );
    console.log(
      `Invocation through report: ${wallSeconds.toFixed(2)} s (${setupSeconds.toFixed(2)} s setup/build included).`,
    );
  } finally {
    fs.closeSync(lockFile);
    fs.unlinkSync(lock);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  await main(checkPerformance);
