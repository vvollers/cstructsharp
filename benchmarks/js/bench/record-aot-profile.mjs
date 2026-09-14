// Records an AOT profile (E3.1b) from a profiler-enabled bundle: run the representative workload, trigger the
// profiler's write-at method (which sends the data to BenchReceiveAotProfile), and save it for `-p:AOTProfilePath=`.
//   node build-wasm.mjs --bundle bundle-aotprof "-p:WasmProfilers=aot;"
//   BENCH_BUNDLE=bundle-aotprof node bench/record-aot-profile.mjs [output.aotprofile]
import fs from "node:fs";
import path from "node:path";
import { loadBundle } from "./runtime.mjs";
import { repositoryRoot, loadFixture, manifest } from "./fixtures.mjs";

const output = process.argv[2] ?? path.join(repositoryRoot, "artifacts/js-bench/cstructsharp.aotprofile");
// BENCH_AOT_PROFILE_SCOPE=core records only the retained-layout core parse path (no public API, no JSON
// projection), which keeps the profile - and therefore the AOT'd part of CoreLib - to what parsing itself needs.
const coreOnly = process.env.BENCH_AOT_PROFILE_SCOPE === "core";
const env = await loadBundle({
  config: {
    aotProfilerOptions: {
      writeAt: "CStructSharpWeb.Wasm.CStructExports::BenchStopAotProfile",
      sendTo: "CStructSharpWeb.Wasm.CStructExports::BenchReceiveAotProfile",
    },
  },
});

// Breadth: every fixture with bytes, through the public API (parse, parseWithDebug, serialize round trip, update)
// and the retained-layout core path (parse + JSON projection), a few dozen iterations each so the interpreter's
// tiering has settled on the hot methods.
let operations = 0;
for (const entry of manifest().fixtures) {
  const { document, bytes } = loadFixture(entry.id);
  if (!bytes || bytes.length > 4 * 1024 * 1024 || document.expectedError) continue;
  const options = {
    pointerSize: document.options.pointerSize,
    aligned: document.options.aligned,
    littleEndian: document.options.littleEndian,
    rootTypeName: document.root,
    ...(document.readOptions?.addressingMode ? { addressingMode: document.readOptions.addressingMode } : {}),
    ...(document.readOptions?.maxArrayElements ? { maxArrayElements: document.readOptions.maxArrayElements } : {}),
    ...(document.readOptions?.maxTotalBytesRead ? { maxTotalBytesRead: document.readOptions.maxTotalBytesRead } : {}),
  };
  const iterations = bytes.length > 65536 ? 3 : 40;
  let parsable = true;
  for (let i = 0; i < (coreOnly ? 1 : iterations) && parsable; i++) {
    const parsed = await env.api.parse(document.definition, bytes, options);
    operations++;
    parsable = parsed.Success;
    if (i === 0 && parsed.Success && !coreOnly) {
      const value = parsed.Data[document.root];
      const written = await env.api.serialize(document.definition, value, options);
      if (written.Success) {
        await env.api.parseWithDebug(document.definition, written.Data, options);
      }
      operations += 2;
    }
  }
  // Fixtures whose read limits exceed the bridge's bounds (the 1 MiB uint8 array) only go through the public path.
  if (!parsable || (options.maxArrayElements ?? 0) > 1_000_000) continue;
  env.managed.BenchCompile(document.definition, JSON.stringify(options));
  for (let i = 0; i < iterations; i++) {
    env.managed.BenchParseRetain(bytes, document.root, JSON.stringify(options));
    operations++;
    if (!coreOnly) {
      env.managed.BenchProjectRetained();
      operations++;
    }
  }
}

env.managed.BenchStopAotProfile();
const data = env.managed.BenchTakeAotProfile();
if (!(data instanceof Uint8Array) || data.length === 0) {
  throw new Error("No AOT profile was received; was the bundle built with WasmProfilers=aot?");
}
fs.mkdirSync(path.dirname(output), { recursive: true });
fs.writeFileSync(output, data);
console.log(`Recorded ${data.length} bytes of AOT profile after ${operations} operations to ${output}`);
process.exit(0);
