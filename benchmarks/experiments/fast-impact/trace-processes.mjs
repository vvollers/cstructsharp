// Instruments child-process wall time without changing the comparison script.
// Usage: PERF_PROCESS_TRACE=<jsonl> node --import ./benchmarks/experiments/fast-impact/trace-processes.mjs tools/quality/quick-perf-check.mjs ...
import childProcess from 'node:child_process';
import { syncBuiltinESMExports } from 'node:module';
import fs from 'node:fs';
import { performance } from 'node:perf_hooks';

for (const name of ['execFileSync', 'spawnSync']) {
  const original = childProcess[name];
  /** Records duration and command identity, preserving return values and failures. */
  childProcess[name] = function (...args) {
    const start = performance.now();
    try { return original.apply(this, args); }
    finally {
      if (process.env.PERF_PROCESS_TRACE) fs.appendFileSync(process.env.PERF_PROCESS_TRACE,
        JSON.stringify({ tool: name, file: args[0], args: args[1], ms: performance.now() - start }) + '\n');
    }
  };
}
syncBuiltinESMExports();
