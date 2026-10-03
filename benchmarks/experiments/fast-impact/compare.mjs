#!/usr/bin/env node
// Serial paired measurement experiment. Build FastImpact.csproj first, then run:
// node benchmarks/experiments/fast-impact/compare.mjs --out artifacts/perf/fast-impact/cold.json --repeats 30
// Options: --persistent, --ms 2, --pairs 12, --gc, --controls, --tiering 0|1, --seed 42,
// --before path/to/FastImpact.dll --after path/to/FastImpact.dll. No builds occur during timing.
import { spawn } from 'node:child_process';
import { createInterface } from 'node:readline';
import { performance } from 'node:perf_hooks';
import fs from 'node:fs';
import path from 'node:path';
import crypto from 'node:crypto';

const invocation = performance.now();
const args = process.argv.slice(2);
/** Gets an option's following value or its fallback. */
function option(name, fallback) { const i = args.indexOf(`--${name}`); return i < 0 ? fallback : args[i + 1]; }
const defaults = 'benchmarks/experiments/fast-impact/bin/Release/net10.0/FastImpact.dll';
const options = { before: option('before', defaults), after: option('after', defaults), ms: +option('ms', 2),
  pairs: +option('pairs', 12), repeats: +option('repeats', 1), persistent: args.includes('--persistent'),
  gc: args.includes('--gc'), controls: args.includes('--controls'), reverse: args.includes('--reverse'), tiering: option('tiering', '0'), seed: +option('seed', 42) };
options.include = option('include', '');
const out = option('out', 'artifacts/perf/fast-impact/experiment.json');
let randomState = options.seed;
/** Produces a repeatable uniform shuffle value, independent of worker timing. */
function random() { randomState = (Math.imul(randomState, 1664525) + 1013904223) >>> 0; return randomState / 2 ** 32; }
/** Shuffles a copy without changing the manifest's case indices. */
function shuffle(items) { const copy = [...items]; for (let i = copy.length - 1; i > 0; i--) { const j = Math.floor(random() * (i + 1)); [copy[i], copy[j]] = [copy[j], copy[i]]; } return copy; }
/** Computes a sample median without discarding slow observations. */
function median(values) {
  // Sort a copy so the raw batch order remains available for an order-effect audit.
  const v = [...values].sort((a, b) => a - b);
  return (v[Math.floor(v.length / 2)] + v[Math.floor((v.length - 1) / 2)]) / 2;
}
/** Calculates the exact two-sided sign-test probability under independent paired observations. */
function signP(values) {
  // Exact ties do not contribute a sign trial.
  const n = values.filter(v => v !== 0).length;
  // Either direction is evidence against the null; use the smaller tail.
  const k = Math.min(values.filter(v => v > 0).length, values.filter(v => v < 0).length);
  let term = 1, sum = 1;
  for (let i = 1; i <= k; i++) { term *= (n - i + 1) / i; sum += term; }
  return Math.min(1, 2 * sum / 2 ** n);
}
/** Fingerprints top-level DLL/JSON files, excluding external fixtures; this prototype never caches results. */
function fingerprint(dll) {
  const dir = path.dirname(path.resolve(dll));
  // Record the managed inputs without claiming that external fixture provenance has been verified.
  return fs.readdirSync(dir).filter(f => /\.(dll|json)$/.test(f)).sort().map(file => ({ file,
    sha256: crypto.createHash('sha256').update(fs.readFileSync(path.join(dir, file))).digest('hex') }));
}
/** Starts one idle worker, queues JSON responses and fails on unexpected exit. */
async function worker(dll) {
  const child = spawn('dotnet', [path.resolve(dll), 'worker', ...(options.controls ? ['controls'] : [])], {
    env: { ...process.env, DOTNET_TieredCompilation: options.tiering }, stdio: ['pipe', 'pipe', 'inherit'], windowsHide: true,
  });
  const lines = createInterface({ input: child.stdout });
  const queue = []; let pending; let failure;
  // There is only one outstanding command per worker; unsolicited output fails JSON parsing.
  lines.on('line', line => { const value = JSON.parse(line); if (pending) { const p = pending; pending = null; p.resolve(value); } else queue.push(value); });
  // An unexpected worker failure must reject an outstanding command rather than hang the experiment.
  child.on('exit', code => { if (code !== 0) { failure = new Error(`Worker exited ${code}`); pending?.reject(failure); } });
  /** Reads the next response, propagating worker failures. */
  function receive() {
    if (failure) return Promise.reject(failure);
    if (queue.length) return Promise.resolve(queue.shift());
    // The line/exit handlers complete this one outstanding protocol request.
    return new Promise((resolve, reject) => { pending = { resolve, reject }; });
  }
  const ready = await receive();
  return { ready, child,
    /** Sends one command and waits for its completed response before any other work starts. */
    async send(command) { child.stdin.write(JSON.stringify(command) + '\n'); return receive(); },
    /** Waits for cleanup and successful process exit. */
    async close() {
      // Register before sending quit so a fast cleanup cannot race the exit listener.
      const done = new Promise((resolve, reject) => child.once('exit', code => code === 0 ? resolve() : reject(new Error(`Exit ${code}`))));
      child.stdin.end('{"op":"quit"}\n');
      await done;
    },
  };
}
/** Starts both workers sequentially, validates identities and freshly calibrates each. */
async function start() {
  const t = performance.now();
  const a = await worker(options.before);
  const b = await worker(options.after);
  if (JSON.stringify(a.ready.cases) !== JSON.stringify(b.ready.cases)) throw new Error('Case manifests differ');
  const startupMs = performance.now() - t;
  const ca = await a.send({ op: 'prepare', ms: options.ms });
  const cb = await b.send({ op: 'prepare', ms: options.ms });
  await a.send({ op: 'gc' }); await b.send({ op: 'gc' });
  return { a, b, ca, cb, startupMs, prepareMs: performance.now() - t - startupMs };
}
/** Runs a whole suite with shuffled cases and balanced alternating side order within each case. */
async function compare(state, repetition) {
  const t = performance.now();
  const { a, b, ca, cb } = state;
  const cases = [];
  const manifest = a.ready.cases;
  // Controls compare each validation count with zero validation; both execute one identical parse.
  const indices = shuffle(manifest.map((id, index) => ({ id, index })).filter(x => !options.include || new RegExp(options.include).test(x.id)));
  if (indices.length === 0) throw new Error('Empty selection');
  for (const { id, index } of indices) {
    const baselineIndex = options.controls ? manifest.indexOf(id.split('|')[0] + '|[Checks=0, Scratch=0]') : index;
    if (baselineIndex < 0) throw new Error(`Missing control baseline in ${manifest}`);
    const ai = options.reverse ? index : baselineIndex;
    const bi = options.reverse ? baselineIndex : index;
    const count = Math.max(ca[ai].Count, cb[bi].Count);
    const before = [], after = [];
    const flip = random() < 0.5;
    for (let pair = 0; pair < options.pairs; pair++) {
      /** Measures and retains one baseline batch; its peer remains idle until completion. */
      const takeA = async () => before.push(await a.send({ op: 'batch', index: ai, count, gc: options.gc }));
      /** Measures and retains one candidate batch; its peer remains idle until completion. */
      const takeB = async () => after.push(await b.send({ op: 'batch', index: bi, count, gc: options.gc }));
      if ((pair % 2 === 0) !== flip) { await takeA(); await takeB(); } else { await takeB(); await takeA(); }
    }
    // Keep matching AB/BA pairs together rather than pooling away their pairing.
    const ratios = before.map((v, i) => after[i].ns / v.ns);
    const delta = median(ratios) - 1;
    // Log-ratio signs distinguish speedups and slowdowns around a ratio of one.
    const p = signP(ratios.map(v => Math.log(v)));
    // Both sides performed the same operation count; counters are normalized independently of time.
    const allocatedBefore = median(before.map(v => v.bytes / count));
    // Retain candidate allocation separately so a time/space tradeoff remains visible.
    const allocatedAfter = median(after.map(v => v.bytes / count));
    cases.push({ id, count, before, after, ratios, delta, p, allocatedBefore, allocatedAfter,
      // Bonferroni controls suite multiplicity only if paired signs behave independently; A/A audits this assumption.
      flagged: p <= 0.05 / Math.max(55, manifest.length) && Math.abs(delta) > 0.03,
      thresholdFlag: Math.abs(delta) > 0.03, range: [Math.min(...ratios) - 1, Math.max(...ratios) - 1] });
  }
  return { repetition, measureMs: performance.now() - t, cases };
}

const metadata = { options, node: process.version, before: fingerprint(options.before), after: fingerprint(options.after) };
const results = [];
let state;
try {
  for (let repetition = 0; repetition < options.repeats; repetition++) {
    const startTime = performance.now();
    if (!state) state = await start();
    const result = await compare(state, repetition);
    result.startupMs = repetition === 0 || !options.persistent ? state.startupMs : 0;
    result.prepareMs = repetition === 0 || !options.persistent ? state.prepareMs : 0;
    result.ready = [state.a.ready, state.b.ready];
    if (!options.persistent) { await state.a.close(); await state.b.close(); state = null; }
    result.wallMs = performance.now() - startTime;
    results.push(result);
    fs.mkdirSync(path.dirname(out), { recursive: true });
    fs.writeFileSync(out, JSON.stringify({ metadata, totalMs: performance.now() - invocation, results }));
    // Emit progress counts only; the saved report retains every sample and both decision rules.
    console.log(JSON.stringify({ repetition, seconds: result.wallMs / 1000, cases: result.cases.length,
      thresholdFlags: result.cases.filter(c => c.thresholdFlag).length, flags: result.cases.filter(c => c.flagged).length }));
  }
} finally {
  if (state) { await state.a.close(); await state.b.close(); }
}
fs.writeFileSync(out, JSON.stringify({ metadata, totalMs: performance.now() - invocation, results }));
