// Serial full-Impact affinity A/A experiment. Build FastImpact.csproj first.
// Usage: node benchmarks/experiments/fast-impact/affinity.mjs [rounds=8] [label=affinity] [cpu-list=none,8,24]
import fs from 'node:fs';
import { spawnSync } from 'node:child_process';
import { performance } from 'node:perf_hooks';
const rounds = Number(process.argv[2] ?? 8);
const label = process.argv[3] ?? 'affinity';
const cpus = (process.argv[4] ?? 'none,8,24').split(',');
const root = `artifacts/perf/fast-impact/${label}`;
fs.mkdirSync(root, { recursive: true });
const records = [];
/** Reads the original full BDN reports and retains all actual operation samples, including slow ones. */
function readCases(dir) {
  const cases = [];
  for (const name of fs.readdirSync(`${dir}/results`)) {
    if (!name.endsWith('report-full.json')) continue;
    for (const c of JSON.parse(fs.readFileSync(`${dir}/results/${name}`, 'utf8')).Benchmarks) {
      if (!c.Statistics || c.Memory?.BytesAllocatedPerOperation === undefined) throw Error('Incomplete case');
      // Compare raw actual samples so changing outlier filtering cannot hide instability.
      const samples = c.Measurements.filter(m => m.IterationMode === 'Workload' && m.IterationStage === 'Actual').map(m => m.Nanoseconds / m.Operations);
      cases.push({ id: `${c.Type}|${c.Method}|${c.Parameters}`, samples, median: median(samples), allocated: c.Memory.BytesAllocatedPerOperation });
    }
  }
  return cases;
}
/** Computes a middle-pair median without modifying raw sample order. */
function median(values) {
  // Numeric ordering is needed because durations are numbers, not strings.
  const sorted = [...values].sort((a, b) => a - b);
  return (sorted[Math.floor(sorted.length / 2)] + sorted[Math.floor((sorted.length - 1) / 2)]) / 2;
}
for (let round = 0; round < rounds; round++) {
  // Rotate configurations to distribute time trends across the affinity choices.
  for (let offset = 0; offset < cpus.length; offset++) {
    const cpu = cpus[(round + offset) % cpus.length];
    const start = performance.now();
    const sides = {};
    for (const side of round % 2 ? ['after', 'before'] : ['before', 'after']) {
      const dir = `${root}/${cpu}-${round}-${side}`;
      const fd = fs.openSync(`${dir}.log`, 'w');
      const result = spawnSync('dotnet', ['benchmarks/experiments/fast-impact/bin/Release/net10.0/FastImpact.dll', 'bdn', '1', '12', 'false', 'true', dir, 'user-power'],
        { env: { ...process.env, DOTNET_TieredCompilation: '0', PERF_CPU: cpu }, stdio: ['ignore', fd, fd], windowsHide: true });
      fs.closeSync(fd);
      if (result.status !== 0) throw result.error ?? Error(`Failed ${dir}`);
      sides[side] = readCases(dir);
    }
    // Match identities after changing launch order; no operation implementation is duplicated.
    const cases = sides.before.map(b => {
      const a = sides.after.find(a => a.id === b.id);
      if (!a) throw Error(`Missing ${b.id}`);
      return { id: b.id, delta: a.median / b.median - 1, before: b.samples, after: a.samples, allocatedBefore: b.allocated, allocatedAfter: a.allocated };
    });
    const record = { round, cpu, seconds: (performance.now() - start) / 1000, cases };
    records.push(record);
    fs.writeFileSync(`${root}/results.json`, JSON.stringify(records));
    // This is a descriptive threshold count, not a statistical declaration of regressions.
    console.log(JSON.stringify({ round, cpu, seconds: record.seconds, flags: cases.filter(c => Math.abs(c.delta) > 0.03).length }));
  }
}
