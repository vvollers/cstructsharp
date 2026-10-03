#!/usr/bin/env node
// Serial experiment driver. Usage: node benchmarks/experiments/fast-impact/matrix.mjs fast|bdn|current|power|more|screen [label]
// Build first and run no other timing, tests, or builds until this command exits.
import { spawnSync } from 'node:child_process';
import fs from 'node:fs';
import { performance } from 'node:perf_hooks';

const root = 'artifacts/perf/fast-impact';
fs.mkdirSync(root, { recursive: true });
const dll = 'benchmarks/experiments/fast-impact/bin/Release/net10.0/FastImpact.dll';
const records = [];
/** Executes one entire experiment and records invocation-to-exit time, failing immediately on errors. */
function run(label, exe, args, env = {}) {
  const output = fs.openSync(`${root}/${label}.log`, 'w');
  const start = performance.now();
  const result = spawnSync(exe, args, { env: { ...process.env, ...env }, stdio: ['ignore', output, output], windowsHide: true });
  fs.closeSync(output);
  records.push({ label, seconds: (performance.now() - start) / 1000, status: result.status, args });
  fs.writeFileSync(`${root}/matrix-${process.argv[2]}.json`, JSON.stringify(records, null, 2));
  console.log(JSON.stringify(records.at(-1)));
  if (result.status !== 0) throw new Error(`Failed ${label}; inspect log`);
}

if (process.argv[2] === 'fast') {
  for (const [label, extra] of [
    ['aa-persistent', ['--persistent', '--repeats', '30']],
    ['aa-gc', ['--gc', '--repeats', '10']],
    ['aa-8ms', ['--ms', '8', '--repeats', '10']],
    ['aa-tiering', ['--tiering', '1', '--repeats', '10']],
    ['controls', ['--controls', '--include', 'Controls.Parse\\|', '--repeats', '30']],
    ['controls-reverse', ['--controls', '--include', 'Controls.Parse\\|', '--reverse', '--repeats', '30']],
  ]) run(label, process.execPath, ['benchmarks/experiments/fast-impact/compare.mjs', '--out', `${root}/${label}.json`, ...extra]);
} else if (process.argv[2] === 'bdn') {
  for (const [shape, ms, n, gc, memory, repeats] of [
    ['profile', 25, 5, true, true, 1],
    ['no-force', 25, 5, false, true, 2],
    ['tiny', 5, 5, false, true, 6],
    ['tiny-no-memory', 5, 5, false, false, 2],
    ['long', 100, 30, true, true, 2],
    ['controls-long', 100, 30, true, true, 1],
    ['controls-light-long', 100, 30, true, true, 3],
  ]) {
    for (let repeat = 1; repeat <= repeats; repeat++) {
      const label = `${shape}-${repeat}`;
      run(label, 'dotnet', [dll, shape.includes('light') ? 'controls-light-bdn' : shape.startsWith('controls') ? 'controls-bdn' : 'bdn', `${ms}`, `${n}`, `${gc}`, `${memory}`,
        `${root}/${label}`, ...(shape === 'profile' ? ['profile'] : [])], { DOTNET_TieredCompilation: '0' });
    }
  }
} else if (process.argv[2] === 'power') {
  run('profile-user-1', 'dotnet', [dll, 'bdn', '25', '5', 'true', 'true', `${root}/profile-user-1`, 'user-power', 'profile'], { DOTNET_TieredCompilation: '0' });
  const comparisons = [];
  for (const [shape, ms, n] of [['tiny-user', 5, 5], ['micro-user', 1, 12]]) {
    for (let comparison = 1; comparison <= 5; comparison++) {
      const start = performance.now();
      const summaries = [];
      for (let side = 0; side < 2; side++) {
        const label = `${shape}-${2 * comparison - 1 + side}`;
        run(label, 'dotnet', [dll, 'bdn', `${ms}`, `${n}`, 'false', 'true', `${root}/${label}`, 'user-power'], { DOTNET_TieredCompilation: '0' });
        const summary = `${root}/${label}/summary.json`;
        run(`${label}-convert`, process.execPath, ['tools/quality/convert-benchmark-baseline.mjs', `${root}/${label}/results`, summary]);
        summaries.push(summary);
      }
      const label = `${shape}-comparison-${comparison}`;
      run(label, process.execPath, ['tools/quality/compare-summaries.mjs', '--before', summaries[0], '--after', summaries[1]]);
      fs.copyFileSync(`${root}/${label}.log`, `${root}/${label}.md`);
      comparisons.push({ shape, comparison, seconds: (performance.now() - start) / 1000, summaries });
      fs.writeFileSync(`${root}/power-comparisons.json`, JSON.stringify(comparisons, null, 2));
      console.log(JSON.stringify(comparisons.at(-1)));
    }
  }
} else if (process.argv[2] === 'screen') {
  const prefix = `screen-${process.argv[3] ?? 'single'}`;
  const summaries = [];
  for (const side of ['before', 'after']) {
    const label = `${prefix}-${side}`;
    run(label, 'dotnet', [dll, 'bdn', '1', '12', 'false', 'true', `${root}/${label}`, 'user-power'], { DOTNET_TieredCompilation: '0' });
    const summary = `${root}/${label}/summary.json`;
    run(`${label}-convert`, process.execPath, ['tools/quality/convert-benchmark-baseline.mjs', `${root}/${label}/results`, summary]);
    summaries.push(summary);
  }
  run(`${prefix}-comparison`, process.execPath, ['tools/quality/compare-summaries.mjs', '--before', summaries[0], '--after', summaries[1]]);
  console.log(fs.readFileSync(`${root}/${prefix}-comparison.log`, 'utf8'));
} else if (process.argv[2] === 'more') {
  const subset = 'Runtime_PrimRecord_ParseAsync_MemoryStream|HandWritten_PrimRecord|Generated_PrimRecord_Parse\\||ImpactParseBenchmarks.ParseSpan\\|\\[Fixture=prim-le-record|PacketBenchmarks.ParseSpan\\||ReadSelectedScalarTypedMemory|CompileBenchmarks.Compile\\|\\[Fixture=compile-small|ReadValue_Scalar_Natural\\|\\[Index=0|Serialize_Prim_Poco_ToSpan\\||Update_Bitfield\\||Runtime_PrimRecord_FourSegments\\||SerializeTerminatedUtf8';
  for (const [label, extra] of [
    ['aa-5ms', ['--ms', '5', '--repeats', '10']],
    ['controls-light', ['--controls', '--include', 'ParseLight', '--repeats', '30']],
    ['controls-light-reverse', ['--controls', '--include', 'ParseLight', '--reverse', '--repeats', '30']],
    ['subset', ['--include', subset, '--ms', '8', '--pairs', '24', '--repeats', '10']],
  ]) run(label, process.execPath, ['benchmarks/experiments/fast-impact/compare.mjs', '--out', `${root}/${label}.json`, ...extra]);
  for (let repeat = 1; repeat <= 3; repeat++) run(`targeted-${repeat}`, process.execPath,
    ['tools/quality/quick-perf-check.mjs', '--baseline', '.', '--job', 'Quick', '--filter', '*PacketBenchmarks.ParseSpan*', '--label', `investigation-targeted-${repeat}`]);
  run('controls-light-pgo', 'dotnet', [dll, 'controls-light-bdn', '100', '30', 'true', 'true', `${root}/controls-light-pgo`, 'oop'], { DOTNET_TieredCompilation: '1' });
  for (let repeat = 1; repeat <= 3; repeat++) run(`external-cold-${repeat}`, process.execPath,
    ['benchmarks/experiments/fast-impact/compare.mjs', '--out', `${root}/external-cold-${repeat}.json`]);
} else if (process.argv[2] === 'current') {
  for (let repeat = 2; repeat <= 3; repeat++) run(`current-aa-${repeat}`, process.execPath,
    ['--import', './benchmarks/experiments/fast-impact/trace-processes.mjs', 'tools/quality/quick-perf-check.mjs', '--baseline', '.', '--job', 'Quick', '--categories', 'Impact', '--label', `investigation-aa-${repeat}`],
    { PERF_PROCESS_TRACE: `${root}/current-aa-${repeat}-processes.jsonl` });
} else throw new Error('Expected fast, bdn, current, power, more, or screen');
