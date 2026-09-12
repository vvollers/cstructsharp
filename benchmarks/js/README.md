# JavaScript/WASM benchmark harness

Phase 0 measurement harness for the WASM bridge (see `agentdocs/performance-improvement-plan.md` §4 and §11).
Everything here is benchmark-only; nothing is shipped in the npm package or the release bundle.

## One-time setup

```sh
cd benchmarks/js
npm ci                      # tinybench + playwright (pinned)
npx playwright install chromium
npm run build:wasm          # dotnet publish with wasm/Benchmark.targets → artifacts/js-bench/bundle
```

`build:wasm` needs the `wasm-tools` workload. Extra MSBuild properties pass through, e.g.
`node build-wasm.mjs -p:RunAOTCompilation=true` for the AOT experiment (E3.1). For profiling build a second bundle with
`node build-wasm.mjs --bundle bundle-symbols -p:WasmEmitSymbolMap=true` (the symbol map is loaded by the runtime at
startup, so it must never be inside the timed bundle). The bundle manifest
(`artifacts/js-bench/bundle-manifest.json`) records every framework file's SHA-256 and the properties used.

Fixtures come from `benchmarks/fixtures/` (run `node benchmarks/fixtures/generate-fixtures.mjs` and the
`CStructSharp.FixtureTool fill` step first if they are missing).

## Running

| Command | What it measures | Output |
| --- | --- | --- |
| `npm run bench:node` | boundary micro-cases, retained-layout core parse / JSON projection, public API (`parse`, `parseWithDebug`, `serialize`, `update`), compiled handle, stream sources, hand-written `DataView` comparators | `artifacts/js-bench/results/node-<stamp>.json`, `node-latest.json` |
| `npm run bench:cold` | fresh Node process per sample: runtime create, exports, first compile, first core parse, first public parse | `cold-<stamp>.json`, `cold-latest.json` |
| `npm run bench:browser` | the same case groups in headless Chromium (Playwright) plus cold start from fresh contexts | `browser-<stamp>.json`, `browser-latest.json` |
| `npm run check` | soft drift report of `*-latest.json` against `contracts/performance/web-benchmark-rc1.json` | markdown on stdout |

Environment variables: `BENCH_FILTER=<regex>` selects cases, `BENCH_QUICK=1` shortens warm-up/batches for smoke
runs (never for baselines), `BENCH_COLD_LAUNCHES`, `BENCH_COLD_FIXTURES`, and `BENCH_BUNDLE=<name>` selects an
alternative staged bundle under `artifacts/js-bench/` (for example `bundle-aot` from
`node build-wasm.mjs --bundle bundle-aot -p:RunAOTCompilation=true`).

`core.compile.*` measures the bridge's compile path, which hits the process-wide layout cache after the first call;
`core.compileFresh.*` bypasses the cache and measures an actual compilation.

## Method

- Every run first verifies that the public JS `parse` reproduces the C# expected JSON (or its SHA-256) for a set of
  fixtures, so timing never runs on a build that disagrees with the managed library.
- Batched timing: warm-up ≥ 600 ms, batch calibrated to ~100 ms, 9 batches; the reported median/RSD are over the
  per-batch ns/op samples. Cases with a median ≥ 50 µs additionally record per-call p50/p95/p99 via tinybench.
- Managed allocation per op comes from `GC.GetTotalAllocatedBytes` through a benchmark-only export, measured
  outside the timed phase. It is `0` for cases whose work happens in a worker (the public API), by design.
- The copy counter reports page reads and bytes copied through the JS-backed source for calls made on the
  harness thread (`direct.*`, `boundary.pageRead.*`); worker-internal copies are not visible to it.
- A dead-code sink consumes every result; RSD > 0.35 flags a case `UNSTABLE` and the check script treats it as
  non-gating.
