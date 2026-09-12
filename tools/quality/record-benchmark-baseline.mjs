#!/usr/bin/env node
// Records a Phase 0 baseline contract (contracts/performance/non-web-rc2.json) from one or more normalized
// benchmark summaries (convert-benchmark-baseline.mjs output). Cases are keyed by type|method|parameters|runtime so
// net8.0 and net10.0 are recorded separately. Environment capture (dotnet --info, CPU, OS, git revision, fixture
// manifest hash) is embedded as baselineEvidence, matching the non-web-rc1 fields.
//
// Usage: node tools/quality/record-benchmark-baseline.mjs --output contracts/performance/non-web-rc2.json
//        --summary <summary.json> [--summary <more.json>] [--budget-id non-web-rc2] [--job Short]
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import crypto from "node:crypto";
import { execFileSync } from "node:child_process";
import { fileURLToPath } from "node:url";

const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const args = process.argv.slice(2);
const values = (name) => args.flatMap((a, i) => (a === name ? [args[i + 1]] : []));
const option = (name, fallback) => values(name)[0] ?? fallback;
const summaries = values("--summary");
const output = option("--output");
if (!output || summaries.length === 0) {
  console.error("Usage: record-benchmark-baseline.mjs --output <contract.json> --summary <summary.json> [...]");
  process.exit(2);
}
const run = (cmd, a) => { try { return execFileSync(cmd, a, { encoding: "utf8", stdio: ["ignore", "pipe", "ignore"] }).trim(); } catch { return null; } };
const sha256 = (buffer) => crypto.createHash("sha256").update(buffer).digest("hex").toUpperCase();
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
  budgetId: option("--budget-id", "non-web-rc2"),
  status: "baseline",
  description: "Phase 0 baseline of the scenario-matrix benchmarks (Baseline0 category) on both packaged target frameworks. Consumed by tools/quality/compare-benchmark-baseline.mjs as a soft drift report, not by the enforced rc1 release gate.",
  date: new Date().toISOString().slice(0, 10),
  benchmark: {
    generator: "BenchmarkDotNet",
    generatorVersion: hostEnvironment?.BenchmarkDotNetVersion ?? "0.15.8",
    category: "Baseline0",
    job: option("--job", "Short"),
    runtimes: [...new Set(cases.map((c) => c.runtime))],
    minimumSamples: Math.min(...cases.map((c) => c.samples)),
    timingMetric: "medianNanoseconds",
    maximumRelativeStandardDeviation: 0.35,
    softPolicy: { medianGrowthRatio: 0.10, allocationGrowthRatio: 0.05, minimumMedianNanoseconds: 200, minimumAllocationBytes: 256 },
    baselineEvidence: {
      summaries: sources,
      environment: `${os.type()} ${os.release()}; ${os.cpus()[0]?.model ?? "unknown CPU"} x${os.cpus().length}; ${os.arch()}; ${hostEnvironment?.RuntimeVersion ?? ""}; SDK ${run("dotnet", ["--version"])}`,
      dotnetInfoSha256: sha256(Buffer.from(run("dotnet", ["--info"]) ?? "")),
      revision: run("git", ["-C", repositoryRoot, "rev-parse", "HEAD"]),
      worktreeDirty: (run("git", ["-C", repositoryRoot, "status", "--porcelain"]) ?? "").length > 0,
      fixtureManifestSha256: fs.existsSync(fixtureManifest) ? sha256(fs.readFileSync(fixtureManifest)) : null,
    },
    cases,
  },
};
fs.writeFileSync(output, JSON.stringify(contract, null, 2) + "\n");
console.log(`Recorded ${cases.length} cases (${contract.benchmark.runtimes.join(", ")}) to ${output}`);
