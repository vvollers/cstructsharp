# Deferred execution candidates

This round starts from `f748f794`. The immutable `deferred-next-baseline` capture precedes product edits.
`deferred-next-cases-baseline` contains unchanged product code plus identical new benchmark cases in the reference
checkout. Earlier measurements are context, not the baseline for this round. Existing local research and artifacts
are preserved.

## Ranked experiments

| Rank | Mechanism and workload | Source evidence and expected benefit | Effort and correctness risks | Smallest decisive experiment |
| --- | --- | --- | --- | --- |
| 1 | Runtime numeric matrix parsing into final leaf lists | The block reader fills a flat boxed list and `ReshapeTable` copies every leaf reference; remove that temporary reference array | Small executor fusion and row-aware block destination; preserve 64 KiB reads, diagnostics, cancellation and owned `List<object?>` shapes | Existing large matrix plus small, BE and stream cases; compare against original flat execution |
| 2 | Runtime matrix write normalization capacity | `FlattenNestedArrayValues` grows an owned flat list geometrically although validated dimensions bound the usual extent | Small bounded capacity hint; keep every collection callback, eager snapshot and validation-before-write step | Existing runtime matrix serialization; compare callback traces and partial writes against the preserved algorithm |
| 3 | Generated fixed bitfield leaf reader | Every declaration repeats placement, seek, storage loads and masking | Reader-only eligibility and helper; packing, byte order, anonymous charges, constructors, conditions and union fallback | Existing `bitfield-x1k` bytes, now also generated; full owned leaf and array results plus writer control |
| 4 | Direct source spans for runtime boxed numeric blocks | The boxed reader still rents/copies blocks; typed 1D decoding already uses source spans | Small but likely secondary to boxing; retain short-read and byte-budget consumption | Separate follow-up to the row prototype, without other edits |
| 5 | Schema-specific executable blocks | Prepared static plans still dispatch and box each operation | High: second backend, AOT fallback, bounded code/cache lifetime, concurrent initialization and JIT break-even | One numeric block, compare preparation cost C and warmed saving D before considering a backend |
| 6 | Buffered continuation and stream staging | Low-budget/sparse inputs can replay, but ordinary default-buffer fixtures parse once | High: repeated constructor/codec invocations and stream calls are observable | Count real retries, discarded graphs and physical I/O before building resumable execution |

## Baseline checks

The development profile passed before edits: 3,080 runtime, 70 generator, 40 generated parity and two compiler
compatibility tests on .NET 10 (`artifacts/test-results/development/run-MbAkSI`). The previous full suite is not
used as a substitute for final validation of this round.

Benchmark additions use existing fixture bytes and complete results. The new generated bitfield cases compare
every field and serialization byte outside timing; runtime matrix supplements use the existing small/large and
big-endian layouts. Both captures and all subsequent reports retain source identity and raw observations.

## Retained changes and boundaries

The runtime executor recognizes adjacent `ReadNumericList` and `ReshapeTable` instructions for nonempty numeric
tables. It fills the final owned leaf lists during the original block reads, then groups those rows through the
existing shape helper. The bytecode and its diagnostic dumps remain unchanged. Empty tables, standalone reads,
debug reads and disabled fast paths retain their original routes. No boxed value type, list capacity or mutable
ownership is replaced with a typed array or borrowed view. Rows crossing a 64 KiB block do not split the physical
read, budget charge or cancellation boundary. The pooled byte copy is deliberately unchanged in this experiment.

Writer normalization retains every call to `ConvertToObjectList`, count check, enumeration, leaf snapshot and
`AddRange`. After the first child validates, its owned flat list receives a capacity hint bounded to 65,536
references. This removes geometric growth for the tested matrix without streaming values before validation.
An invalid later child can cause speculative storage of up to 512 KiB of references per active nesting level on
x64, plus the array header. A huge misleading count cannot reserve its whole declared product. This is a bounded
memory tradeoff on invalid input; failure text, callback order and partial output are unchanged.

Generated bitfield specialization is reader-only. An eligible leaf is a fixed, unconditional struct with only
constant bitfield windows and separators, no arrays/pointers/nested members, at least two named values and no
consumer-declared partial type. That last restriction also excludes implicit constructors with user initializers.
The existing `TryTakeFixed` guard checks the full extent, alignment, depth, cancellation and **every repeated
storage charge** before using constant-offset loads; identical windows share a load. The seven-byte fixture leaf
still charges 13 bytes, including anonymous padding. A failed guard runs the original member reader. Shared fixed
plans, writers, parent construction, union behavior and signed/enum casts are unchanged.

## Timing evidence and uncertainty

All timing launches ran serially on logical CPU 16, with agents, builds and tests stopped. Affinity does not
isolate that CPU from other activity. The machine is Windows 11 on a Ryzen 9 9950X, SDK 10.0.401 selected by
`global.json`, .NET 10.0.12 and Node 26.5.0. Diagnostic jobs disable tiering/PGO; production jobs enable both and
use 30 warmups and 15 measured 250 ms iterations in fresh processes. Every launch and raw sample is retained.

The initial three-pair identical-code Screen (`deferred-next-aa`) was already unstable. Large matrix parse deltas
ranged from -80.8% to +3.4% with identical code; the canary remained within the screening margin. The prototype
screen suggested the three changes were worth confirmation, but did not establish throughput improvements.

The longer diagnostic confirmation then encountered severe canary drift (-48.2% to +88.5%). Production confirmation
was steadier for bitfields, but its canary also flagged drift (-13.4% to +2.4%, spread 18.9%). The tool therefore
labels **all these timing results inconclusive**. Consistent large directional effects, the removed work, allocation
counts and independent first-use evidence justify retaining the small changes; they do not establish precise,
portable speedup percentages. Writer throughput is explicitly unresolved. No drift correction is applied.

Times below are medians of launch medians in microseconds. Deltas are medians of paired launch ratios, which need
not equal the ratio of the displayed medians. Each row has three fresh launches per side and configuration.

| Operation | Diagnostic before → after, µs | Paired delta (all pairs) | Production before → after, µs | Paired delta (all pairs) |
| --- | ---: | --- | ---: | --- |
| Generated bitfield leaf parse | 0.115 → 0.034 | -70.2% (-81.5…-50.0%) | 0.058 → 0.017 | -71.4% (-71.5…-71.3%) |
| Generated bitfields ×1,024 parse | 66.115 → 15.572 | -76.4% (-77.0…-62.1%) | 54.639 → 11.381 | -79.1% (-79.4…-78.7%) |
| Runtime 256×256 LE matrix parse | 888.249 → 504.877 | -42.3% (-60.8…-18.5%) | 537.340 → 308.449 | -40.5% (-51.2…-20.5%) |
| Runtime 256×256 matrix serialize | 1,289.098 → 1,280.935 | +0.1% (-40.8…+1.6%) | 677.066 → 630.190 | -5.7% (-13.6…+27.0%) |

Diagnostic spreads for those rows were 41.0%, 67.0%, 32.8% and 41.7%; production spreads were 3.6%, 2.4%, 47.5%
and 51.2%. In particular, the bitfield results consistently favor the candidate in both configurations, while the
writer's time does not have a repeatable direction. Its independently verified memory reduction is the reason to
retain the capacity hint.

Additional diagnostic confirmation:

| Runtime parse | Before → after, µs | Paired delta (all pairs) | Spread |
| --- | ---: | --- | ---: |
| 16×16 matrix | 2.671 → 2.358 | -11.8% (-41.4…+45.4%) | 38.3% |
| 256×256 BE matrix | 898.883 → 354.728 | -60.5% (-61.9…-45.3%) | 59.5% |
| 256×256 non-exposable stream | 881.112 → 329.495 | -62.6% (-65.8…-40.7%) | 66.0% |

Those timings share the canary drift and remain inconclusive. No production throughput claim is made for these
additional cases, for genuine asynchronous I/O, or for another machine, runtime or operating system.

### Allocation and opposite-direction controls

Independent warmed current-thread allocation probes confirm the large matrix counts exactly. The small matrix
and stream counts below repeat across Screen launches. Generated bitfields still return the same owned classes.

| Operation | Before B/op | After B/op | Change |
| --- | ---: | ---: | ---: |
| Runtime 256×256 matrix parse | 2,638,056 | 2,113,744 | -524,312 (-19.9%) |
| Runtime 256×256 matrix serialize | 1,178,024 | 655,624 | -522,400 (-44.3%) |
| Runtime 16×16 matrix parse | 11,496 | 9,424 | -2,072 |
| Runtime 256×256 stream parse | 2,638,224 | 2,113,912 | -524,312 |
| Generated bitfields ×1,024 parse | 41,008 | 41,008 | unchanged |
| Generated bitfield leaf parse | 32 | 32 | unchanged |

BDN sometimes reports extra amortized bytes during longer runs; these raw observations remain in the reports and
are not treated as changed result allocations. The independent probe captures its counter before constructing
the reporting object. The changes remove temporary reference storage, not numeric boxes or owned row storage.

Generated matrix parsing and serialization are controls for the runtime changes; their source is byte-identical
between the measured checkouts. Generated bitfield serialization also retains its original code and allocation
(179,224 B/op in Screen); its diagnostic confirmation was +0.6% with -44.9…+82.3% pair variation. No writer gain or
regression is established there. The handwritten primitive checksum, about 2.2 ns and zero allocation, is a drift
canary only. It is not an equivalent comparator for an owned matrix or 1,024 validated bitfield objects, and no
gap to a contract-equivalent handcrafted implementation is claimed.

### Complete Impact screen and follow-up

The combined **74-case Impact screen ran once**. It samples runtime/generated operations, parse/write/update,
compilation, tiny records, arrays, text, byte order, dynamic layouts, unions, pointers, mapping, caller spans,
buffer writers, streams, async and segmented input. Several unrelated cases flagged very large slowdowns.

Targeted three-pair confirmation covered the largest flags. Its canary again drifted (-40.3…-2.1%, spread 92.8%).
Generated packet parsing ranged -1.0…+81.0%; primitive POCO buffer-writer output -45.9…+81.1%; span output
-44.8…+80.5%; typed small-root reads -36.7…+58.7%; segmented primitive parsing -44.4…+77.1%; and union parsing
+2.6…+70.3%. All remain inconclusive. Packet and mixed-text generated source hashes are unchanged.
This does **not** prove absence of a regression, particularly small effects in the shared runtime executor.
Further repetitions were stopped because the environment could not resolve the uncertainty. Stable-machine
confirmation is the decisive follow-up; no favorable launch was selected or substituted for the complete record.

## First use, compilation and code size

Ten fresh processes per side/configuration/workload produced 160 first-operation observations. Schema construction
precedes the runtime matrix read; its lazy execution preparation remains measured. Matrix write input is parsed
before observing its first serialization. These are first-operation costs, not whole-application startup times.

| First operation | Diagnostic median ms (range), before → after | Production median ms (range), before → after |
| --- | --- | --- |
| Runtime matrix parse | 55.411 (54.644–55.892) → 56.110 (55.496–56.790) | 28.368 (28.126–29.118) → 29.350 (28.875–29.921) |
| Runtime matrix serialize | 29.670 (29.335–29.920) → 29.924 (29.619–30.398) | 14.410 (14.207–14.831) → 14.461 (14.311–14.988) |
| Generated bitfields ×1,024 parse | 5.334 (5.276–5.408) → 2.854 (2.826–2.942) | 2.491 (2.460–2.527) → 1.635 (1.614–1.730) |
| Generated bitfield leaf parse | 4.550 (4.535–4.668) → 1.927 (1.907–1.962) | 1.634 (1.610–1.735) → 1.065 (1.048–1.142) |

Runtime matrix parsing trades approximately 0.7–1.0 ms of first-use work for lower temporary allocation and the
observed warmed direction. Its incremental JIT medians increase 53.277 → 54.231 ms diagnostically and
26.214 → 27.248 ms with production tiering. Given the warmed timing uncertainty, no precise call-count break-even
is claimed. Writer first-use ranges overlap. Generated bitfield first-use ranges separate in both configurations;
the success path avoids compiling its original large fallback body.

The tested bitfield generated source grows 131,112 → 133,285 bytes (+2,173), and reader IL grows 1,465 → 1,679
bytes (+214). The diagnostic `ReadInto<MemoryReadCursor>` body grows 1,836 → 2,259 native bytes (+423). The native
bitfield dump records the old `ReadRec` at 6,348 bytes and the new `ReadRecBitfields` at 315 bytes; this compares
the bodies compiled on those first success paths, **not** total native code, and excludes inlined caller work and
the candidate fallback that was not JIT-compiled in that observation.

There is no new runtime execution backend, persistent plan cache or pooled result lifetime. The bitfield plan
dictionary exists only while generating a layout; consumer-declared type names are additional generator request
metadata. Generator compilation throughput and retained native memory were not separately measured. Runtime
schema-compilation screen results were noisy and establish no change. No AOT, ARM, .NET 8 performance or multicore
throughput claims are made; .NET 8 correctness is covered by the full suite.

## Rejected scope and decisive next work

- **Rejected:** removing eager matrix normalization or writing each row as it is enumerated. It changes mutation
  visibility, caller callbacks and validation-before-write failure order. The retained capacity hint leaves all of
  those steps in place; adversarial collections are compared against the original algorithm.
- **Rejected:** applying the bitfield shortcut to consumer-declared leaf types or broadening the shared fixed plan.
  Implicit constructors can execute user initializers, and the shared plan also drives writers and parent shortcuts.
  The reader-only plan and conservative exclusion avoid those changes.
- **Deferred:** borrowing source spans inside the boxed block reader. The pooled copy still exists, but the new
  measurements cannot resolve a likely smaller timing change. A separate span-versus-pooled prototype must retain
  original block failure behavior; no benefit is asserted without that experiment.
- **Deferred architectural backend:** a bounded numeric executable block may remove remaining prepared-plan dispatch.
  It still needs owned boxed results, an AOT fallback, concurrent initialization tests, bounded cache/code lifetime,
  preparation/JIT cost C, warmed saving D, and measured break-even before adopting another backend.
- **Deferred:** larger fixed sections in dynamic generated layouts. Use a representative section of at least several
  dozen bytes, excluding open bitfield state, assertions and caller effects; do not repeat the rejected tiny prefix.
- **Deferred:** buffered continuation. First count retries, copied bytes, discarded graphs and codec/constructor calls
  on sparse pointer targets and low budgets with real async I/O. Default buffering usually parses ordinary fixtures
  once, and repeated user-code interactions cannot simply be removed.

## Reproduction and artifact locations

Use the pinned Node 26.5.0 (`$env:TEMP/cstructsharp-node-26.5.0/node-v26.5.0-win-x64/node.exe` here); commands below
abbreviate it as `node`. Capture names and output directories must be fresh when reproducing.

```powershell
node tools/quality/perf-check.mjs --capture deferred-next-baseline
$reference = 'C:/Users/vmvol/.codex/worktrees/deferred-next-reference/cstructsharp'
$candidate = 'C:/Users/vmvol/.codex/worktrees/deferred-next-candidate/cstructsharp'
# Reference product code is f748f794; copy only the identical benchmark additions to it.
node tools/quality/perf-check.mjs --capture deferred-next-cases-baseline --checkout $reference
$filters = @('--filter', '*MaterializationBenchmarks.Runtime_Matrix*', '--filter', '*BitfieldBenchmarks*', '--filter', '*GeneratedBenchmarks.HandWritten_PrimRecord')
node tools/quality/perf-check.mjs --baseline deferred-next-cases-baseline --checkout $reference --rounds 3 --cpu 16 @filters --label deferred-next-aa
node tools/quality/perf-check.mjs --baseline deferred-next-cases-baseline --rounds 3 --cpu 16 @filters --label deferred-next-prototypes
node tools/quality/perf-check.mjs --baseline deferred-next-cases-baseline --confirm --cpu 16 @filters --label deferred-next-confirm

# Copy the DeferredNext probe sources to both checkouts; copy product edits only to candidate.
dotnet build "$reference/benchmarks/experiments/deferred-next/DeferredNext.csproj" -c Release
dotnet build "$candidate/benchmarks/experiments/deferred-next/DeferredNext.csproj" -c Release
$env:PERF_WARMUPS = '30'
node benchmarks/experiments/runtime-generated/production.mjs $reference $candidate artifacts/perf/deferred-next/production '*MaterializationBenchmarks.Runtime_Matrix256_Parse' '*MaterializationBenchmarks.Runtime_Matrix256_Serialize' '*BitfieldBenchmarks.Generated_*_Parse' '*GeneratedBenchmarks.HandWritten_PrimRecord'
Remove-Item Env:PERF_WARMUPS
node benchmarks/experiments/deferred-next/cold.mjs $reference $candidate artifacts/perf/deferred-next/cold

node tools/quality/perf-check.mjs --baseline deferred-next-cases-baseline --cpu 16 --label deferred-next-impact
node tools/quality/perf-check.mjs --baseline deferred-next-cases-baseline --confirm --cpu 16 --filter '*WriteBenchmarks.Serialize_Prim_Poco_To*' --filter '*PacketBenchmarks.Generated_Parse' --filter '*ReadBenchmarks.ReadTypedSmallRootMemory' --filter '*SequenceBenchmarks.Runtime_PrimRecord_FourSegments' --filter '*ImpactParseBenchmarks*union-x1k*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord' --label deferred-next-impact-confirm

# Run on each checkout, using a different generated-source output directory per side.
dotnet build benchmarks/experiments/deferred-next/DeferredNext.csproj -c Release --no-incremental -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=C:/projects/struct/cstructsharp/artifacts/perf/deferred-next/generated-after
```

The production runner is the pre-existing local investigation tool preserved under `runtime-generated/`; that
directory remains untracked. Immutable captures are under `artifacts/perf/development/bundles/`. Every diagnostic
report, launch order and original BDN sample is under `artifacts/perf/development/runs/deferred-next-*`.
Production reports, cold JSON/allocation counters, native dumps and generated source are under
`artifacts/perf/deferred-next/`. Both isolated worktrees are retained. The reference has only benchmark/probe/test
additions; its product files remain at `f748f794`.

## Correctness and delivery

The 19 new runtime cases and seven new generated parity cases pass against both the candidate and unchanged
reference product code. They cover numeric types and byte order, rows crossing block boundaries, zero and 3D
shapes, mutable ownership, promoted/union/standalone values, exact failures and positions, explicit stream read
sizes, mid-read cancellation, adversarial collection callbacks and snapshots, bounded invalid-shape allocation,
partial writes, bitfield packing/separators/repeated charges, consumer constructors/initializers, and stream/async
position restoration. No existing assertion was weakened and the character-scratch boundary assertion is intact.

- `dotnet build CStructSharp.NonWeb.slnf -c Release`: passed with zero warnings/errors;
  `artifacts/perf/deferred-next/release-build.log`.
- `node tools/quality/test-managed.mjs --full --no-build`: **8,742 passed** — 4,288 runtime cases per runtime,
  47 parity cases per runtime, 70 generator tests and two modern compiler tests;
  `artifacts/test-results/development/run-YMrreo/summary.json` and original TRX/logs.
- Reference equivalence checks: runtime `run-O2HNzM`, parity `run-VuzrRx`, under the reference checkout's
  `artifacts/test-results/development/`.
- Managed API comparison passed, baseline revision 62 unchanged. Canonical reference and feature-operation
  matrix checks passed, including all 48 generated parity fixtures.
- Generated benchmark layout checks: 13 passed. Changed-declaration documentation checks passed for C# and scripts.
- Existing generated-source snapshots passed unchanged. No behavioral fixture, golden outcome or contract changed.

```powershell
dotnet build CStructSharp.NonWeb.slnf -c Release
node tools/quality/test-managed.mjs --full --no-build
node tools/quality/managed-api-baseline.mjs compare
node tools/documentation/validate-canonical-reference.mjs
node tools/quality/feature-operation-matrix.mjs
node --test tools/quality/benchmark-generated-layouts.test.mjs
node tools/quality/changed-documentation.mjs --base HEAD --language csharp
node tools/quality/changed-documentation.mjs --base HEAD --language script
git diff --check
# From the unchanged reference checkout, after copying only the new tests:
node tools/quality/test-managed.mjs --filter 'FullyQualifiedName~NumericTableMaterializationTests|FullyQualifiedName~NestedArrayNormalizationTests'
node tools/quality/test-managed.mjs --suite parity --filter 'FullyQualifiedName~BitfieldLeafTests'
```

No authored `docs/` pages or app code changed. The existing local README edits, earlier research directories and
benchmark artifacts remain intact. Validation preceded the user-requested commit and push. No publication or release was performed.
