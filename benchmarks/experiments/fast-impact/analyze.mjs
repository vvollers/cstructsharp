#!/usr/bin/env node
// Summarizes saved experiments without running workloads. Usage: node benchmarks/experiments/fast-impact/analyze.mjs
// Output stays under artifacts/perf/fast-impact; no published performance baseline is updated.
import fs from 'node:fs';
import path from 'node:path';
const root = 'artifacts/perf/fast-impact';
/** Reads an existing JSON file; absent experiments are omitted. */
function read(file) { return fs.existsSync(file) ? JSON.parse(fs.readFileSync(file, 'utf8')) : null; }
/** Returns the middle-pair median at 0.5, or an empirical quantile using the nearest observed rank otherwise. */
function quantile(values, q) {
  // Numeric sorting preserves the original sample order in the saved raw results.
  const a = [...values].sort((x, y) => x - y);
  return q === 0.5 ? (a[Math.floor(a.length / 2)] + a[Math.floor((a.length - 1) / 2)]) / 2 : a[Math.max(0, Math.ceil(a.length * q) - 1)];
}
/** Computes a Wilson 95% interval for a binomial count; the suite run is the trial for familywise errors. */
function wilson(k, n) { const z2 = 3.841458820694124, p = k / n, d = 1 + z2 / n; const center = (p + z2 / (2 * n)) / d; const half = Math.sqrt(z2 * (p * (1 - p) / n + z2 / (4 * n * n))) / d; return [center - half, center + half]; }
/** Sums all numeric values. */
function sum(a) {
  // Empty experiment groups contribute zero to aggregate counts.
  return a.reduce((x, y) => x + y, 0);
}
/** Extracts all BenchmarkDotNet report cases in a run directory. */
function bdn(label) {
  const dir = path.join(root, label, 'results');
  if (!fs.existsSync(dir)) return [];
  // A BDN host exports one full report per class; include every class, excluding brief exporters.
  return fs.readdirSync(dir).filter(f => f.endsWith('report-full.json')).flatMap(f => read(path.join(dir, f)).Benchmarks);
}
/** Projects a BDN identity without job configuration, preserving full parameter values. */
function key(c) { return `${c.Type ?? c.type}|${c.Method ?? c.method}|${c.Parameters ?? c.parameters ?? ''}`; }
const output = { fast: {}, bdn: {}, current: [] };
for (const label of ['aa-cold', 'aa-persistent', 'aa-gc', 'aa-8ms', 'aa-tiering', 'controls', 'controls-reverse', 'controls-light', 'controls-light-reverse', 'aa-5ms', 'subset', 'revisions-forward', 'revisions-reverse']) {
  const data = read(`${root}/${label}.json`);
  if (!data) continue;
  const r = data.results;
  // The suite, rather than an individual case, is the trial for the familywise false-alarm audit.
  const suiteFalse = r.filter(x => x.cases.some(c => c.flagged)).length;
  const byCase = {};
  for (const c of r[0].cases) {
    // Join by case identity because each trial shuffles measurement order.
    const samples = r.map(x => x.cases.find(y => y.id === c.id));
    // Report both directions so a wrong-direction flag is not counted as detection of an injected cost.
    byCase[c.id] = { deltaMedian: quantile(samples.map(x => x.delta), 0.5), deltaP05: quantile(samples.map(x => x.delta), 0.05),
      deltaP95: quantile(samples.map(x => x.delta), 0.95), flags: samples.filter(x => x.flagged).length,
      positiveFlags: samples.filter(x => x.flagged && x.delta > 0).length,
      negativeFlags: samples.filter(x => x.flagged && x.delta < 0).length,
      thresholdFlags: samples.filter(x => x.thresholdFlag).length,
      allocDelta: quantile(samples.map(x => x.allocatedAfter - x.allocatedBefore), 0.5),
      allocChangedRuns: samples.filter(x => Math.abs(x.allocatedAfter - x.allocatedBefore) >= 1).length };
  }
  // Summaries retain routine wall-time boundaries; parent-measured command times live in the matrix files.
  output.fast[label] = { runs: r.length, cases: r[0].cases.length,
    medianSeconds: quantile(r.map(x => x.wallMs / 1000), 0.5), p95Seconds: quantile(r.map(x => x.wallMs / 1000), 0.95), maxSeconds: Math.max(...r.map(x => x.wallMs / 1000)),
    totalSeconds: data.totalMs / 1000, firstSeconds: r[0].wallMs / 1000,
    startupMedianMs: quantile(r.map(x => x.startupMs), 0.5), prepareMedianMs: quantile(r.map(x => x.prepareMs), 0.5),
    suiteFlagRuns: suiteFalse, suiteFlagRateCI: wilson(suiteFalse, r.length),
    correctedFlags: sum(r.map(x => x.cases.filter(c => c.flagged).length)),
    thresholdFlags: sum(r.map(x => x.cases.filter(c => c.thresholdFlag).length)),
    absoluteDeltaP95: quantile(r.flatMap(x => x.cases.map(c => Math.abs(c.delta))), 0.95), byCase };
}
// Include only BDN rows from mixed matrices; their other experiments have separate raw formats.
const matrices = [...(read(`${root}/matrix-bdn.json`) ?? []), ...(read(`${root}/matrix-power.json`) ?? []),
  ...(read(`${root}/matrix-more.json`) ?? []).filter(x => x.label === 'controls-light-pgo'),
  ...(read(`${root}/revisions.json`) ?? []).filter(x => x.label.startsWith('revision-long'))];
for (const row of matrices) {
  const cases = bdn(row.label);
  const phases = {};
  for (const c of cases) for (const m of c.Measurements ?? []) {
    const k = `${m.IterationMode}/${m.IterationStage}`;
    phases[k] = (phases[k] ?? 0) + m.Nanoseconds / 1e9;
  }
  // Keep per-case reference statistics, including the retained sample count after BDN's filtering.
  output.bdn[row.label] = { seconds: row.seconds, cases: cases.length, phases,
    values: cases.map(c => ({ key: key(c), median: c.Statistics?.Median, n: c.Statistics?.N,
      rsd: c.Statistics?.StandardDeviation / c.Statistics?.Mean, allocated: c.Memory?.BytesAllocatedPerOperation })) };
}
for (let i = 1; i <= 5; i++) {
  const dir = `artifacts/perf/quick/investigation-aa-${i}`;
  const before = read(`${dir}/before-best.json`), after = read(`${dir}/after-best.json`);
  if (!before || !after) continue;
  const rounds = [read(`${dir}/before-1/summary.json`), read(`${dir}/before-2/summary.json`), read(`${dir}/after-1/summary.json`), read(`${dir}/after-2/summary.json`)];
  // Compare matched identities in the production script's selected summaries.
  const changes = before.benchmarks.map(c => { const a = after.benchmarks.find(x => key(x) === key(c)); return { key: key(c), delta: a.medianNanoseconds / c.medianNanoseconds - 1 }; });
  // Preserve both launches on each side to expose instability hidden by selection.
  const spreads = before.benchmarks.map(c => rounds.map(r => r.benchmarks.find(x => key(x) === key(c)).medianNanoseconds));
  // A first-launch-only comparison provides one reference for the selection audit.
  const single = spreads.map(v => v[2] / v[0] - 1);
  // Averaging both medians is a descriptive comparison, not a new statistical decision rule.
  const pooled = spreads.map(v => (v[2] + v[3]) / (v[0] + v[1]) - 1);
  // Measure optimistic selection discounts separately from between-launch spread and threshold counts.
  output.current.push({ run: i, changes, flags: changes.filter(x => Math.abs(x.delta) > 0.03).length,
    singleRoundFlags: single.filter(x => Math.abs(x) > 0.03).length,
    meanOfMediansFlags: pooled.filter(x => Math.abs(x) > 0.03).length,
    minimumDiscountMedian: quantile(spreads.flatMap(v => [Math.min(v[0], v[1]) / ((v[0] + v[1]) / 2) - 1, Math.min(v[2], v[3]) / ((v[2] + v[3]) / 2) - 1]), 0.5),
    sideRoundSpreadP95: quantile(spreads.flatMap(v => [Math.max(v[0], v[1]) / Math.min(v[0], v[1]) - 1, Math.max(v[2], v[3]) / Math.min(v[2], v[3]) - 1]), 0.95) });
}
fs.writeFileSync(`${root}/analysis.json`, JSON.stringify(output, null, 2));
const subset = read(`${root}/subset.json`);
if (subset) {
  // Preserve the exact timed subset and its omitted identities, including any original display-name abbreviations.
  const selected = subset.results[0].cases.map(c => c.id).sort();
  const all = subset.results[0].ready[0].cases;
  // The difference is timed coverage, even though all fixtures were prepared in both workers.
  fs.writeFileSync(`${root}/coverage.json`, JSON.stringify({ all, selected, omitted: all.filter(id => !selected.includes(id)) }, null, 2));
}
// Keep terminal output compact; detailed per-case evidence remains in analysis.json.
console.log(JSON.stringify({ fast: Object.fromEntries(Object.entries(output.fast).map(([k, { byCase, ...v }]) => [k, v])),
  bdn: Object.fromEntries(Object.entries(output.bdn).map(([k, { values, ...v }]) => [k, v])), current: output.current.map(({ changes, ...v }) => v) }, null, 2));
