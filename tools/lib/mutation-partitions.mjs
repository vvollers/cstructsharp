/** Plans complete file-based mutation partitions and combines their reports without losing test identities. */
import assert from "node:assert/strict";
import crypto from "node:crypto";
import fs from "node:fs";
import path from "node:path";

/** Returns the SHA-256 identity of a file's bytes. */
export function mutationFileHash(filename) {
  return crypto.createHash("sha256").update(fs.readFileSync(filename)).digest("hex");
}

/**
 * The number of files in the permanent mutation scope. The tools and their tests check the configured allowlist against
 * it, so a file cannot leave the scope silently; a file split into partial files adds its parts here.
 */
export const PERMANENT_SCOPE_SIZE = 138;

/**
 * The number of file partitions the permanent scope runs in. The mutation workflow runs one job per partition, so the
 * scope's size divided by this count sets each job's running time; revisit it with the scope size.
 */
export const MUTATION_PARTITION_COUNT = 24;

/**
 * Maps an exact permanent-scope pattern to its repository source path.
 * @param {string} pattern A file below src/CStructSharp, or a CStructSharp.Core path with a leading `**` for a
 *   shared source.
 * @returns {string} The repository-relative `.cs` path.
 * @throws {assert.AssertionError} When the pattern is a glob, climbs with `..`, or does not name a `.cs` file.
 */
export function mutationSource(pattern) {
  const source = pattern.startsWith("**/CStructSharp.Core/") ? `src/${pattern.slice(3)}` : `src/CStructSharp/${pattern}`;
  assert.ok(!/[*!?{}]/.test(source) && !source.includes("..") && source.endsWith(".cs"), `Not an exact semantic file: ${pattern}`);
  return source;
}

/**
 * Builds a project-context invocation using the configured source/test projects and absolute evidence paths.
 * Running from the solution directory makes Stryker discover extra test projects despite test-projects settings.
 */
export function mutationInvocation(root, configuration, output, patterns = []) {
  const config = JSON.parse(fs.readFileSync(configuration, "utf8"))["stryker-config"];
  assert.equal(config.project, "src/CStructSharp/CStructSharp.csproj");
  assert.deepEqual(config["test-projects"], ["tests/CStructSharpTests/CStructSharpTests.csproj"]);
  const project = path.resolve(root, config.project);
  // No --solution: Stryker builds the test project and its references directly, so the WASM project in the
  // solution (and its workload) is never needed.
  const args = ["stryker", "--config-file", path.resolve(configuration),
    "--project", path.basename(project), "--test-project", path.resolve(root, config["test-projects"][0]),
    "--target-framework", "net10.0", "--configuration", "Release", "--output", path.resolve(output),
    "--skip-version-check", "--log-to-file"];
  for (const pattern of patterns) args.push("--mutate", pattern);
  return { cwd: path.dirname(project), args };
}

/** Rejects reports that ran tests from projects outside the configured core test project. */
export function requireCoreMutationTests(report) {
  let count = 0;
  for (const [filename, file] of Object.entries(report.testFiles ?? {})) {
    if (!(file.tests?.length > 0)) continue;
    const normalized = `/${path.posix.normalize(filename.replaceAll("\\", "/"))}`.toLowerCase();
    assert.ok(normalized.includes("/cstructsharptests/"), `Mutation report includes tests outside the configured core project: ${filename}`);
    count += file.tests.length;
  }
  assert.ok(count > 0, "Mutation report contains no configured core tests");
}

/**
 * Partitions every configured file once. The largest source runs alone in p00, the partition with the extended time
 * budget; the others are assigned, larger sources first, to the currently smallest of the remaining groups. Source
 * length is a scheduling estimate, not a claim about mutation count or duration. Files are never split.
 */
export function planMutationPartitions(root, config, count = MUTATION_PARTITION_COUNT) {
  assert.ok(Number.isInteger(count) && count > 0);
  const patterns = config.mutate;
  assert.ok(Array.isArray(patterns) && patterns.length >= count);
  assert.equal(new Set(patterns).size, patterns.length, "Duplicate mutation scope entries");
  // Resolve every source before scheduling, so stale config paths cannot become silently empty partitions.
  const files = patterns.map((pattern) => {
    const source = mutationSource(pattern);
    return { pattern, source, bytes: fs.statSync(path.join(root, source)).size };
  });
  files.sort((left, right) => right.bytes - left.bytes || left.source.localeCompare(right.source, "en"));
  // Stable identifiers make each uploaded report's owning partition unambiguous within one source revision.
  const partitions = Array.from({ length: count }, (_, index) => ({ id: `p${String(index).padStart(2, "0")}`, files: [], bytes: 0 }));
  for (const [index, file] of files.entries()) {
    // With one partition everything shares it; otherwise p00 takes only the first, largest file.
    const candidates = count === 1 ? partitions : index === 0 ? [partitions[0]] : partitions.slice(1);
    const smallest = candidates.reduce((best, candidate) => candidate.bytes < best.bytes ? candidate : best);
    smallest.files.push(file);
    smallest.bytes += file.bytes;
  }
  assert.equal(new Set(partitions.flatMap((partition) => partition.files.map((file) => file.source))).size, patterns.length);
  return partitions;
}

/** Matches a report source path regardless of Stryker's absolute checkout prefix or path separator. */
export function matchesMutationSource(reportPath, source) {
  const normalized = reportPath.replaceAll("\\", "/").toLowerCase();
  const suffix = source.slice(4).toLowerCase();
  return normalized === suffix || normalized.endsWith(`/${suffix}`);
}

/** Requires terminal mutation statuses, retaining compile errors and ignored mutations as undetected limitations. */
export function requireCompletedMutants(mutants) {
  const terminal = new Set(["Killed", "Timeout", "Survived", "NoCoverage", "RuntimeError", "CompileError", "Ignored"]);
  for (const mutant of mutants) assert.ok(terminal.has(mutant.status), `Incomplete or unknown mutation status: ${mutant.status}`);
}

/**
 * Combines exactly one checked report per partition. Mutant and test IDs are namespaced together, preserving
 * killedBy/coveredBy evidence. Copies of unselected files may contain only ignored or compile-error mutants.
 */
export function aggregateMutationPartitions(root, partitions, inputs) {
  assert.equal(inputs.length, partitions.length, "Missing or extra mutation partition reports");
  assert.equal(new Set(inputs.map((input) => input.id)).size, inputs.length, "Duplicate mutation partition reports");
  const combined = { schemaVersion: "2", thresholds: { high: 75, low: 75 }, projectRoot: root, files: {}, testFiles: {} };
  for (const partition of partitions) {
    const input = inputs.find((candidate) => candidate.id === partition.id);
    assert.ok(input, `Missing partition ${partition.id}`);
    const report = input.report;
    assert.equal(String(report.schemaVersion), "2");
    assert.deepEqual(report.thresholds, combined.thresholds, `Wrong thresholds in ${partition.id}`);
    const testIds = new Set();
    for (const [filename, value] of Object.entries(report.testFiles ?? {})) {
      // Each test remains attributable to the original partition and source file after aggregation.
      const tests = (value.tests ?? []).map((entry) => {
        const id = String(entry.id);
        assert.ok(!testIds.has(id), `Duplicate test ID ${id} in ${partition.id}`);
        testIds.add(id);
        return { ...entry, id: `${partition.id}:${id}` };
      });
      combined.testFiles[`${partition.id}/${filename}`] = { ...value, tests };
    }
    assert.ok(testIds.size > 0, `No tests in ${partition.id}`);
    const seen = new Set();
    const mutantIds = new Set();
    for (const [filename, value] of Object.entries(report.files ?? {})) {
      const selected = partition.files.filter((file) => matchesMutationSource(filename, file.source));
      const mutants = value.mutants ?? [];
      if (selected.length === 0) {
        // Stryker can retain filtered source in its report. Never accept tested mutations outside this partition.
        assert.ok(mutants.every((mutant) => ["Ignored", "CompileError"].includes(mutant.status)), `Tested file outside ${partition.id}: ${filename}`);
        continue;
      }
      assert.equal(selected.length, 1, `Ambiguous source ${filename}`);
      const source = selected[0].source;
      assert.ok(!seen.has(source) && !Object.hasOwn(combined.files, source), `Duplicate source ${source}`);
      seen.add(source);
      assert.equal(value.source.replaceAll("\r\n", "\n"), fs.readFileSync(path.join(root, source), "utf8").replaceAll("\r\n", "\n"), `Report source changed: ${source}`);
      requireCompletedMutants(mutants);
      // Namespacing both ends of each relationship avoids test-ID collisions between independent Stryker runs.
      const merged = mutants.map((mutant) => {
        const id = String(mutant.id);
        assert.ok(!mutantIds.has(id), `Duplicate mutant ID ${id} in ${partition.id}`);
        mutantIds.add(id);
        const result = { ...mutant, id: `${partition.id}:${id}` };
        for (const relation of ["killedBy", "coveredBy"]) {
          if (mutant[relation] === undefined) continue;
          // Every referenced test must exist; a score without its test attribution is insufficient evidence.
          result[relation] = mutant[relation].map((testId) => {
            assert.ok(typeof testId === "string" || typeof testId === "number", `Unsupported test reference in ${partition.id}`);
            assert.ok(testIds.has(String(testId)), `Unknown test ${testId} in ${partition.id}`);
            return `${partition.id}:${testId}`;
          });
        }
        return result;
      });
      combined.files[source] = { ...value, mutants: merged };
    }
    assert.equal(seen.size, partition.files.length, `Missing configured files in ${partition.id}`);
  }
  return combined;
}
