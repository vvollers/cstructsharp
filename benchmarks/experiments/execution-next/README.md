# Numeric sections in dynamic generated readers

**Status (2026-10-04): removed at the user's request.** The numeric-section implementation, dedicated tests and
Impact cases are no longer in the product or benchmark suite. The ~0.3 ms production first-use penalty and narrow
workload applicability did not justify retaining it for the intended ordinary workloads. The results below are
historical evidence, not a description of current product behavior. Sources and probe tooling are preserved under
`artifacts/perf/execution-next/removed-numeric-sections/`; the isolated measured checkouts and all timing artifacts
remain intact. The independent runtime matrix ownership regression remains useful and is retained.

The previous round was committed and pushed as `81ddaab0`. This investigation uses that revision as its new
baseline. `execution-next-baseline` was captured before hot-path edits. `execution-next-cases-baseline` adds only
the six new benchmark cases to an untouched reference checkout. The original capture remains immutable.
Earlier measurements are context, not this round's baseline. Existing local investigations and artifacts remain intact.

## Ranked opportunities and decisions

| Rank | Mechanism and workloads | Evidence, expected benefit and effort | Risks | Smallest experiment and decision |
| --- | --- | --- | --- | --- |
| 1 | Runtime boxed numeric blocks: decode source spans without the pooled copy | `PrimitiveArrayReader.ReadInto` still copies 64 KiB blocks; typed `ReadBlocks` already borrows spans internally. Small implementation, likely small benefit beside boxing | Block failure consumption, cancellation, stream callbacks and owned rows | Isolated span/fallback prototype on existing small, large, BE and stream matrices. Rejected: no allocation reduction or convincing timing benefit |
| 2 | Generated parsing: group substantial numeric sections around dynamic fields | `EmitFields` repeats `Take` checks for every scalar even when placement is contiguous. Moderate potential for tens of scalar fields; one small emitter helper | Construction, publishing values used by later counts, placement, byte charges, failures and code growth | Two 64-byte sections separated by a variable payload. Retained with conservative eligibility and unchanged fallback |
| 3 | Generated serialization: group the same sections | The incremental writer also repeats reservations; scalar encoding can fail, and borrowed output can alias later array input | Partial writes, range validation, mutation visibility, output clearing and access order | Deferred. First restrict a separate prototype to infallible built-in scalars; compare owned output, overlapping spans and buffer-writer call traces |
| 4 | Runtime schema-specific executable blocks | Prepared plans still dispatch operations and construct boxed owned values; this is not syntax-tree interpretation. Potentially meaningful for repeated fixed numeric records, but high implementation cost | AOT fallback, compile/JIT break-even, concurrent initialization and bounded code/cache lifetime | Deferred architectural hypothesis. One whole numeric block returning the same owned `StructValue`, not another per-field delegate. Measure preparation C and warmed saving D before adding a backend |
| 5 | Buffered continuation | `BufferedInput.InitialLength` normally covers ordinary fixtures; sparse targets and small budgets can cause grow-and-rerun | Repeated codec/constructor calls and physical stream interactions are observable | Deferred. Count retries, copied bytes and discarded graphs with actual asynchronous I/O before designing continuation state |

Runtime serialization's eager normalization remains required for callback, snapshot and failure ordering. Its
previous capacity optimization is part of the new baseline. No additional runtime write change was justified here.

## Execution paths and retained scope

Runtime matrix benchmarks reach the general engine's numeric-list/table path and its block reader. Numeric 1D
arrays already have bulk typed decoding. Exposed memory uses the memory cursor; non-exposable streams exercise
the stream cursor. The rejected prototype changed only the boxed block reader and kept its original block sizes.
Its patch remains at `artifacts/perf/execution-next/rejected-block-copy.patch`; product code was restored exactly.

The retained generated shortcut runs inside `EmitFields`, after composite entry and construction. It groups
16–32 named built-in numeric scalars totaling at least 64 bytes in packed dynamic structs. Fixed composites,
unions, aligned layouts, bitfields/separators, enums, assertions, conditional members, pointers, arrays and custom
codecs are excluded from a section. Sections can follow dynamic arrays; their offsets are relative to the current
cursor, not assumed fixed offsets from the record's start. The containing placement exclusion is inspected once
per emission, without a persistent cache.

`TryTakeFixed(size, 1, size, 0, 0)` checks the complete extent and budget without entering another composite.
On success, ordinary numeric decoders assign properties in declaration order and placement advances to the final
position. Any rejected guard executes the original field emitter. No construction moves, no result is borrowed,
and no extra cancellation failure is introduced. Expression publication remains in declaration order. Writers
and shared fixed plans are unchanged; there is no second runtime execution backend or new public API.

The focused benchmark models two 64-byte scalar sections, a payload of zero or 65 bytes, and a three-byte tail.
Each section publishes a count consumed by its following array. The odd payload makes trailer loads unaligned;
the trailer also includes BE fields. Setup compares all scalar values, both owned payloads and serialization bytes
with the runtime outside timing. Each timed read returns the complete owned record. The stream case includes a
new non-exposable `MemoryStream` and the existing generated buffering adapter. It is not physical disk or genuinely
asynchronous I/O. Serialization of the same record is an opposite-direction control.

## Timing, allocations and uncertainty

All timing ran serially on logical CPU 16, with agent work, builds, tests and source edits stopped during measurement.
Affinity does not isolate the CPU. Environment: Windows 11, Ryzen 9 9950X, SDK 10.0.401 selected by `global.json`,
.NET 10.0.12 and pinned Node 26.5.0. Diagnostic jobs disable tiering/PGO; production jobs enable both, with
30 warmups and 15 measured 250 ms iterations in separate processes. Every launch, raw sample and report is retained.

The initial matrix A/A Screen was unstable: identical-code large LE parse pair deltas ranged -5.3…+20.0%.
The block-copy prototype showed +0.1% median paired time for that case (-1.8…+10.7%), -3.0% for the small case
(-13.7…+7.7%), and unchanged allocation everywhere. Its BE direction was favorable (-23.1…-3.8%), but did not
justify extending this weak prototype given matrix variability, no memory reduction and unchanged stream copying.
It was removed without repeated tuning or confirmation searches for a favorable launch.

The generated-section Screen favored both parsing cases in every pair. A subsequent identical-code section Screen
had median parse deltas +2.7% and +0.4%, with pair ranges -1.9…+19.2% and -1.3…+17.0%. Its serialization control
also produced an apparent +9.4% slowdown with identical code. These are evidence of environmental variation.

Times below are medians of launch medians, in ns/op. Deltas summarize paired launch ratios; their median need not
equal the ratio of the displayed medians. There are three fresh launches per side for each configuration.

| Generated operation | Diagnostic before → after | Paired delta (all pairs) | Spread | Production before → after | Paired delta (all pairs) | Spread |
| --- | ---: | --- | ---: | ---: | --- | ---: |
| Parse, empty payload | 81.02 → 60.08 | -26.2% (-27.6…+16.5%) | 37.4% | 33.16 → 22.94 | -30.8% (-32.6…-30.2%) | 45.0% |
| Parse, 65-byte payload | 91.42 → 66.10 | -27.7% (-27.8…-24.1%) | 36.4% | 37.10 → 26.25 | -29.2% (-31.3…-29.1%) | 5.7% |
| Stream parse, empty payload | 133.67 → 109.66 | -18.0% (-19.9…+36.8%) | 39.2% | Not measured | — | — |
| Stream parse, 65-byte payload | 131.72 → 113.20 | -12.5% (-21.7…-10.8%) | 55.1% | Not measured | — | — |
| Serialize, empty payload (control) | 175.77 → 177.94 | -0.5% (-5.0…+74.9%) | 41.2% | Not measured | — | — |
| Serialize, 65-byte payload (control) | 186.08 → 172.87 | -6.1% (-38.8…-0.7%) | 39.7% | Not measured | — | — |

**The tooling labels all these confirmation results inconclusive because of canary drift.** Diagnostic canary
pair deltas were -35.3…+30.8%, spread 75.8%; production -0.7…+3.3%, spread 12.0%. The consistent production parse
direction, diagnostic direction on the nonempty payload, removed repeated checks and smaller diagnostic native
body support retaining the bounded change. They do not establish precise or portable speedup percentages.
No canary correction or favorable-median selection is applied. No serialization speedup is claimed.

| Allocation, B/op | Before | After |
| --- | ---: | ---: |
| Generated parse, empty / 65-byte payload | 216 / 288 | 216 / 288 |
| Generated stream parse, empty / 65-byte payload | 280 / 352 | 280 / 352 |
| Generated serialize, empty / 65-byte payload | 160 / 224 | 160 / 224 |
| Runtime 256×256 matrix parse (unchanged control) | 2,113,744 | 2,113,744 |
| Runtime 256×256 matrix serialize (unchanged control) | 655,624 | 655,624 |

Independent warmed current-thread probes confirm generated parse and 65-byte serialization counts exactly.
Stream and empty-write counts repeat across all targeted BDN launches. The retained change removes checks, not
result allocations. There is no persistent plan/cache allocation. The handwritten primitive checksum is only a
drift canary: it does not validate or construct this complete owned result, so it is not a fair throughput comparator.
No gap to an equivalent handcrafted record reader is claimed.

## Cold start, compilation and code size

Ten fresh processes per side, mode and configuration produced 120 first-operation observations. Read probes do not
invoke the generated parser beforehand. Writer probes parse their input before timing the first serialization.
These are first-operation costs, not application startup times or generator compilation throughput.

| First operation | Diagnostic median ms (range), before → after | Production median ms (range), before → after |
| --- | --- | --- |
| Parse, empty payload | 8.166 (7.888–8.435) → 8.611 (8.453–8.910) | 3.273 (3.037–3.429) → 3.577 (3.389–3.762) |
| Parse, 65-byte payload | 8.201 (7.950–8.537) → 8.741 (8.328–9.027) | 3.198 (3.066–3.925) → 3.515 (3.352–3.789) |
| Serialize, 65-byte payload | 4.098 (4.079–4.164) → 4.082 (3.960–4.184) | 1.909 (1.902–1.942) → 1.877 (1.866–1.970) |

The production read JIT median rises 3.087 → 3.402 ms for the nonempty case. The observed extra first-use cost
is about 0.3 ms; dividing it by the approximately 11 ns warmed median saving gives an illustrative order of
30,000 reads to amortize it. Timing drift and overlapping cold ranges prevent a reliable call-count guarantee.
This tradeoff favors repeated reads rather than one-shot parsing. Writer cold differences are not claimed as gains.

For the focused layout, generated source grows 116,146 → 122,748 bytes (+6,602); `ReadRoot` IL grows
1,772 → 2,555 bytes (+783). Its diagnostic native body shrinks 8,428 → 7,607 bytes (-821), despite the larger IL.
This is one method/configuration, not total process native memory. `EncodeRoot` remains 2,018 IL bytes and its
source and following generated source are byte-identical. All other benchmark generated files are byte-identical.
A final generator-only cleanup moves the composite eligibility scan outside the member loop; all 49 measured
generated files remain byte-identical afterward (`generated-final-check.txt`). The broad screen was not repeated
for that output-preserving cleanup. Generator throughput, total retained native memory and multicore throughput
were not measured. There are no new runtime caches or dynamic-code/AOT dependencies.

## Complete Impact screen and follow-up

The combined **80-case Impact screen ran once**. It samples runtime/generated reads and writes, tiny records,
large arrays, nested/dynamic layouts, strings, byte order, bitfields, unions, pointers, typed/dynamic results,
compilation, paths, span/buffer-writer output, streams, async adapters and segmented input. Runtime matrix parse
was 322.890 → 324.960 µs (+0.6%); serialization 596.975 → 603.300 µs (+1.1%), both inconclusive. No runtime
performance improvement is claimed. The original tiny packet and previously optimized matrices/text remain controls.

The largest relevant flags included generated text (+27.6%), typed scalar reading (+10.5%) and runtime update
(+4.2%). One targeted three-pair confirmation found text -1.3% (-4.1…+0.6%), typed reading +2.5%
(-1.2…+4.3%), and update -2.3% (-2.3…+0.9%). Allocation returned to identical 10,432, 208 and 632 B/op respectively.
The canary's 10.7% spread still flagged drift. These results do not demonstrate a repeatable regression or prove
equivalence. Further repetition stopped. Small effects and environments not measured remain unresolved.

## Correctness and reproduction

The eight new generated parity cases pass against both candidate and unchanged reference product code on .NET 8
and .NET 10. They cover every truncation and byte-budget boundary, exact failure metadata, counts published by both
sections, all numeric codec families, byte order, owned arrays, constructor mutation/cancellation, nested records,
preceding union charges, promoted dynamic members, active/inactive conditional groups, and sync/async stream origins.
Eleven generator-driver cases check eligibility and the 32-scalar bound while compiling the resulting consumer.
The rejected block-copy experiment also leaves a useful matrix source-mutation ownership test. No existing test,
assertion, fixture, behavioral golden, generated-source snapshot or contract was weakened or refreshed.

Final validation:

- `dotnet build CStructSharp.NonWeb.slnf -c Release`: zero warnings/errors;
  `artifacts/perf/execution-next/release-build-delivery.log`.
- `node tools/quality/test-managed.mjs --full --no-build`: **8,771 passed** — 4,289 runtime tests on each runtime,
  55 parity tests on each runtime, 81 generator-driver tests and two newer-compiler tests;
  `artifacts/test-results/development/run-fxW40F/summary.json` and its original TRX/logs.
- Unchanged-reference equivalence: eight new parity cases on each runtime;
  reference checkout `artifacts/test-results/development/run-7AHLQZ/`.
- API comparison passed against unchanged managed revision 62. Canonical reference, the 48-fixture feature matrix,
  14 benchmark-layout checks and changed-declaration documentation checks passed.
- Independent semantic review found no concrete defect. Entire diff, local Markdown links and `git diff --check`
  reviewed/passed. Existing source snapshots pass unchanged. No authored `docs/` or app files changed.

Use pinned Node 26.5.0; on this host it is
`$env:TEMP/cstructsharp-node-26.5.0/node-v26.5.0-win-x64/node.exe`. Commands below abbreviate it as `node`.
Use fresh capture/run/output names when reproducing.

```powershell
node tools/quality/perf-check.mjs --capture execution-next-baseline
$reference = 'C:/Users/vmvol/.codex/worktrees/execution-next-reference/cstructsharp'
$candidate = 'C:/Users/vmvol/.codex/worktrees/execution-next-candidate/cstructsharp'
# Reference product stays at 81ddaab0. Copy only the identical new benchmark sources to it.
node tools/quality/perf-check.mjs --capture execution-next-cases-baseline --checkout $reference
$matrix = @('--filter', '*MaterializationBenchmarks.Runtime_Matrix*', '--filter', '*GeneratedBenchmarks.HandWritten_PrimRecord')
node tools/quality/perf-check.mjs --baseline execution-next-baseline --rounds 3 --cpu 16 @matrix --label execution-next-aa
# Apply the saved rejected-block-copy.patch only for this isolated screen; then remove it.
node tools/quality/perf-check.mjs --baseline execution-next-baseline --rounds 3 --cpu 16 @matrix --label execution-next-block-copy
$sections = @('--filter', '*NumericSectionBenchmarks*', '--filter', '*GeneratedBenchmarks.HandWritten_PrimRecord')
node tools/quality/perf-check.mjs --baseline execution-next-cases-baseline --rounds 3 --cpu 16 @sections --label execution-next-sections
node tools/quality/perf-check.mjs --baseline execution-next-cases-baseline --confirm --cpu 16 @sections --label execution-next-sections-confirm
node tools/quality/perf-check.mjs --baseline execution-next-cases-baseline --checkout $reference --rounds 3 --cpu 16 @sections --label execution-next-sections-aa

# Copy the identical ExecutionNext probe to both checkouts; only candidate receives product changes.
dotnet build "$reference/benchmarks/experiments/execution-next/ExecutionNext.csproj" -c Release
dotnet build "$candidate/benchmarks/experiments/execution-next/ExecutionNext.csproj" -c Release
node benchmarks/experiments/execution-next/cold.mjs $reference $candidate artifacts/perf/execution-next/cold
$env:PERF_WARMUPS = '30'
node benchmarks/experiments/runtime-generated/production.mjs $reference $candidate artifacts/perf/execution-next/production '*NumericSectionBenchmarks.Generated_Parse*' '*GeneratedBenchmarks.HandWritten_PrimRecord'
Remove-Item Env:PERF_WARMUPS
node tools/quality/perf-check.mjs --baseline execution-next-cases-baseline --cpu 16 --label execution-next-impact
node tools/quality/perf-check.mjs --baseline execution-next-cases-baseline --confirm --cpu 16 --filter '*MaterializationBenchmarks.Generated_Text_Parse*' --filter '*ReadBenchmarks.ReadSelectedScalarTypedMemory*' --filter '*GeneratedBenchmarks.Runtime_PrimRecord_Update*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*' --label execution-next-impact-confirm

dotnet build CStructSharp.NonWeb.slnf -c Release
node tools/quality/test-managed.mjs --full --no-build
node tools/quality/managed-api-baseline.mjs compare
node tools/documentation/validate-canonical-reference.mjs
node tools/quality/feature-operation-matrix.mjs
node --test tools/quality/benchmark-generated-layouts.test.mjs
node tools/quality/changed-documentation.mjs --base HEAD --language csharp
node tools/quality/changed-documentation.mjs --base HEAD --language script
git diff --check
```

Immutable captures live under `artifacts/perf/development/bundles/`; all diagnostic observations and launch orders
are under `artifacts/perf/development/runs/execution-next-*`. Production, cold JSON, independent allocation counts,
native dumps, generated source comparisons and build logs are under `artifacts/perf/execution-next/`. The initial
size probe named nonexistent writer helpers; its raw null entries remain preserved. `cold/*-methods.jsonl` contains
the corrected `ReadRoot`/`EncodeRoot` observations. Both isolated worktrees are retained. The production runner is
the pre-existing local tool in `runtime-generated/`; that investigation remains untracked and untouched.

This follow-on change is left uncommitted. Only the explicitly requested previous round was committed and pushed.
Stable-machine confirmation is the decisive next performance check. No .NET 8 performance, ARM, NativeAOT,
genuine asynchronous I/O, representative generator build-time or contract-equivalent handcrafted comparison claim
is made. The executable-block, writer-section and buffered-continuation experiments above remain deferred.
