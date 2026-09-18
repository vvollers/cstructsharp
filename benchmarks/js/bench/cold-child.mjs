// One cold-start sample: a fresh Node process loads the runtime, compiles a schema, and parses once.
// Prints a JSON line with the phase timings (ms). Invoked by cold.mjs.
import { loadFixture } from "./fixtures.mjs";
import { loadBundle } from "./runtime.mjs";

const id = process.argv[2] ?? "real-png";
const t0 = performance.now();
const bundle = await loadBundle();
const t1 = performance.now();
const { document, bytes } = loadFixture(id);
const options = { pointerSize: document.options.pointerSize, aligned: document.options.aligned, littleEndian: document.options.littleEndian, root: document.root };
const optionsJson = JSON.stringify(options);
bundle.managed.BenchCompile(document.definition, optionsJson);
const t2 = performance.now();
const consumed = bundle.managed.BenchParseCore(bytes, document.root, optionsJson);
const t3 = performance.now();
const result = await bundle.api.parse(document.definition, bytes, options);
const t4 = performance.now();
console.log(JSON.stringify({
  fixture: id,
  processStartToBundleReadyMs: t1 - t0,
  runtimeCreateMs: bundle.timings.runtimeCreateMs,
  exportsReadyMs: bundle.timings.exportsReadyMs,
  firstCompileMs: t2 - t1,
  firstCoreParseMs: t3 - t2,
  firstPublicParseMs: t4 - t3,
  consumed,
  publicSuccess: result.success,
}));
process.exit(0);
