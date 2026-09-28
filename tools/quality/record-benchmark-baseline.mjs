#!/usr/bin/env node
// Records a scenario drift baseline (contracts/performance/drift-scenarios.json) from one or more normalized
// benchmark summaries (convert-benchmark-baseline.mjs output). Cases are keyed by type|method|parameters|runtime so
// net8.0 and net10.0 are recorded separately. Environment capture (dotnet --info, CPU, OS, git revision, fixture
// manifest hash) is embedded as baselineEvidence, matching the release-gate fields.
//
// Usage: node tools/quality/record-benchmark-baseline.mjs --output contracts/performance/drift-scenarios.json
//        --summary <summary.json> [--summary <more.json>] [--budget-id drift-scenarios] [--job Short]
//        [--merge --note "<why these cases changed>"]   # replace only the cases present in the summaries, keep the rest
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import { parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";

const usage = "Usage: record-benchmark-baseline.mjs --output <contract.json> --summary <summary.json> [...]";
let options;
try {
  // Summary paths are kept whole ("repeat"), since a path may contain a comma.
  options = parseArguments(
    process.argv.slice(2),
    { output: "string", summary: "repeat", "budget-id": "string", job: "string", merge: "flag", note: "string", "allow-new": "flag" },
    { defaults: { summary: [], "budget-id": "drift-scenarios", job: "Short", merge: false, note: "partial re-baseline", "allow-new": false } },
  );
} catch (error) {
  console.error(`${error.message}\n${usage}`);
  process.exit(2);
}
const summaries = options.summary;
const output = options.output;
if (!output || summaries.length === 0) {
  console.error(usage);
  process.exit(2);
}
/** Runs a command from the repository root and returns its trimmed standard output, or null when it fails. */
const run = (cmd, a) => {
  const result = runCommand(cmd, a, { allowFailure: true });
  return result.status === 0 ? result.stdout.trim() : null;
};
/** Returns the uppercase hexadecimal SHA-256 hash of a buffer. */
const sha256 = (buffer) => crypto.createHash("sha256").update(buffer).digest("hex").toUpperCase();
/** Extracts the runtime (such as `.NET 10.0.0`) from a benchmark case's display information. */
const runtimeOf = (c) => (c.displayInfo?.match(/Runtime=([^,)]+)/)?.[1] ?? "").trim();

const cases = [];
const sources = [];
let hostEnvironment = null;
for (const file of summaries) {
  const summary = JSON.parse(fs.readFileSync(file, "utf8"));
  hostEnvironment ??= summary.hostEnvironment;
  sources.push({ file: path.relative(repositoryRoot, path.resolve(file)), sha256: sha256(fs.readFileSync(file)), source: summary.source, revision: summary.revision, worktreeDirty: summary.worktreeDirty });
  for (const c of summary.benchmarks) {
    cases.push({
      type: c.type,
      method: c.method,
      parameters: c.parameters ?? "",
      runtime: runtimeOf(c),
      samples: c.samples,
      baselineMedianNanoseconds: c.medianNanoseconds,
      baselineMeanNanoseconds: c.meanNanoseconds,
      baselineP95Nanoseconds: c.percentile95Nanoseconds ?? null,
      baselineStandardDeviationNanoseconds: c.standardDeviationNanoseconds,
      relativeStandardDeviation: c.relativeStandardDeviation ?? (c.medianNanoseconds > 0 ? Number((c.standardDeviationNanoseconds / c.medianNanoseconds).toFixed(4)) : null),
      baselineAllocatedBytes: c.allocatedBytes,
      gen0Collections: c.gen0Collections ?? null,
    });
  }
}
cases.sort((a, b) => a.type.localeCompare(b.type) || a.method.localeCompare(b.method) || a.parameters.localeCompare(b.parameters) || a.runtime.localeCompare(b.runtime));

const fixtureManifest = path.join(repositoryRoot, "benchmarks/fixtures/manifest.json");
const contract = {
  schemaVersion: 1,
  budgetId: options["budget-id"],
  status: "baseline",
  description: "Baseline of the scenario benchmarks (Scenario category) on both packaged target frameworks, measured with the Short job. Consumed by tools/quality/compare-benchmark-baseline.mjs as a soft drift report; the benchmark drift workflow compares the Impact cases it contains.",
  date: new Date().toISOString().slice(0, 10),
  benchmark: {
    generator: "BenchmarkDotNet",
    generatorVersion: hostEnvironment?.BenchmarkDotNetVersion ?? "0.15.8",
    category: "Scenario",
    job: options.job,
    runtimes: [...new Set(cases.map((c) => c.runtime))],
    minimumSamples: Math.min(...cases.map((c) => c.samples)),
    timingMetric: "medianNanoseconds",
    maximumRelativeStandardDeviation: 0.35,
    softPolicy: { medianGrowthRatio: 0.10, allocationGrowthRatio: 0.05, minimumMedianNanoseconds: 200, minimumAllocationBytes: 256 },
    baselineEvidence: {
      summaries: sources,
      environment: `${os.type()} ${os.release()}; ${os.cpus()[0]?.model ?? "unknown CPU"} x${os.cpus().length}; ${os.arch()}; ${hostEnvironment?.RuntimeVersion ?? ""}; SDK ${run("dotnet", ["--version"])}`,
      dotnetInfoSha256: sha256(Buffer.from(run("dotnet", ["--info"]) ?? "")),
      revision: run("git", ["rev-parse", "HEAD"]),
      worktreeDirty: (run("git", ["status", "--porcelain"]) ?? "").length > 0,
      fixtureManifestSha256: fs.existsSync(fixtureManifest) ? sha256(fs.readFileSync(fixtureManifest)) : null,
    },
    cases,
  },
};
if (options.merge && fs.existsSync(output)) {
  // Partial re-baseline after an accepted experiment: overwrite the re-measured cases, keep every other case and
  // the original evidence, and append a dated note so the contract's history stays readable.
  const existing = JSON.parse(fs.readFileSync(output, "utf8"));
  /** Identifies a case by type, method, parameters and runtime. */
  const key = (c) => `${c.type}|${c.method}|${c.parameters}|${c.runtime}`;
  const replaced = new Map(cases.map((c) => [key(c), c]));
  const merged = existing.benchmark.cases.map((c) => replaced.get(key(c)) ?? c);
  // New cases are appended only for benchmark classes the contract already tracks (or with --allow-new), so a
  // filtered run that also caught rc1-category classes does not widen the contract by accident.
  const trackedTypes = new Set(existing.benchmark.cases.map((c) => c.type));
  const allowNew = options["allow-new"];
  let skipped = 0;
  for (const c of cases) {
    if (existing.benchmark.cases.some((e) => key(e) === key(c))) continue;
    if (allowNew || trackedTypes.has(c.type)) merged.push(c);
    else skipped++;
  }
  if (skipped > 0) console.log(`Skipped ${skipped} cases from classes the contract does not track (use --allow-new to add them).`);
  existing.benchmark.cases = merged;
  existing.benchmark.runtimes = [...new Set(merged.map((c) => c.runtime))];
  existing.benchmark.minimumSamples = Math.min(...merged.map((c) => c.samples));
  existing.benchmark.updates ??= [];
  existing.benchmark.updates.push({
    date: contract.date,
    note: options.note,
    replacedCases: cases.length,
    revision: contract.benchmark.baselineEvidence.revision,
    worktreeDirty: contract.benchmark.baselineEvidence.worktreeDirty,
    summaries: sources,
  });
  fs.writeFileSync(output, JSON.stringify(existing, null, 2) + "\n");
  console.log(`Merged ${cases.length} re-measured cases into ${output} (${merged.length} total)`);
} else {
  fs.writeFileSync(output, JSON.stringify(contract, null, 2) + "\n");
  console.log(`Recorded ${cases.length} cases (${contract.benchmark.runtimes.join(", ")}) to ${output}`);
}
