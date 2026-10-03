#!/usr/bin/env node
/**
 * Fast managed development checks; build selected projects first, then run bounded test hosts.
 * Usage: node tools/quality/test-managed.mjs [--suite runtime|parity|generator|all] [--full]
 *        [--filter 'FullyQualifiedName~ClassName'] [--no-build] [--jobs 1..4] [--workers 1..32]
 * Default: .NET 10 runtime tests excluding Extended/OptIn. --full defaults to all suites and both supported runtimes.
 */
import fs from "node:fs";
import path from "node:path";
import { spawn, spawnSync } from "node:child_process";
import { performance } from "node:perf_hooks";
import { fileURLToPath } from "node:url";
import { main, repositoryRoot } from "../lib/tooling.mjs";
import { runBounded, testEvidence, testPlan } from "../lib/managed-tests.mjs";

/** Builds and tests the requested scope, retaining logs/TRX and reporting build, test and total wall time. */
export async function testManaged(argv = process.argv.slice(2)) {
  const started = performance.now() - process.uptime() * 1000;
  const plan = testPlan(argv);
  const { options } = plan;
  if (options.help) {
    console.log(
      "test-managed.mjs [--suite runtime|parity|generator|all] [--full] [--filter expression] [--no-build] [--jobs 1..4] [--workers 1..32]",
    );
    console.log(
      "Default: .NET 10 runtime development checks. --full: all suites, Extended sweeps, .NET 8 and .NET 10. CI coverage remains separate.",
    );
    return;
  }
  if (
    process.env.UPDATE_SNAPSHOTS === "1" ||
    process.env.CSTRUCTSHARP_ENGINE_GOLDEN_RECORD === "1"
  ) {
    throw new Error(
      "This command verifies tests; use the documented dedicated workflow to record snapshots or golden outcomes.",
    );
  }
  const root = path.join(repositoryRoot, "artifacts/test-results/development");
  fs.mkdirSync(root, { recursive: true });
  const output = fs.mkdtempSync(path.join(root, "run-"));
  const hosts = new Set();
  let interrupted = false;
  /** Stops owned process trees on cancellation so test hosts cannot outlive the reported operation. */
  const cancel = () => {
    interrupted = true;
    for (const child of hosts) {
      if (!child.pid) continue;
      if (process.platform === "win32")
        spawnSync("taskkill", ["/pid", String(child.pid), "/T", "/F"], {
          windowsHide: true,
          stdio: "ignore",
        });
      else {
        try {
          process.kill(-child.pid, "SIGTERM");
        } catch (error) {
          if (error.code !== "ESRCH") throw error;
        }
      }
    }
  };
  process.on("SIGINT", cancel);
  process.on("SIGTERM", cancel);

  /** Executes one build or test without a shell, preserving its complete log on either success or failure. */
  const execute = (name, args) =>
    new Promise((resolve, reject) => {
      if (interrupted) {
        reject(new Error("Test run cancelled."));
        return;
      }
      const log = path.join(output, `${name}.log`);
      const fd = fs.openSync(log, "wx");
      const begin = performance.now();
      const child = spawn("dotnet", args, {
        cwd: repositoryRoot,
        stdio: ["ignore", fd, fd],
        windowsHide: true,
        detached: process.platform !== "win32",
      });
      hosts.add(child);
      let failure;
      // A spawn error is reported by close as well; retain the original message and release the log exactly once.
      child.on("error", (error) => {
        failure = error;
      });
      // Completion means output is closed and the next build or report can safely use these files.
      child.on("close", (code) => {
        hosts.delete(child);
        fs.closeSync(fd);
        if (failure || code !== 0 || interrupted) {
          console.error(fs.readFileSync(log, "utf8"));
          reject(
            new Error(
              `${name}: ${failure?.message ?? (interrupted ? "cancelled" : `exit ${code}`)}. Log: ${log}`,
            ),
          );
        } else
          resolve({
            name,
            seconds: (performance.now() - begin) / 1000,
            command: ["dotnet", ...args],
            log,
          });
      });
    });

  try {
    console.log(
      `${options.full ? "Full" : "Development"} profile; suite ${options.suite}${options.filter ? "; filtered selection" : ""}; at most ${options.jobs} hosts × ${options.workers} test workers.`,
    );
    if (!options.full)
      console.log(
        "Extended sweeps and .NET 8 are omitted. Use --full before review; CI still runs the full suites.",
      );
    console.log(`Logs and results: ${output}`);
    const builds = [];
    if (options["no-build"])
      console.log("Build skipped by request: you must ensure Release binaries match your source.");
    else {
      for (const suite of plan.suites) {
        console.log(`Building ${suite.name}…`);
        const args = ["build", suite.project, "-c", "Release", "--nologo"];
        if (!options.full) args.push("-f", "net10.0");
        builds.push(await execute(`build-${suite.name}`, args));
      }
    }
    const buildSeconds = (performance.now() - started) / 1000;
    const testStarted = performance.now();
    // Every selected project is built before any test host starts, avoiding races on shared project outputs.
    const outcome = await runBounded(plan.runs, options.jobs, async (run) => {
      const directory = path.join(output, run.name);
      fs.mkdirSync(directory);
      const args = [
        "test",
        run.project,
        "-c",
        "Release",
        "-f",
        run.framework,
        "--no-build",
        "--nologo",
        "--filter",
        run.filter,
        "--logger",
        "trx;LogFileName=tests.trx",
        "--results-directory",
        directory,
        "--",
        `MSTest.Parallelize.Workers=${options.workers}`,
        "MSTest.Parallelize.Scope=MethodLevel",
      ];
      console.log(`Testing ${run.name}…`);
      const result = await execute(run.name, args);
      const evidence = testEvidence(path.join(directory, "tests.trx"));
      console.log(`${run.name}: ${evidence.count} passed in ${result.seconds.toFixed(2)} s.`);
      return { ...result, ...evidence };
    });
    const summary = {
      schemaVersion: 1,
      plan,
      builds,
      ...outcome,
      buildSeconds,
      testSeconds: (performance.now() - testStarted) / 1000,
      wallSeconds: (performance.now() - started) / 1000,
    };
    fs.writeFileSync(path.join(output, "summary.json"), JSON.stringify(summary, null, 2) + "\n");
    console.log(
      `Build/setup ${buildSeconds.toFixed(2)} s; tests ${summary.testSeconds.toFixed(2)} s; total ${summary.wallSeconds.toFixed(2)} s.`,
    );
    for (const result of outcome.results) {
      if (result.error) console.error(`${result.name}: ${result.error}`);
    }
    if (outcome.failed || outcome.completed !== plan.runs.length)
      throw new Error(
        `Managed checks failed or were incomplete. See ${path.join(output, "summary.json")}`,
      );
  } finally {
    process.off("SIGINT", cancel);
    process.off("SIGTERM", cancel);
  }
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url))
  await main(testManaged);
