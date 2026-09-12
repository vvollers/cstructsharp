import assert from 'node:assert/strict';
import fs from 'node:fs';
import path from 'node:path';
import { pathToFileURL } from 'node:url';
const root = process.cwd();
const bundle = path.join(root, 'src/CStructSharp.Wasm/bin/Release/net10.0/browser-wasm/AppBundle');
for (const file of ['bootstrap.js', 'large-source.js', 'source-worker.js', 'cstructsharp-api.js'])
  fs.copyFileSync(path.join(root, 'src/CStructSharp.Wasm', file), path.join(bundle, file));
const { compileLargeSource, parseLargeSource } = await import(pathToFileURL(path.join(bundle, 'large-source.js')));
const definition = 'struct root { uint8 tag; if (tag == 1) { uint32 value; } else { uint16 small; } uint8 tail; };';
const options = { aligned: false };
const compiled = await compileLargeSource(definition, options);
options.aligned = true;
const a = new Uint8Array([1, 42, 0, 0, 0, 7]);
const b = new Uint8Array([0, 19, 0, 8]);
function value(result) { assert.equal(result.Success, true, JSON.stringify(result)); return JSON.parse(result.Data).root; }
try {
  const results = await Promise.all(Array.from({length: 20}, (_, i) => compiled.parse(i % 2 ? b : a)));
  results.forEach((result, i) => assert.deepEqual(value(result), i % 2 ? {tag:0,small:19,tail:8} : {tag:1,value:42,tail:7}));
  const debug = await compiled.parseWithDebug(a);
  assert.deepEqual(debug, await parseLargeSource(definition, a, {}, true));
  assert.equal((await compiled.parse(a, {maxTotalBytesRead: 1})).Success, false);
  assert.equal(value(await compiled.parse(a)).value, 42);
  await assert.rejects(compiled.parse(a, {aligned: true}), /fixed at compilation/);
  const cancelled = new AbortController(); cancelled.abort();
  await assert.rejects(compiled.parse(a, {signal: cancelled.signal}), {name: 'AbortError'});
  let released = false;
  const controller = new AbortController();
  const hanging = { [Symbol.asyncIterator]() { return {next: () => new Promise(() => {}), return() { released = true; return Promise.resolve({done:true}); }}; }};
  const pending = compiled.parse(hanging, {signal:controller.signal});
  await new Promise(resolve => setTimeout(resolve, 20)); controller.abort();
  await assert.rejects(pending, {name:'AbortError'});
  assert.equal(value(await compiled.parse(b)).small, 19);
  assert.equal(released, true);
  // Abort a CPU-bound worker, then verify automatic recompilation on the next read.
  const expensive = await compileLargeSource('struct root { uint8 data[1000000]; };');
  try {
    const abort = new AbortController();
    const pending = expensive.parse(new Uint8Array(1000000), {signal:abort.signal, maxArrayElements:1000000, maxTotalBytesRead:40000000});
    setTimeout(() => abort.abort(), 50);
    await assert.rejects(pending, {name:'AbortError'});
    assert.equal((await expensive.parse(new Uint8Array(1))).Success, false);
  } finally { await expensive.dispose(); }
  const pendingDispose = compiled.parse(hanging);
  const queued = compiled.parse(a);
  const checkedPending = assert.rejects(pendingDispose, {name:'AbortError'});
  const checkedQueued = assert.rejects(queued, {name:'AbortError'});
  await new Promise(resolve => setTimeout(resolve, 10));
  await compiled.dispose();
  await Promise.all([checkedPending, checkedQueued]);
  await assert.rejects(compiled.parse(a), /disposed/);
} finally { await compiled.dispose(); }
await assert.rejects(compileLargeSource('struct broken { missing value; };'));
const other = await compileLargeSource('struct root { uint16 value; };', {littleEndian:false});
try { assert.equal(value(await other.parse(new Uint8Array([1,2]))).value, 258); }
finally { await other.dispose(); }
assert.equal(value(await parseLargeSource('struct root { uint8 value; };', new Uint8Array([9]), {}, false)).value, 9);
console.log('PASS compiled layouts: mixed branches, concurrency, debug parity, limits, cancellation, recovery, disposal, isolation');

// Staging an ordinary source must not block another source that is ready now.
const slowAbort = new AbortController();
const slowSource = { [Symbol.asyncIterator]() { return {next: () => new Promise(() => {}), return: async () => ({done:true})}; }};
const slowRead = parseLargeSource('struct root { uint8 value; };', slowSource, {signal:slowAbort.signal}, false);
const checkedSlow = assert.rejects(slowRead, {name:'AbortError'});
try {
  assert.equal(value(await parseLargeSource('struct root { uint8 value; };', new Uint8Array([11]), {signal:AbortSignal.timeout(5000)}, false)).value, 11);
} finally { slowAbort.abort(); await checkedSlow; }
console.log('PASS ordinary source staging remains independent');
