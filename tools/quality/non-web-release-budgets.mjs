#!/usr/bin/env node
/**
 * Enforces the non-web release budgets (contracts/performance/non-web-rc1.json): the BenchmarkDotNet release-gate
 * summary against its noise-aware timing and allocation budgets, and the NuGet package pair against its size
 * budgets. `--self-test` checks the policy and the gate logic with synthetic inputs.
 *
 *   node tools/quality/non-web-release-budgets.mjs [--policy-path <json>] [--self-test]
 *     [--benchmark-summary-path <json>] [--package-artifact-path <json>]
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot } from "../lib/tooling.mjs";

const options = parseArguments(
  process.argv.slice(2),
  { "policy-path": "string", "benchmark-summary-path": "string", "package-artifact-path": "string", "self-test": "flag" },
  { defaults: { "policy-path": path.join(repositoryRoot, "contracts/performance/non-web-rc1.json"), "self-test": false } },
);
const isFile = (file) => fs.existsSync(file) && fs.statSync(file).isFile();
const caseKey = (benchmark) => `${benchmark.type ?? ""}|${benchmark.method ?? ""}|${benchmark.parameters ?? ""}`;
const clone = (value) => JSON.parse(JSON.stringify(value));

function timingBudget(expected, policy) {
  return Math.max(Number(expected.baselineMedianNanoseconds) * Number(policy.maximumMedianMultiplier), Number(policy.minimumMedianBudgetNanoseconds));
}

function allocationBudget(expected, policy) {
  const baseline = Number(expected.baselineAllocatedBytes);
  return Math.max(baseline * (1 + Number(policy.maximumAllocationGrowthRatio)), baseline + Number(policy.minimumAllocationHeadroomBytes));
}

function benchmarkFailures(summary, policy) {
  const failures = [];
  if (summary.schemaVersion !== 1) {
    failures.push("Benchmark summary has an unsupported schema version.");
    return failures;
  }
  const runtimeVersion = String(summary.hostEnvironment?.RuntimeVersion ?? "");
  if (!runtimeVersion.startsWith(String(policy.runtimePrefix))) {
    failures.push(`Benchmark runtime '${runtimeVersion}' does not match '${policy.runtimePrefix}'.`);
  }
  const currentByKey = new Map();
  for (const current of summary.benchmarks ?? []) {
    const key = caseKey(current);
    if (currentByKey.has(key)) failures.push(`Benchmark summary contains duplicate case '${key}'.`);
    else currentByKey.set(key, current);
  }
  const policyKeys = new Set();
  for (const expected of policy.cases ?? []) {
    const key = caseKey(expected);
    policyKeys.add(key);
    const current = currentByKey.get(key);
    if (!current) {
      failures.push(`Benchmark summary is missing required case '${key}'.`);
      continue;
    }
    const minimumSamples = Number(policy.minimumSamples);
    if (Number(current.samples) < minimumSamples) {
      failures.push(`Benchmark '${key}' has ${current.samples} samples; at least ${minimumSamples} are required.`);
    }
    const median = Number(current.medianNanoseconds);
    const standardDeviation = Number(current.standardDeviationNanoseconds);
    const budget = timingBudget(expected, policy);
    if (median > budget) failures.push(`Benchmark '${key}' median ${median} ns exceeds the noise-aware ${budget} ns budget.`);
    if (median <= 0 || standardDeviation / median > Number(policy.maximumRelativeStandardDeviation)) {
      failures.push(`Benchmark '${key}' is too unstable for gating: median ${median} ns, standard deviation ${standardDeviation} ns.`);
    }
    const allocated = Number(current.allocatedBytes);
    const allocation = allocationBudget(expected, policy);
    if (allocated > allocation) failures.push(`Benchmark '${key}' allocation ${allocated} bytes exceeds the ${allocation}-byte budget.`);
  }
  for (const key of currentByKey.keys()) {
    if (!policyKeys.has(key)) failures.push(`Benchmark summary contains unreviewed release-gate case '${key}'.`);
  }
  return failures;
}

function packageFailures(artifact, policy) {
  const failures = [];
  if (artifact.schemaVersion !== 1) {
    failures.push("Package artifact report has an unsupported schema version.");
    return failures;
  }
  const packages = artifact.packages;
  if (!packages) {
    failures.push("Artifact report has no package measurement.");
    return failures;
  }
  if (Number(packages.files) !== 2) failures.push(`Package artifact report contains ${packages.files} files; exactly two are required.`);
  if (Number(packages.bytes) > Number(policy.maximumBytes)) failures.push(`Package pair is ${packages.bytes} bytes; budget is ${policy.maximumBytes}.`);
  if (Number(packages.gzipBytes) > Number(policy.maximumGzipBytes)) failures.push(`Compressed package pair is ${packages.gzipBytes} bytes; budget is ${policy.maximumGzipBytes}.`);
  const extensions = packages.byExtension ?? [];
  for (const expected of policy.extensions ?? []) {
    const actual = extensions.filter((entry) => entry.extension === expected.extension);
    if (actual.length !== 1) {
      failures.push(`Package artifact report must contain one '${expected.extension}' summary.`);
      continue;
    }
    if (Number(actual[0].files) !== 1) failures.push(`Package artifact '${expected.extension}' count must be one.`);
    if (Number(actual[0].bytes) > Number(expected.maximumBytes)) failures.push(`${expected.extension} is ${actual[0].bytes} bytes; budget is ${expected.maximumBytes}.`);
    if (Number(actual[0].gzipBytes) > Number(expected.maximumGzipBytes)) {
      failures.push(`Compressed ${expected.extension} is ${actual[0].gzipBytes} bytes; budget is ${expected.maximumGzipBytes}.`);
    }
  }
  return failures;
}

function assertNoFailures(failures, context) {
  if (failures.length > 0) throw new Error(`${context} failed.\n${failures.join("\n")}`);
}

const percent = (value) => `${(value * 100).toFixed(2)} %`;

await main(() => {
  assertCondition(isFile(options["policy-path"]), `Non-Web release budget policy '${options["policy-path"]}' does not exist.`);
  const policy = JSON.parse(fs.readFileSync(options["policy-path"], "utf8"));
  assertCondition(policy.schemaVersion === 1, "Unsupported non-Web release budget schema.");
  assertCondition(policy.budgetId === "non-web-rc1", "Unexpected non-Web release budget id.");
  assertCondition(policy.status === "enforced", "Non-Web release budgets are not enforced.");
  assertCondition(policy.workItem === "QA-08", "Non-Web release budgets are not assigned to QA-08.");
  const benchmark = policy.benchmark;
  assertCondition(benchmark.generator === "BenchmarkDotNet", "The benchmark policy names an unexpected generator.");
  assertCondition(benchmark.generatorVersion === "0.15.8", "The benchmark policy must pin BenchmarkDotNet 0.15.8.");
  assertCondition(benchmark.targetFramework === "net10.0", "The benchmark policy must target net10.0.");
  assertCondition(
    benchmark.job.launchCount === 3 && benchmark.job.warmupCount === 5 && benchmark.job.iterationCount === 8,
    "The benchmark release-gate job must retain 3 launches, 5 warmups, and 8 measured iterations.",
  );
  assertCondition(benchmark.minimumSamples >= 16, "The benchmark release gate must require at least 16 retained samples.");
  assertCondition(benchmark.maximumMedianMultiplier >= 1, "The benchmark median multiplier is invalid.");
  assertCondition(benchmark.maximumRelativeStandardDeviation > 0 && benchmark.maximumRelativeStandardDeviation <= 0.5, "The benchmark dispersion limit must be in (0, 0.5].");
  const policyCases = benchmark.cases ?? [];
  assertCondition(policyCases.length === 16, "The non-Web benchmark gate must contain exactly 16 cases (14 runtime, 2 generated).");
  const keys = policyCases.map(caseKey);
  assertCondition(new Set(keys).size === keys.length, "The non-Web benchmark policy contains duplicate cases.");
  for (const entry of policyCases) {
    assertCondition(Number(entry.baselineMedianNanoseconds) > 0, `Benchmark policy case '${caseKey(entry)}' has no positive median.`);
    assertCondition(Number(entry.baselineAllocatedBytes) >= 0, `Benchmark policy case '${caseKey(entry)}' has invalid allocation.`);
  }
  const pkg = policy.package;
  assertCondition(pkg.baseline.files === 2, "The package baseline must contain exactly two files.");
  assertCondition(pkg.maximumBytes > pkg.baseline.bytes && pkg.maximumGzipBytes > pkg.baseline.gzipBytes, "The package pair budgets must exceed the frozen QA-07 baseline.");
  const packageExtensions = pkg.extensions ?? [];
  assertCondition(packageExtensions.length === 2, "The package policy must contain exactly .nupkg and .snupkg budgets.");
  assertCondition(packageExtensions.map((entry) => entry.extension).sort().join(",") === ".nupkg,.snupkg", "The package policy must contain exactly .nupkg and .snupkg budgets.");
  assertCondition(typeof policy.webIntegration === "string" && policy.webIntegration.trim().length > 0, "The non-Web release budget policy has no final Web/WASM boundary.");

  if (options["self-test"]) {
    const synthetic = {
      schemaVersion: 1,
      hostEnvironment: { RuntimeVersion: `${benchmark.runtimePrefix}.self-test` },
      benchmarks: policyCases.map((entry) => ({
        type: entry.type,
        method: entry.method,
        parameters: entry.parameters,
        samples: benchmark.minimumSamples,
        medianNanoseconds: entry.baselineMedianNanoseconds,
        standardDeviationNanoseconds: 0,
        allocatedBytes: entry.baselineAllocatedBytes,
      })),
    };
    assertNoFailures(benchmarkFailures(synthetic, benchmark), "Benchmark self-test baseline");
    const timingRegression = clone(synthetic);
    timingRegression.benchmarks[0].medianNanoseconds = 1e15;
    assertCondition(benchmarkFailures(timingRegression, benchmark).length > 0, "Benchmark self-test did not reject a timing regression.");
    const allocationRegression = clone(synthetic);
    allocationRegression.benchmarks[0].allocatedBytes = 1e15;
    assertCondition(benchmarkFailures(allocationRegression, benchmark).length > 0, "Benchmark self-test did not reject an allocation regression.");
    const missingCase = clone(synthetic);
    missingCase.benchmarks = missingCase.benchmarks.slice(1);
    assertCondition(benchmarkFailures(missingCase, benchmark).length > 0, "Benchmark self-test did not reject a missing case.");

    const syntheticPackage = {
      schemaVersion: 1,
      packages: {
        files: 2,
        bytes: pkg.baseline.bytes,
        gzipBytes: pkg.baseline.gzipBytes,
        byExtension: packageExtensions.map((entry) => ({ extension: entry.extension, files: 1, bytes: entry.baselineBytes, gzipBytes: entry.baselineGzipBytes })),
      },
    };
    assertNoFailures(packageFailures(syntheticPackage, pkg), "Package self-test baseline");
    const packageRegression = clone(syntheticPackage);
    packageRegression.packages.bytes = 1e15;
    assertCondition(packageFailures(packageRegression, pkg).length > 0, "Package self-test did not reject an artifact regression.");
    console.log("Non-Web release budget self-test passed.");
    console.log("Negative checks: timing, allocation, missing case, package size.");
  }

  if (options["benchmark-summary-path"]) {
    assertCondition(isFile(options["benchmark-summary-path"]), `Benchmark summary '${options["benchmark-summary-path"]}' does not exist.`);
    const summary = JSON.parse(fs.readFileSync(options["benchmark-summary-path"], "utf8"));
    assertNoFailures(benchmarkFailures(summary, benchmark), "Non-Web benchmark release gate");
    let timingUse = 0;
    let allocationUse = 0;
    for (const entry of summary.benchmarks ?? []) {
      const expected = policyCases.find((candidate) => caseKey(candidate) === caseKey(entry));
      timingUse = Math.max(timingUse, Number(entry.medianNanoseconds) / timingBudget(expected, benchmark));
      allocationUse = Math.max(allocationUse, Number(entry.allocatedBytes) / allocationBudget(expected, benchmark));
    }
    console.log("Non-Web benchmark release gate passed.");
    console.log(`Cases: ${(summary.benchmarks ?? []).length}`);
    console.log(`Minimum samples: ${benchmark.minimumSamples}`);
    console.log(`Maximum timing-budget use: ${percent(timingUse)}`);
    console.log(`Maximum allocation-budget use: ${percent(allocationUse)}`);
  }

  if (options["package-artifact-path"]) {
    assertCondition(isFile(options["package-artifact-path"]), `Package artifact report '${options["package-artifact-path"]}' does not exist.`);
    const artifact = JSON.parse(fs.readFileSync(options["package-artifact-path"], "utf8"));
    assertNoFailures(packageFailures(artifact, pkg), "Non-Web package artifact gate");
    console.log("Non-Web package artifact gate passed.");
    console.log(`Files: ${artifact.packages.files}`);
    console.log(`Raw bytes: ${artifact.packages.bytes} / ${pkg.maximumBytes}`);
    console.log(`Gzip-equivalent bytes: ${artifact.packages.gzipBytes} / ${pkg.maximumGzipBytes}`);
  }

  if (!options["self-test"] && !options["benchmark-summary-path"] && !options["package-artifact-path"]) {
    throw new Error("Specify --self-test, --benchmark-summary-path, or --package-artifact-path.");
  }
});
