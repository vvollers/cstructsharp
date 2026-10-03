/** Reads complete BDN measurements and reports provisional signals. Used by tools/quality/perf-check.mjs. */
import fs from "node:fs";
import path from "node:path";

/** Computes a sample median while retaining the original measurement order. */
export function median(values) {
  // Reject corrupt samples before ordering or aggregating them.
  if (!values.length || values.some((value) => !Number.isFinite(value)))
    throw new Error("Missing or non-finite measurements.");
  // Sort a copy: chronological samples are retained in the run's raw report.
  const sorted = [...values].sort((a, b) => a - b);
  return (sorted[Math.floor(sorted.length / 2)] + sorted[Math.floor((sorted.length - 1) / 2)]) / 2;
}

/** Computes the observed 10th-to-90th percentile span divided by the median; this is spread, not a confidence interval. */
export function spread(values) {
  // Nearest-rank percentiles avoid inventing new observations in these short samples.
  const sorted = [...values].sort((a, b) => a - b);
  return (
    (sorted[Math.ceil(sorted.length * 0.9) - 1] - sorted[Math.ceil(sorted.length * 0.1) - 1]) /
    median(values)
  );
}

/** Extracts every actual workload sample and rejects incomplete timing, memory diagnostics or duplicate identities. */
export function readRun(directory, expectedIterations) {
  const cases = [];
  const ids = new Set();
  for (const file of fs.readdirSync(path.join(directory, "results"))) {
    if (!file.endsWith("report-full.json")) continue;
    const report = JSON.parse(fs.readFileSync(path.join(directory, "results", file), "utf8"));
    for (const c of report.Benchmarks ?? []) {
      const id = c.FullName;
      if (!id || ids.has(id)) throw new Error(`Missing or duplicate benchmark identity: ${id}`);
      ids.add(id);
      // Use all actual samples, including observations excluded by BDN's default summary outlier policy.
      const measurements = (c.Measurements ?? []).filter(
        (m) => m.IterationMode === "Workload" && m.IterationStage === "Actual",
      );
      // A completed run needs the expected number of finite, positive workload observations.
      if (
        !c.Statistics ||
        measurements.length !== expectedIterations ||
        measurements.some(
          (m) =>
            !Number.isFinite(m.Operations) ||
            !Number.isFinite(m.Nanoseconds) ||
            !(m.Operations > 0) ||
            !(m.Nanoseconds > 0),
        )
      )
        throw new Error(`Incomplete timing: ${id}`);
      const allocated = c.Memory?.BytesAllocatedPerOperation;
      if (!Number.isFinite(allocated) || allocated < 0)
        throw new Error(`Missing allocation diagnostics: ${id}`);
      // Normalize duration by the actual operation count reported by BDN, never by a guessed invocation count.
      const samples = measurements.map((m) => m.Nanoseconds / m.Operations);
      cases.push({
        id,
        type: c.Type,
        method: c.Method,
        parameters: c.Parameters,
        median: median(samples),
        samples,
        allocated,
        spread: spread(samples),
      });
    }
  }
  if (!cases.length) throw new Error(`No benchmark results: ${directory}`);
  // Stable ordering makes exact coverage comparisons independent of execution order.
  cases.sort((a, b) => a.id.localeCompare(b.id));
  return {
    directory,
    environment: JSON.parse(
      fs.readFileSync(path.join(directory, "development-environment.json"), "utf8"),
    ),
    cases,
  };
}

/** Validates coverage and runtime controls, then describes direction and instability across fresh launches. */
export function compareRuns(before, after, threshold, confirmation) {
  if (!before.length || before.length !== after.length)
    throw new Error("Both sides need the same nonzero launch count.");
  // Exact case identities catch missing parameters as well as missing methods.
  const ids = JSON.stringify(before[0].cases.map((c) => c.id));
  const environment = JSON.stringify(before[0].environment);
  for (const run of [...before, ...after]) {
    // Compare full identities, including parameter text, for each launch.
    if (JSON.stringify(run.cases.map((c) => c.id)) !== ids)
      throw new Error("Benchmark coverage differs between launches or revisions.");
    if (JSON.stringify(run.environment) !== environment)
      throw new Error("Runtime or affinity differs between launches or revisions.");
  }
  // Pair launch medians, not individual samples from independent hosts, and retain every launch.
  const rows = before[0].cases.map((c, index) => {
    // Align this case across baseline launches.
    const b = before.map((run) => run.cases[index]);
    // Align the same case across candidate launches.
    const a = after.map((run) => run.cases[index]);
    // Each ratio compares one fresh baseline/candidate pair.
    const deltas = b.map((item, round) => a[round].median / item.median - 1);
    const delta = median(deltas);
    // Keep baseline launch medians for reporting and between-launch spread.
    const beforeMedians = b.map((item) => item.median);
    // Keep candidate launch medians independently of the paired ratio estimate.
    const afterMedians = a.map((item) => item.median);
    // Combine within-launch spread with the variation between launches.
    const variation = Math.max(
      ...[...a, ...b].map((item) => item.spread),
      spread(beforeMedians),
      spread(afterMedians),
    );
    // A descriptive repeatable signal must clear the margin in the same direction in every pair.
    const sameDirection = deltas.every(
      (value) => Math.sign(value) === Math.sign(delta) && Math.abs(value) > threshold,
    );
    const unstable =
      variation > Math.max(0.06, threshold * 2) ||
      (confirmation && !sameDirection && Math.abs(delta) > threshold);
    let timing = "inconclusive (within margin)";
    if (unstable) timing = "inconclusive (unstable)";
    else if (Math.abs(delta) > threshold)
      timing = `${confirmation && sameDirection ? "repeatable" : "possible"} ${delta > 0 ? "slowdown" : "speedup"}; confirm`;
    // Retain the byte difference for every pair, even when its timing is inconclusive.
    const allocationDeltas = b.map((item, round) => a[round].allocated - item.allocated);
    return {
      id: c.id,
      type: c.type,
      method: c.method,
      before: median(beforeMedians),
      after: median(afterMedians),
      delta,
      deltas,
      beforeMedians,
      afterMedians,
      variation,
      timing,
      // Preserve baseline memory diagnostics per launch.
      allocatedBefore: b.map((item) => item.allocated),
      // Preserve candidate memory diagnostics per launch.
      allocatedAfter: a.map((item) => item.allocated),
      allocationDeltas,
      // Any observed allocation change is visible without a timing threshold.
      allocationSignal: allocationDeltas.some((value) => value !== 0),
    };
  });
  // A changing hand-written control is evidence that measurement conditions changed; do not subtract it from results.
  const canary = rows.find((row) => row.method === "HandWritten_PrimRecord");
  const canaryUnstable =
    canary &&
    (Math.abs(canary.delta) > threshold || canary.variation > Math.max(0.06, threshold * 2));
  if (canaryUnstable) for (const row of rows) row.timing = "inconclusive (canary drift)";
  return {
    rows,
    canary: canary ? (canaryUnstable ? "drift" : "within screening margin") : "not selected",
  };
}

/** Formats provisional signals, observed launch ranges and separate allocation deltas; no pass/fail performance gate is emitted. */
export function renderReport(report) {
  const lines = [
    "# Development performance comparison",
    "",
    `**${report.mode}: provisional measurements, not a regression verdict.** ${report.rows.length} cases; ${report.rounds} fresh launch(es) per side; CPU ${report.environment.selectedCpu ?? "unrestricted"}.`,
    `Canary: ${report.canary}. Practical timing margin: ±${(report.threshold * 100).toFixed(1)}%. Spread and launch ranges below are observations, not confidence intervals.`,
    "Δ time is the median of paired launch ratios; before/after columns summarize each side independently. These estimates can differ when launches drift.",
    "",
    "| Case | Before ns/op | After ns/op | Δ time | Observed launch Δ range | Spread | Bytes/op before → after | Timing signal |",
    "| --- | ---: | ---: | ---: | ---: | ---: | --- | --- |",
  ];
  for (const row of report.rows) {
    const range = `${(Math.min(...row.deltas) * 100).toFixed(1)}…${(Math.max(...row.deltas) * 100).toFixed(1)}%`;
    lines.push(
      `| \`${row.id.replaceAll("|", "\\|")}\` | ${row.before.toFixed(2)} | ${row.after.toFixed(2)} | ${(row.delta * 100).toFixed(1)}% | ${range} | ${(row.variation * 100).toFixed(1)}% | ${row.allocatedBefore.join(",")} → ${row.allocatedAfter.join(",")}${row.allocationSignal ? " (changed; confirm)" : ""} | ${row.timing} |`,
    );
  }
  lines.push(
    "",
    "No signal does not demonstrate equivalence. A possible change, allocation change, canary drift or instability needs targeted confirmation in fresh launches. Repeatable direction still does not establish suite-adjusted statistical confidence.",
    `Setup/build: ${report.setupSeconds.toFixed(2)} s; measurement/report so far: ${report.measureSeconds.toFixed(2)} s. Total invocation time is printed after report files are written.`,
    "",
    "Raw BDN samples, immutable bundle hashes, runtime controls and launch order are retained beside this report.",
  );
  return lines.join("\n") + "\n";
}
