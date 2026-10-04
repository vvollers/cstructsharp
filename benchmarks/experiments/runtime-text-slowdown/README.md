# Runtime text serialization: tiering and staging

This investigation starts at committed `2aa33f54` with the uncommitted text-writing optimization from
[text-write-next](../text-write-next/README.md). The generated writer and all earlier artifacts remain intact.
The only product change in this follow-up extracts bounded stack staging from `WriteText` into
`WriteSmallBoundedText`. Measurements and validation were completed before committing the combined change.

## Baselines and ranked hypotheses

- **Original:** unchanged `2aa33f54` product, with the identical supplementary benchmark cases in immutable
  `artifacts/perf/development/bundles/text-write-next-cases-baseline`.
- **Start:** the uncommitted stack-only optimization, captured before editing as
  `artifacts/perf/development/bundles/runtime-text-slowdown-start`.
- **Final:** the same optimization with the bounded staging helper. Existing generated writing is byte-identical
  to Start. All 92 Impact cases and their inputs remain unchanged.

| Rank | Hypothesis and evidence before prototyping | Smallest experiment | Risk / expected value |
| --- | --- | --- | --- |
| 1 | Tiering changes execution state: an earlier production launch fell from roughly 51 to 38 microseconds during measurement | Sustained writes with JIT counters and separate production disassembly; identical-code control | No product risk; distinguish startup transitions from settled work |
| 2 | Shared `WriteText` stack allocation changes its frame and compilation policy; diagnostic code already had a larger frame and guard | Extract only bounded staging and inspect actual tiers, cold cost and timing | One private helper; preserve validation and destination calls |
| 3 | Encoding overloads, helper calls and retained copies offset allocation savings | Inspect production calls and compare padded/unpadded and large-fallback cases | Avoid replacing strict encoding or changing failure order without evidence |
| 4 | GC or external activity explains between-process variation | Record allocations, pause duration and process CPU; run identical-code pairs | Affinity and counters do not isolate the host or prove all outliers harmless |

The runtime fixture has a dynamic record count and complete `StructValue` input. `WriteEngine` normalizes the
root, executes compiled write instructions, loads each member, converts non-string character values if needed,
then calls `WriteText`. It does not walk a syntax tree. The extraction changes none of that work. Owned arrays
and caller spans use `MemoryWriteDestination`; buffer writers and physical stream calls use
`StreamWriteDestination`. Async output still serializes the complete owned array before its existing async write.

## Supported cause and retained fix

`WriteText` contains a loop for its narrow-character fallback. The starting optimization added `localloc`
(the IL operation emitted for `stackalloc`) to the same method, even when the actual field takes another branch.
The .NET JIT scans the whole method before executing a branch.

The [.NET 10 JIT policy](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/jit/compiler.cpp) switches loop
methods to eager optimization when they cannot use on-stack replacement. On-stack replacement means switching
an already-running method to optimized code; its
[eligibility check](https://github.com/dotnet/runtime/blob/v10.0.0/src/coreclr/jit/compiler.hpp) rejects `localloc`.
These published sources explain the mechanism; the installed .NET 10.0.12 disassembly verifies its effect here:

| Version | Observed memory-writer compilation |
| --- | --- |
| Original | Instrumented Tier0, then Tier1 |
| Start | `Tier0-FullOpts`, no collected PGO data for `WriteText`; no later Tier1 version observed |
| Final | Instrumented Tier0, then Tier1; bounded staging tiers separately |

A diagnostic counterfactual disables OSR for **unchanged Start code**. `WriteText` then reaches Tier1. This is
preserved in `osr-disabled-control/`; it is neither the proposed fix nor a production timing configuration.
All ordinary measurements retain default OSR behavior and explicitly enable tiering/PGO for production.

The retained helper receives the already-validated encoding, immutable string and encoded byte count. It performs
the identical stack allocation, encoding and payload write. The caller still handles capacity/encoding validation,
the large allocating fallback and the separate padding write. Empty-payload callbacks, whole wide-field writes,
stream failures, budgets and malformed-wide fallback remain unchanged. No pool, cache, backend or runtime setting
is added. The generated writer is preserved.

This identifies and largely fixes the first-use regression. It does **not** identify the cause of every older slow
launch. In particular, the historical 51-microsecond production plateau is not retrospectively labelled noise.

## Measurement protocol

Windows 11, Ryzen 9950X, .NET 10.0.12, SDK 10.0.401 selected through `global.json`, Node 26.5.0, logical CPU 16.
Runs were serial, with no concurrent builds/tests or subagent work. Affinity does not isolate the CPU, its sibling,
frequency changes or other applications. No canary correction or favorable-launch selection was used.

The existing BenchmarkDotNet production tool was reused with 30 warmups and 15 requested 250 ms measurements.
Its invocation count is calibrated before later tier transitions. Actual text iterations sometimes lasted only
60–100 ms, so 30 warmups do not guarantee 7.5 seconds of warmed execution. Its inconsistent results are retained.

The additional `RuntimeTextSlowdown` probe reuses `TextRecordBenchmarks.CreateInput` and `TextRecordLayout`.
Each operation writes a complete owned array or complete caller span. It validates all output bytes outside every
250 ms window, verifies owned-array independence, and records every window, JIT work, allocation, GC pauses and
process CPU. It does not substitute checksums or partial results. Runtime/tiny inputs contain the original padding;
the span workload uses the same fixture parsed with `TrimFixedText`, as `TextOutputBenchmarks` does.

Each clean comparison uses three fresh pairs with alternating order. Record batches run for 20 seconds per process;
span/tiny cases run for 10 seconds. Whole-run medians retain startup windows; the second-half summaries were
specified before measurement and are reported separately. Initial JIT-heavy windows are preserved, not discarded.
Profiling/disassembly runs are separate and are not used as clean throughput measurements.

## Production results

Sustained probe, nanoseconds per complete operation. The delta is the median paired ratio, not necessarily the
ratio of the displayed side medians. Ranges are observed launch variation, not confidence intervals.

| Comparison | Before | Final | Paired delta | Pair range |
| --- | ---: | ---: | ---: | --- |
| Identical Start code A/A, 256 records | 27,833.92 | 27,841.02 | -0.2% | -0.8…+1.6% |
| Original → Final, 256 records | 30,274.42 | 25,832.04 | -14.7% | -15.3…-13.6% |
| Start → Final, 256 records | 27,550.10 | 26,121.46 | -5.4% | -5.4…-4.6% |
| Original → Final, unpadded caller span | 32,887.75 | 25,069.10 | -23.9% | -24.5…-22.1% |
| Original → Final, one record | 210.29 | 198.25 | -6.6% | -12.1…+3.2%; inconclusive |

Second-half launch ratios support the batch directions: Original → Final -15.4…-13.7%, Start → Final
-5.5…-4.6%, span -24.4…-22.3%. A/A is -0.8…+1.7%. Tiny records remain contradictory (-11.9…+3.2%).
These observations support the repeated-record improvement on this configuration, not a universal percentage.

The reused out-of-process BDN comparison of **Start → Final** remains inconclusive:

| Case | Before ns | Final ns | Paired delta | Pair range / spread |
| --- | ---: | ---: | ---: | --- |
| One record | 210.82 | 197.81 | -0.4% | -8.0…+53.2% / 81.0% |
| 256 records | 27,104.57 | 26,877.48 | -0.8% | -7.2…+3.3% / 7.0% |
| Numeric canary | 2.19 | 2.20 | -0.1% | -4.7…+2.7% / 8.6% |

The canary drifts; no CPU-speed claim comes from those medians. The first attempted BDN comparison failed because
the main checkout contains older nested worktrees with matching project names. Its completed unpaired Start
launch and failed candidate logs remain in `production-helper/`. `production-helper-isolated/` uses a separate
managed Final worktree and contains all six successful hosts. No old worktree was modified to fix discovery.

## Diagnostic timing and allocations

Tiering/PGO disabled, standard perf-check Confirm, three pairs, thirty 100 ms measurements per method.
Original → Final uses fresh measurements from `runtime-text-slowdown-original-confirm`:

| Runtime serialization | Before ns | Final ns | Paired delta | Pair range / spread |
| --- | ---: | ---: | ---: | --- |
| One padded record | 343.77 | 309.14 | -10.2% | -12.5…-8.3% / 7.7%; unstable |
| 256 padded records | 38,629.57 | 33,215.88 | -14.6% | -14.7…-12.5% / 2.6% |
| Unpadded caller span | 41,547.41 | 32,392.33 | -22.0% | -25.5…-21.1% / 3.6% |
| Buffer writer | 51,410.62 | 42,242.09 | -17.3% | -19.6…-4.4% / 17.2%; unstable |
| Physical writes to memory stream | 48,672.61 | 39,517.33 | -18.6% | -22.3…-3.1% / 21.4%; unstable |
| Async memory sink | 43,209.48 | 34,214.41 | -20.8% | -24.4…-19.9% / 3.7% |
| Mixed 4 KiB text, large fallback | 2,693.48 | 2,717.76 | +0.9% | -0.4…+1.5% / 10.7%; unstable |

The separate **Start → Final** confirmation is contrary/inconclusive evidence: one record 313.82 → 317.54 ns,
paired +5.6% (-3.9…+6.3%, 8.0% spread); 256 records 32,845.12 → 33,782.29 ns, +2.9%
(-7.9…+10.5%, 10.5% spread). No diagnostic-only gain is claimed for the helper extraction. These are distinct
campaigns; their different Final medians must not be combined by selecting the faster one.

Exact warmed allocation counts from Screen and the sustained probe are unchanged by the extraction:

| Operation | Original bytes/op | Start / Final bytes/op |
| --- | ---: | ---: |
| Runtime owned array, 1 record | 296 | 128 |
| Runtime owned array, 256 records | 68,640 | 25,632 |
| Unpadded caller span | 67,584 | 0 |
| Buffer writer | 67,720 | 136 |
| Memory stream | 67,648 | 64 |
| Async memory sink | 93,216 | 25,632 |
| Runtime mixed 4 KiB text | 7,800 | 7,800 |
| Generated owned array, 256 records | 39,968 | 25,632 |

Long BDN runs include small amortized harness allocations (for example 68,643 → 25,634 bytes); every raw count
is retained. The numeric handcrafted case is a canary, not an equivalent text serializer. No handcrafted speed
ratio is claimed. Generated throughput was not re-confirmed in production this round; its implementation is unchanged.

The sustained batch probe records about 43 ms of GC pause over each original 20-second run and 15 ms for Final.
Span runs record about 26 ms before and zero afterward. These are observations of this probe, not a prediction of
application pause latency or disk throughput. CPU ratios near 0.99 do not rule out frequency/cache effects.

## First use, compilation and code size

The unchanged `OrdinaryNext` probe supplies 180 fresh-process observations: ten rounds, three versions, two
tiering settings, and compile/read-256/write-256 modes. Schema preparation stays outside first-write timing.

| First write of 256 records | Original | Start | Final |
| --- | --- | --- | --- |
| Production ms, median (range) | 11.222 (11.184–11.319) | 12.234 (12.200–12.287) | 11.398 (11.294–11.476) |
| Production incremental JIT ms | 10.509 | 11.573 | 10.717 |
| Production compiled IL bytes | 13,619 | 13,765 | 13,790 |
| Production allocated bytes | 146,784 | 103,776 | 103,776 |
| Diagnostic ms, median (range) | 29.850 (29.775–30.529) | 30.151 (30.088–30.899) | 30.389 (30.299–30.913) |

The extraction removes 0.837 ms, about 83% of the added production first-write penalty. Final remains about
0.176 ms above Original. Diagnostic first-write cost increases another 0.238 ms versus Start: separating methods
does not remove compilation work when the runtime optimizes everything immediately. This is a process first-use
measurement, not a per-schema cost or a portable break-even threshold.

Production compile medians are 63.276 / 63.245 / 63.383 ms; first reads 29.321 / 29.255 / 29.210 ms. Their ranges
overlap. Diagnostic compile medians are 153.170 / 153.056 / 152.922 ms and reads 61.760 / 61.878 / 61.891 ms.
One Final diagnostic read took 83.326 ms and is retained. Cold production read allocation alternates between
114,768 and 127,104 bytes in **all three versions**; diagnostic reads allocate 126,976 in all three. No parsing or
schema-memory change is inferred. Warmed runtime/generated record-read allocations remain 86,192 / 63,544 bytes.

The Release library is 900,608 bytes for both Start and Final (Original 900,096). Production memory-writer code
is 1,852 bytes for Start `WriteText`, versus 1,392 for Final plus 861 for its new helper. The existing wide helper
and validated encoder remain 780 and 437 bytes in the selected dumps. Selected settled code therefore grows
401 bytes; additional tier versions are also emitted. This is not total retained native memory. Generated source
emission is unchanged. There is no per-schema code/cache growth, new pool retention or additional managed staging
allocation; staging remains bounded to 512 bytes per active helper. Peak working set and native code reclamation
were not measured.

## Validation, rejected alternatives and limits

- Release solution build passed with zero warnings/errors; `release-build.log`.
- Full managed suite: **8,818 passed**, including 4,326 runtime tests per framework, 47 parity tests per framework,
  70 generator tests and two compiler tests. `artifacts/test-results/development/run-b3bfZ0/`.
- Six added reentry/failure cases cover empty payloads and 511/513-byte boundaries, without altering any existing
  assertion. All 24 focused cases pass on untouched Original and Final on both .NET 8/.NET 10. Original results
  are in `reference-tests/`; candidate focused results are `artifacts/test-results/development/run-9XzEv1/`.
- The 7,128 original failure/callback outcomes remain unchanged. No golden file or generated-source snapshot was
  refreshed. The read scratch-storage assertion above 256 code units remains intact.
- Managed API revision 62, canonical reference, all 48 feature-operation pairs, 14 generated-layout checks,
  changed-declaration documentation (10 C# and five script files), local Markdown links and `git diff --check`
  pass. Logs are `check-1.log` through `check-6.log`. No authored DocFX, app or public-contract source changed.
- The complete 92-case Impact screen ran once. Confirmation did not consistently reproduce the flagged text/PNG
  parsing, mapped access, primitive update or dictionary-writing slowdowns. That confirmation still has canary
  drift and remains inconclusive. Unchanged async-file reads and large-array cases have high variation; no gain
  or regression is inferred from them. No broad suite was rerun to obtain favorable medians.

Only one product prototype was needed. A larger fixed stack buffer or skipped initialization would leave the
method's IL `localloc` in place and would not address the observed JIT policy. Direct memory encoding, custom
UTF-16 loops and new prepared execution blocks would add more behavioral risk than this fix warrants; they were
not implemented or assigned performance claims. The previously rejected pooled variant remains rejected.

The retained change is recommended for merging: it fixes a demonstrated compilation-policy problem with one
private helper, retains allocation savings and improves sustained batch writes in fresh comparisons. It is not
a claim that every production workload is faster. Tiny-record CPU results and the older 51-microsecond plateau
remain unresolved. Further investigation of those cases should capture the actual slow process and its tiered
code/profile, rather than repeatedly launching the same short comparison. .NET 8 performance, ARM, NativeAOT,
multicore scaling, real disk/network latency and generated first-use latency were not measured.

## Reproduction and artifacts

All new timing/profile artifacts are under `artifacts/perf/runtime-text-slowdown/`; the aggregate numeric index is
`summary.json`. Diagnostic reports are under `artifacts/perf/development/runs/runtime-text-slowdown-*`.
The original and starting worktrees remain in place; the separate Final worktree is also retained.
`source-and-binary-checks.json` records hashes and verifies identical benchmark/probe source in all four checkouts,
unchanged Original product source, preserved generated writing, and matching main/isolated Final product source.
Use fresh capture/output names when repeating these commands; tools refuse to overwrite prior evidence.

```powershell
$original = 'C:/Users/vmvol/.codex/worktrees/text-write-next-reference/cstructsharp'
$start = 'C:/Users/vmvol/.codex/worktrees/text-write-next-candidate/cstructsharp'
$final = 'C:/Users/vmvol/.codex/worktrees/runtime-text-slowdown-final/cstructsharp'
# node denotes the pinned .node-version executable; on this host:
$node = "$env:TEMP/cstructsharp-node-26.5.0/node-v26.5.0-win-x64/node.exe"
& $node tools/quality/perf-check.mjs --capture runtime-text-slowdown-start

# Copy the identical probe sources into each checkout, without changing reference product code; then build serially.
foreach ($tree in @($original, $start, $final)) {
  dotnet build "$tree/benchmarks/experiments/runtime-text-slowdown/RuntimeTextSlowdown.csproj" -c Release
  dotnet build "$tree/benchmarks/experiments/ordinary-next/OrdinaryNext.csproj" -c Release
}
& $node benchmarks/experiments/runtime-text-slowdown/sustained.mjs $original $start artifacts/perf/runtime-text-slowdown/profile-start runtime 1 20 disasm
& $node benchmarks/experiments/runtime-text-slowdown/sustained.mjs $start C:/projects/struct/cstructsharp artifacts/perf/runtime-text-slowdown/profile-helper runtime 1 20 disasm
& $node benchmarks/experiments/runtime-text-slowdown/sustained.mjs $start $start artifacts/perf/runtime-text-slowdown/aa-start runtime 3 20
& $node benchmarks/experiments/runtime-text-slowdown/sustained.mjs $original $final artifacts/perf/runtime-text-slowdown/sustained-final runtime 3 20
& $node benchmarks/experiments/runtime-text-slowdown/sustained.mjs $start $final artifacts/perf/runtime-text-slowdown/sustained-start-final runtime 3 20
& $node benchmarks/experiments/runtime-text-slowdown/sustained.mjs $original $final artifacts/perf/runtime-text-slowdown/sustained-span span 3 10
& $node benchmarks/experiments/runtime-text-slowdown/sustained.mjs $original $final artifacts/perf/runtime-text-slowdown/sustained-tiny tiny 3 10
& $node benchmarks/experiments/runtime-text-slowdown/cold.mjs $original $start $final artifacts/perf/runtime-text-slowdown/cold-three-way
$env:PERF_WARMUPS = '30'
& $node benchmarks/experiments/runtime-generated/production.mjs $start $final artifacts/perf/runtime-text-slowdown/production-helper-isolated '*TextRecordBenchmarks.Runtime_Serialize*' '*GeneratedBenchmarks.HandWritten_PrimRecord*'
Remove-Item Env:PERF_WARMUPS

& $node tools/quality/perf-check.mjs --baseline runtime-text-slowdown-start --rounds 3 --cpu 16 --label runtime-text-slowdown-helper-screen --filter '*TextRecordBenchmarks.*Serialize*' --filter '*TextOutputBenchmarks*' --filter '*MaterializationBenchmarks.*Text_Serialize*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'
& $node tools/quality/perf-check.mjs --baseline text-write-next-cases-baseline --confirm --cpu 16 --label runtime-text-slowdown-original-confirm --filter '*TextRecordBenchmarks.Runtime_Serialize*' --filter '*TextOutputBenchmarks*' --filter '*MaterializationBenchmarks.Runtime_Text_Serialize*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'
& $node tools/quality/perf-check.mjs --baseline runtime-text-slowdown-start --confirm --cpu 16 --label runtime-text-slowdown-start-confirm --filter '*TextRecordBenchmarks.Runtime_Serialize*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'
& $node tools/quality/perf-check.mjs --baseline text-write-next-cases-baseline --cpu 16 --label runtime-text-slowdown-impact
& $node tools/quality/perf-check.mjs --baseline text-write-next-cases-baseline --confirm --cpu 16 --label runtime-text-slowdown-impact-confirm --filter '*MaterializationBenchmarks.*Text*Parse*' --filter '*PacketBenchmarks.ReadValueMapped*' --filter '*WriteBenchmarks.Serialize_Prim_Dictionary_ToArray*' --filter '*ImpactParseBenchmarks.ParseSpan*real-png*' --filter '*GeneratedBenchmarks.Runtime_PrimRecord_Update*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'

dotnet build CStructSharp.NonWeb.slnf -c Release
& $node tools/quality/test-managed.mjs --full --no-build
& $node tools/quality/managed-api-baseline.mjs compare
& $node tools/documentation/validate-canonical-reference.mjs
& $node tools/quality/feature-operation-matrix.mjs
& $node --test tools/quality/benchmark-generated-layouts.test.mjs
& $node tools/quality/changed-documentation.mjs --base HEAD --language csharp
& $node tools/quality/changed-documentation.mjs --base HEAD --language script
git diff --check
```

The diagnostic counterfactual uses the unchanged Start probe with these process environment variables:
`DOTNET_TieredCompilation=1`, `DOTNET_TieredPGO=1`, `DOTNET_TC_OnStackReplacement=0`,
`DOTNET_JitDisasm=WriteText`, `DOTNET_JitStdOutFile=<new-output>/WriteText.asm`, and
`CSTRUCTSHARP_FIXTURES=<original>/benchmarks/fixtures`. Invoke
`dotnet <start>/benchmarks/experiments/runtime-text-slowdown/bin/Release/net10.0/RuntimeTextSlowdown.dll runtime 5`
and retain stdout as `profile.json`. Clear those overrides before clean measurements. Only its compilation tiers
are evidence here; its timings are not compared with default-configuration runs.

The initial probe build failures (platform guard and a missing blank line) are preserved alongside the corrected
build logs. They were harness compilation failures before any measurements, not product or behavioral failures.
