#!/usr/bin/env node
/**
 * Runs complete permanent-scope mutation partitions and verifies their aggregation.
 * Usage: node tools/quality/mutation-partitions.mjs --mode plan|run|aggregate|memory
 *   [--partition p00] [--output-directory artifacts/mutation] [--input-directory artifacts/mutation-input]
 * Run mode requires restored tools/projects and an otherwise idle build output. Each output directory must be new.
 */
import assert from "node:assert/strict";
import fs from "node:fs";
import path from "node:path";
import { spawnSync, execFileSync } from "node:child_process";
import { main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";
import { listFiles } from "../lib/files.mjs";
import { aggregateMutationPartitions, mutationFileHash, mutationInvocation, planMutationPartitions, requireCompletedMutants, requireCoreMutationTests } from "../lib/mutation-partitions.mjs";

const options = parseArguments(process.argv.slice(2), {
  mode: "string", partition: "string", "output-directory": "string", "input-directory": "string",
}, { defaults: { "output-directory": "artifacts/mutation", "input-directory": "artifacts/mutation-input" } });
const configPath = path.join(repositoryRoot, "stryker-config.json");
const config = JSON.parse(fs.readFileSync(configPath, "utf8"))["stryker-config"];
assert.equal(config.mutate.length, 71, "The complete permanent scope must retain its 71 reviewed files");
const partitions = planMutationPartitions(repositoryRoot, config);
const output = path.resolve(repositoryRoot, options["output-directory"]);

/** Returns the exact checked-out commit, shared by all jobs in the dispatched workflow. */
function sourceSha() {
  return execFileSync("git", ["rev-parse", "HEAD"], { cwd: repositoryRoot, encoding: "utf8" }).trim();
}

/** Serializes one evidence object with a trailing newline. */
function writeJson(filename, value) {
  fs.writeFileSync(filename, `${JSON.stringify(value, null, 2)}\n`);
}

/** Creates a new output directory without deleting or reusing an earlier mutation report. */
function createOutput() {
  assert.ok(!fs.existsSync(output), `Output already exists; choose a fresh directory: ${output}`);
  fs.mkdirSync(output, { recursive: true });
}

/** Runs the pinned Stryker tool, returning its outcome and elapsed seconds even when its score gate fails. */
function runMutation(configuration, patterns = []) {
  const { cwd, args } = mutationInvocation(repositoryRoot, configuration, output, patterns);
  const started = Date.now();
  const result = spawnSync("dotnet", args, { cwd, stdio: "inherit" });
  return { exitCode: result.status, signal: result.signal, error: result.error?.message,
    elapsedSeconds: (Date.now() - started) / 1000, cwd, args };
}

/** Requires a complete memory report and enforces the existing 75% score floor independently of permanent scope. */
function verifyMemory(report) {
  requireCoreMutationTests(report);
  assert.equal(String(report.schemaVersion), "2");
  assert.deepEqual(report.thresholds, { high: 90, low: 80 });
  const counts = new Map();
  const measured = new Set();
  const sources = listFiles(path.join(repositoryRoot, "src/CStructSharp/Memory"), (filename) => filename.endsWith(".cs"));
  for (const [filename, file] of Object.entries(report.files ?? {})) {
    const mutants = file.mutants ?? [];
    const normalized = filename.replaceAll("\\", "/");
    const selected = `/${normalized}`.toLowerCase().includes("/cstructsharp/memory/");
    if (!selected) {
      assert.ok(mutants.every((mutant) => ["Ignored", "CompileError"].includes(mutant.status)), `Memory run tested an out-of-scope file: ${filename}`);
      continue;
    }
    // Include declarations with no mutation opportunities; require the full selected source population to be present.
    const matching = sources.filter((source) => `/${normalized}`.toLowerCase().endsWith(`/cstructsharp/memory/${path.relative(path.join(repositoryRoot, "src/CStructSharp/Memory"), source).replaceAll("\\", "/")}`.toLowerCase()));
    assert.equal(matching.length, 1, `Unknown memory source ${filename}`);
    assert.ok(!measured.has(matching[0]), `Duplicate memory source ${filename}`);
    measured.add(matching[0]);
    assert.equal(file.source.replaceAll("\r\n", "\n"), fs.readFileSync(matching[0], "utf8").replaceAll("\r\n", "\n"), `Memory report source changed: ${filename}`);
    requireCompletedMutants(mutants);
    for (const mutant of mutants) counts.set(mutant.status, (counts.get(mutant.status) ?? 0) + 1);
  }
  assert.equal(measured.size, sources.length, "The memory report is missing configured source files");
  const detected = (counts.get("Killed") ?? 0) + (counts.get("Timeout") ?? 0);
  const valid = detected + (counts.get("Survived") ?? 0) + (counts.get("NoCoverage") ?? 0) + (counts.get("RuntimeError") ?? 0);
  assert.ok(valid > 0 && detected / valid >= 0.75, `Memory mutation score failed: ${detected}/${valid}`);
  return { detected, valid, score: 100 * detected / valid, counts: Object.fromEntries(counts) };
}

// A single entry point keeps partition planning identical in the matrix, runner and aggregator.
await main(() => {
  if (options.mode === "plan") {
    // The workflow reads this JSON directly as its matrix; stdout must contain no progress text here.
    console.log(JSON.stringify({ include: partitions.map((partition) => ({ id: partition.id, files: partition.files.length })) }));
    return;
  }
  if (options.mode === "run") {
    const partition = partitions.find((candidate) => candidate.id === options.partition);
    assert.ok(partition, `Unknown partition ${options.partition}`);
    createOutput();
    const sha = sourceSha();
    const outcome = runMutation(configPath, partition.files.map((file) => file.pattern));
    const reportPath = path.join(output, "reports/mutation-report.json");
    writeJson(path.join(output, "partition.json"), { schemaVersion: 1, partition, sourceSha: sha,
      configSha256: mutationFileHash(configPath), reportSha256: fs.existsSync(reportPath) ? mutationFileHash(reportPath) : null,
      ...outcome });
    assert.ok(fs.existsSync(reportPath), `Partition ${partition.id} produced no final report`);
    requireCoreMutationTests(JSON.parse(fs.readFileSync(reportPath, "utf8")));
    assert.equal(outcome.exitCode, 0, `Stryker failed for ${partition.id}; retain its report and log for diagnosis`);
    return;
  }
  if (options.mode === "memory") {
    createOutput();
    const outcome = runMutation(path.join(repositoryRoot, "stryker-memory-config.json"));
    const reportPath = path.join(output, "reports/mutation-report.json");
    writeJson(path.join(output, "execution.json"), { sourceSha: sourceSha(), ...outcome });
    assert.ok(fs.existsSync(reportPath), "Memory mutation produced no final report");
    const summary = verifyMemory(JSON.parse(fs.readFileSync(reportPath, "utf8")));
    writeJson(path.join(output, "summary.json"), summary);
    assert.equal(outcome.exitCode, 0, "Memory mutation failed; retain its report and log for diagnosis");
    console.log(`Memory mutation passed: ${summary.detected}/${summary.valid}, ${summary.score.toFixed(2)}%`);
    return;
  }
  assert.equal(options.mode, "aggregate", "Choose --mode plan, run, aggregate or memory");
  const input = path.resolve(repositoryRoot, options["input-directory"]);
  // Downloaded artifacts retain one directory per partition; missing or extra folders cannot pass unnoticed.
  const directories = fs.readdirSync(input).filter((name) => fs.statSync(path.join(input, name)).isDirectory());
  assert.equal(directories.length, partitions.length, "Missing or extra mutation artifacts");
  const reports = [];
  const executions = [];
  for (const directory of directories) {
    const full = path.join(input, directory);
    const execution = JSON.parse(fs.readFileSync(path.join(full, "partition.json"), "utf8"));
    const expected = partitions.find((partition) => partition.id === execution.partition?.id);
    assert.ok(expected, `Unexpected partition artifact ${directory}`);
    assert.deepEqual(execution.partition, expected, `Partition scope changed: ${directory}`);
    assert.equal(execution.sourceSha, sourceSha(), `Wrong source revision in ${directory}`);
    assert.equal(execution.configSha256, mutationFileHash(configPath), `Wrong configuration in ${directory}`);
    const reportPath = path.join(full, "reports/mutation-report.json");
    assert.equal(execution.reportSha256, mutationFileHash(reportPath), `Report identity changed: ${directory}`);
    reports.push({ id: expected.id, report: JSON.parse(fs.readFileSync(reportPath, "utf8")) });
    executions.push(execution);
  }
  const combined = aggregateMutationPartitions(repositoryRoot, partitions, reports);
  createOutput();
  const combinedPath = path.join(output, "mutation-report.json");
  writeJson(combinedPath, combined);
  writeJson(path.join(output, "aggregation.json"), { sourceSha: sourceSha(), configSha256: mutationFileHash(configPath),
    reportSha256: mutationFileHash(combinedPath), executions,
    strykerElapsedSecondsSum: executions.reduce((sum, execution) => sum + execution.elapsedSeconds, 0) });
  // Keep the canonical full-scope validator as the authority for score and zero-survivor requirements.
  const checked = spawnSync(process.execPath, ["tools/quality/mutation-report.mjs", "--report-path", combinedPath], { cwd: repositoryRoot, stdio: "inherit" });
  assert.equal(checked.status, 0, "The complete permanent mutation gate failed");
  assert.ok(executions.every((execution) => execution.exitCode === 0), "At least one partition runner failed");
});
