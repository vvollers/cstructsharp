# Performance measurement

`CStructSharp.Benchmarks/` contains BenchmarkDotNet timing/allocation cases that reference core. Baselines live under
`contracts/performance/`; benchmark output is ignored. Performance checks are maintained manual measurements plus a
non-failing drift report in CI (`.github/workflows/benchmark-drift.yml`), not an automatic timing gate on every push.

## Layout

| Path | Purpose |
| --- | --- |
| `CStructSharp.Benchmarks/` | BenchmarkDotNet host. Original release-gate cases (`ReleaseGate` category) plus the Phase 0 `Baseline0/` scenario-matrix cases and hand-written comparators. `--profile <scenario>` runs a manual loop for sampling profilers. |
| `CStructSharp.FixtureTool/` | Fills and verifies `fixtures/` expectations with the managed library; also the shared fixture loader the benchmarks use. |
| `fixtures/` | Seeded fixture corpus shared by .NET, Node, and browser harnesses (see its README). |
| `js/` | Node + headless-Chromium harness for the WASM bridge (see its README). |
| `profiling/` | `perf` and Chrome DevTools Protocol profiling scripts. |

## Run the .NET benchmarks

```sh
dotnet build ./CStructSharp.NonWeb.sln -c Release
# Phase 0 scenario matrix, both target frameworks, Short job (1 launch, 3 warmups, 5 iterations):
CSTRUCTSHARP_BENCHMARK_JOB=Short CSTRUCTSHARP_BENCHMARK_RUNTIMES=net10.0,net8.0 \
  dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 --no-build -- --filter '*Baseline0*'
# Release-gate cases, Gate job (3 launches, 5 warmups, 8 iterations), net10.0 only:
CSTRUCTSHARP_BENCHMARK_JOB=Gate dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 --no-build -- \
  --filter '*' --anyCategories ReleaseGate
# Cold start (5 fresh processes, one measured call each):
CSTRUCTSHARP_BENCHMARK_JOB=ColdStart dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 --no-build -- \
  --filter '*Baseline0.CompileBenchmarks*'
```

Environment variables: `CSTRUCTSHARP_BENCHMARK_JOB` = `Dry` | `Short` | `Gate` | `ColdStart`;
`CSTRUCTSHARP_BENCHMARK_RUNTIMES` = comma list of `net8.0`, `net10.0` (default `net10.0`);
`CSTRUCTSHARP_BENCHMARK_ARTIFACTS` = output directory (default `artifacts/baseline/benchmarks`);
`CSTRUCTSHARP_BENCHMARK_PROFILE=cpu` adds the EventPipe CPU-sampling diagnoser (writes `.nettrace` per case).

Normalize and compare:

```sh
node tools/quality/convert-benchmark-baseline.mjs <results dir or report-full.json> artifacts/summary.json
node tools/quality/compare-benchmark-baseline.mjs --baseline contracts/performance/non-web-rc2.json --summary artifacts/summary.json
node tools/quality/non-web-release-budgets.mjs --benchmark-summary-path artifacts/summary.json   # rc1 hard gate
```

`convert-benchmark-baseline.mjs` is the only converter.

## Memory analysis workloads

`MemoryAnalysisBenchmarks` measures cross-page selected reads, cached reads, ISF import, bounded traversal,
4,096 stored-pointer links, a selected field in a sparse one-million-byte record, and mapped offline updates.
Run `--filter '*MemoryAnalysisBenchmarks*'` with the same Release job and runtime
settings shown above. The synthetic consumer at `docs/examples/memory-analysis` also checks source-request
budgets, physical fragments, and preservation of the original image. Its sources do not depend on real captures.

For a before/after comparison, preserve a separate checkout and its Release binaries before editing code.
Run both checkouts repeatedly on the same machine with identical filters, runtime, input data, and job settings.
Keep separate artifact directories using `CSTRUCTSHARP_BENCHMARK_ARTIFACTS`. Compare allocations as well as timing;
do not run builds, tests, or mutation analysis while benchmarks are measuring.

## Profiling

```sh
benchmarks/profiling/profile-dotnet.sh ParsePrimitiveArray1KiB 10 artifacts/profiles     # Linux perf, inclusive frames
#   scenarios: CompileSmall, CompileMedium, ParsePrimitiveArray1KiB, ParseNestedUnaligned, SerializeNested256, ReadTypedNested256, ParseCondIf128, ParseDynamic1024,
#   SerializePocoToSpan, ParseRealPng, ParseArrayU32Be (benchmarks/CStructSharp.Benchmarks/ProfileDriver.cs)
node benchmarks/js/bench/profile-browser.mjs real-png 2000 artifacts/profiles            # Chromium CDP CPU profile
```

## Anti-benchmarking rules

The `Baseline0.ComparatorBenchmarks.HandWritten_*` cases exercise no library code, so they are the canary for a
recording run: if any of them drifts more than 10 % against the contract, the machine was perturbed during the run
(this happens on shared VMs) — discard the run and repeat it rather than re-recording from it.


Release builds only; record `dotnet --info`/`process.versions`/CPU in every result (the converters and JS harness do
this); warm up before measuring; never compare means alone — the contracts store medians, allocations, and RSD, and a
case with RSD above 0.35 is reported as unstable instead of gated. Run nothing else on the machine while measuring.
