/**
 * Shared access to the committed serializer-comparison summary (benchmarks/CStructSharp.Comparison/results.json):
 * its path, its number formats, and a case lookup. tools/quality/comparison-benchmarks.mjs writes the summary and
 * renders the README tables from it; tools/documentation/sync-documentation-facts.mjs quotes single cases in guide
 * prose with the same formats, so a number reads the same in the table and in the text.
 */
import fs from "node:fs";
import path from "node:path";
import { assertCondition, repositoryRoot } from "./tooling.mjs";

/** The committed summary, relative to the repository root. */
export const COMPARISON_RESULTS_PATH = "benchmarks/CStructSharp.Comparison/results.json";

/** Formats a median in nanoseconds: one decimal below 100 ns, whole nanoseconds below 10 µs, then µs, then ms. */
export function formatTime(nanoseconds) {
  if (nanoseconds < 100) return `${nanoseconds.toFixed(1)} ns`;
  if (nanoseconds < 10_000) return `${Math.round(nanoseconds).toLocaleString("en-US")} ns`;
  if (nanoseconds < 10_000_000) return `${(nanoseconds / 1000).toFixed(1)} µs`;
  return `${(nanoseconds / 1_000_000).toFixed(1)} ms`;
}

/** Formats allocated bytes per operation as whole bytes. */
export function formatBytes(bytes) {
  return `${Math.round(bytes).toLocaleString("en-US")} B`;
}

/**
 * Reads the committed comparison summary.
 * @param {string} [root] The repository root to read from.
 * @returns {object} The summary: `environment`, `sizes`, and `cases` keyed `Type.Method`.
 */
export function readComparisonSummary(root = repositoryRoot) {
  return JSON.parse(fs.readFileSync(path.join(root, COMPARISON_RESULTS_PATH), "utf8"));
}

/**
 * Looks up one measured case of a comparison summary.
 * @param {object} summary The summary.
 * @param {string} key The case, as `Type.Method`.
 * @returns {{ medianNanoseconds: number, allocatedBytes: number }} The measurement.
 * @throws {Error} When the summary has no such case.
 */
export function comparisonCase(summary, key) {
  const measured = summary.cases?.[key];
  assertCondition(measured, `${COMPARISON_RESULTS_PATH} has no result for ${key}; measure again.`);
  return measured;
}
