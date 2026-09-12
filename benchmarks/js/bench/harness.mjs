// Shared measurement core for Node and the browser page.
//
// Two phases per case:
//   1. Batched throughput (primary numbers): warm for >= warmupTime, calibrate a batch to ~batchTargetMs, then time
//      `batches` batches. medianNanoseconds/RSD come from the per-batch ns/op samples, which is robust for
//      sub-microsecond calls where per-call timer resolution and microtask scheduling dominate.
//   2. Per-call latency distribution via tinybench (retained samples): only for cases whose batched median is
//      >= latencyThresholdNs (default 50 µs), where p50/p95/p99 per call are meaningful (worker round-trips, large
//      payloads). Reported under `latency`.
// A dead-code sink consumes every returned value so engines cannot eliminate the work.
import { Bench } from "tinybench";

export const RSD_LIMIT = 0.35;

let sink = 0;
export function consume(value) {
  if (value == null) return;
  if (typeof value === "number") sink = (sink + value) | 0;
  else if (typeof value === "string") sink = (sink + value.length) | 0;
  else if (value.byteLength !== undefined) sink = (sink + value.byteLength) | 0;
  else if (value.Data !== undefined) sink = (sink + (typeof value.Data === "string" ? value.Data.length : value.Data?.byteLength ?? 0)) | 0;
  else sink = (sink + 1) | 0;
}
export function sinkValue() {
  return sink;
}

const now = () => performance.now();

function percentile(sorted, p) {
  if (sorted.length === 0) return NaN;
  const index = Math.min(sorted.length - 1, Math.max(0, Math.ceil((p / 100) * sorted.length) - 1));
  return sorted[index];
}

function median(values) {
  const sorted = [...values].sort((a, b) => a - b);
  const mid = sorted.length >> 1;
  return sorted.length % 2 ? sorted[mid] : (sorted[mid - 1] + sorted[mid]) / 2;
}

async function timedBatch(fn, count) {
  const start = now();
  for (let i = 0; i < count; i++) consume(await fn());
  return (now() - start) * 1e6 / count; // ns per op
}

export async function runCases(
  cases,
  {
    warmupTime = 600,
    batchTargetMs = 100,
    batches = 9,
    latencyThresholdNs = 50_000,
    latencyTime = 1500,
    onResult,
    allocatedBytes,
  } = {},
) {
  const records = [];
  for (const benchmarkCase of cases) {
    if (benchmarkCase.before) await benchmarkCase.before();

    // Phase 1a: warm-up for at least warmupTime ms.
    let warmCalls = 0;
    const warmStart = now();
    while (now() - warmStart < warmupTime) {
      consume(await benchmarkCase.fn());
      warmCalls++;
    }
    const warmNsPerOp = ((now() - warmStart) * 1e6) / warmCalls;
    // Phase 1b: calibrate a batch to ~batchTargetMs, then sample `batches` batches.
    const batchCount = Math.max(1, Math.min(1_000_000, Math.round((batchTargetMs * 1e6) / warmNsPerOp)));
    const samples = [];
    for (let i = 0; i < batches; i++) samples.push(await timedBatch(benchmarkCase.fn, batchCount));
    const med = median(samples);
    const mean = samples.reduce((a, b) => a + b, 0) / samples.length;
    const sd = Math.sqrt(samples.reduce((a, b) => a + (b - mean) ** 2, 0) / Math.max(1, samples.length - 1));

    // Phase 2: per-call latency percentiles for slow cases.
    let latency = null;
    if (med >= latencyThresholdNs) {
      const bench = new Bench({ time: latencyTime, warmupTime: 0, iterations: 5, warmupIterations: 0, retainSamples: true, throws: true });
      bench.add(benchmarkCase.name, async () => consume(await benchmarkCase.fn()));
      await bench.run();
      const stats = bench.tasks[0].result.latency;
      const sorted = stats.samples ? Array.from(stats.samples).sort((a, b) => a - b) : [];
      latency = {
        samples: stats.samplesCount,
        p50Nanoseconds: Math.round(stats.p50 * 1e6),
        p95Nanoseconds: Math.round(percentile(sorted, 95) * 1e6),
        p99Nanoseconds: Math.round(stats.p99 * 1e6),
        maxNanoseconds: Math.round(stats.max * 1e6),
      };
    }

    // Phase 3: managed allocation per op, outside the timed phases, over enough calls to pass the allocation
    // context granularity of the Mono GC counters.
    let allocated = null;
    if (allocatedBytes) {
      const calls = Math.max(3, Math.min(5000, Math.ceil(5e8 / med)));
      const start = allocatedBytes();
      for (let i = 0; i < calls; i++) consume(await benchmarkCase.fn());
      allocated = Math.round((allocatedBytes() - start) / calls);
    }

    const record = {
      name: benchmarkCase.name,
      tags: benchmarkCase.tags ?? [],
      meta: benchmarkCase.meta ?? {},
      batchSize: batchCount,
      batches: samples.length,
      medianNanoseconds: Math.round(med),
      meanNanoseconds: Math.round(mean),
      minNanoseconds: Math.round(Math.min(...samples)),
      standardDeviationNanoseconds: Math.round(sd),
      relativeStandardDeviation: med > 0 ? Number((sd / med).toFixed(4)) : null,
      unstable: med > 0 && sd / med > RSD_LIMIT,
      latency,
      managedAllocatedBytesPerOp: allocated,
    };
    records.push(record);
    if (onResult) onResult(record);
  }
  return records;
}

export function formatRecord(record) {
  const ns = (v) => (v >= 1e6 ? `${(v / 1e6).toFixed(2)} ms` : v >= 1e3 ? `${(v / 1e3).toFixed(1)} µs` : `${v} ns`);
  const alloc = record.managedAllocatedBytesPerOp == null ? "" : ` alloc=${record.managedAllocatedBytesPerOp} B`;
  const lat = record.latency ? ` | per-call p50 ${ns(record.latency.p50Nanoseconds)} p95 ${ns(record.latency.p95Nanoseconds)} p99 ${ns(record.latency.p99Nanoseconds)}` : "";
  const flag = record.unstable ? " UNSTABLE" : "";
  return `${record.name}: median ${ns(record.medianNanoseconds)} rsd ${(record.relativeStandardDeviation * 100).toFixed(1)}% (${record.batches}×${record.batchSize})${alloc}${lat}${flag}`;
}
