# Overall execution investigation

This round starts from `649d927b` (including `4cc1ed7c`). The original local edits to the benchmark guide and
the `runtime-generated` and `parsing-next` investigations are preserved. Their previous timings are context,
not this round's baseline. No commit, publication or release is part of this work.

## Baselines and execution paths

`overall-next-baseline` is the immutable capture taken before product edits. Baseline development checks passed:
3,074 runtime, 70 generator, 32 compiled parity and two compiler-compatibility cases on .NET 10.

`overall-next-supplement-baseline` contains unchanged product code in the reference checkout, with the same
benchmark-only additions as the candidate: small and big-endian generated matrix serialization, and runtime
matrix parsing and serialization. Setup checks complete values and bytes outside timing. Existing fixtures,
timed methods and inputs are reused. The original capture remains unchanged.

| Operation | Actual path and relevant cost |
| --- | --- |
| `ImpactParseBenchmarks.ParseSpan` | Supplies variables; uses the general engine, including eligible static plans. Recursive static-plan children still initialized absent sentinels and tracked every slot insertion. |
| `GeneratedBenchmarks.Runtime_Nested256_Parse` | Omits variables; the direct root reader already constructs complete slots. It is a control, not evidence for the general-engine change. |
| Generated numeric matrix write | The general generated writer validates each row, then reserves and encodes each leaf separately. The large case makes 65,536 scalar reservations. |
| Runtime numeric matrix read/write | Reads boxed leaves in 64 KiB blocks, then reshapes a flat list; writing validates/flattens nested values before scalar encoding. Generated matrix changes do not optimize these paths. |
| Generated stream/async write | Serializes owned bytes, then retains the existing stream write operation. Caller-provided spans and updates require overlapping-buffer semantics. |
| Buffered read | Ordinary fixtures under the default 64 MiB read budget usually parse once. Sparse targets and low budgets can replay; codecs and constructors can observe the repeats. |
| Compilation/access | Prepared programs/static plans, bounded path caching and the layout cache already exist. There is no general syntax-tree interpretation or uncached-path problem to remove. |

## Ranked experiments

The ranking preceded product changes. Benefit estimates were hypotheses, not results.

| Rank | Mechanism and workloads | Evidence / expected benefit | Effort and correctness risk | Smallest experiment |
| --- | --- | --- | --- | --- |
| 1 | Bulk generated numeric leaf rows; serialization | Repeated per-scalar reservations in `EmitNestedArrayWrite`; potentially substantial | Small emitter change; row validation, partial writes, aliased spans and fallible codecs must remain exact | Existing large matrix write, small/BE supplements; original scalar sequence as failure oracle |
| 2 | Complete recursive slots inside general static plans; runtime parsing | `RunStaticPlan` creates incremental children although every slot is guaranteed present | Small shared dispatcher change; preserve captures, promotions, cancellation, mutable ownership | Existing nested general parse with direct-root control |
| 3 | Direct memory spans for runtime numeric matrix blocks | `PrimitiveArrayReader.ReadInto` rents/copies while typed `ReadBlocks` already borrows internally | Small; likely modest because boxing and reshaping dominate; preserve exact block reads and short-read fallback | Runtime matrix supplement, followed by an isolated lazy-scratch prototype if justified |
| 4 | Generated fixed bitfield runs | `EmitBitfield` repeats placement, loads and budget charges for shared storage | Medium; reader-specific plan needed because fixed plans also drive writers; packing, zero-width separators, union overlap and constructors complicate eligibility | Add generated version of existing `bitfield-x1k`, preserve every charge, compare pure fixed structs first |
| 5 | Schema-specific executable numeric blocks | Prepared plan still dispatches and boxes each numeric operation | High architecture cost: AOT fallback, JIT, cache lifetime, concurrency and native code | One bounded block with owned slots; measure preparation/JIT cost C, warmed saving D, break-even ceil(C/D), retained code and concurrent initialization |
| 6 | Runtime matrix normalization/materialization | Flat reference lists and boxes add substantial temporary storage | Medium–high; retain final nested `List<object?>`, validation-before-write order and arbitrary enumeration side effects | Restricted built-in numeric final-row destination for reads; separately, exact typed-array snapshot validation for writes |
| 7 | Remove replay / change stream staging | Potential discarded graphs and copies, but little evidence in current workloads | High; codec window size, constructor calls, stream positions and partial writes are observable | First count actual replay/copies on sparse and true asynchronous I/O workloads |

## Compatibility decisions

The row prototype initially used `TryReserveFixed` for all destinations. Review rejected that design before timing:
the helper clears its destination, which can alias a numeric source row. A scratch snapshot is also insufficient:
with shifted overlap, the original scalar loop can overwrite a later source element before reading it. The safe
scope is owned serialization output. No new public cursor helper or borrowed result is needed.

Nested complete slots keep the outer value incremental. This avoids moving construction ownership through all
general-engine call sites. One dispatcher accepts the nested owned slot array; captures and structure-entry
checkpoints stay in their original sequence. The extra null branch and native-code size are costs to measure.

The existing runtime character-scratch assertion above 256 code units remains unchanged. This is a test constraint,
not a claim that the public text API inherently needs that scratch storage. Per-field delegates and the rejected
six-byte numeric prefix are not repeated. Larger fixed sections remain a distinct, unmeasured hypothesis.

## Measurement record

Measurements use logical CPU 16, serial launches and the pinned Node 26.5.0. Affinity does not isolate the machine.
Screen/Confirm disable tiering/PGO; production measurements use the existing out-of-process runner with 30 warmups.
Every launch, raw sample and variation report is retained. Percentage ranges are observations, not confidence
intervals; the 3% practical margin is not statistical confidence. Canary drift is never subtracted.

The initial three-pair identical-code screen (`overall-next-aa`) already showed nested parse launch deltas
−4.2%…+1.0%, despite a stable primitive-read canary. Small differences require independent confirmation.

Machine: Windows 11, Ryzen 9 9950X, .NET 10.0.12, SDK 10.0.401 selected by `global.json`'s roll-forward policy.
No performance measurements were taken on .NET 8, ARM, an AOT deployment or another operating system.

### Runtime parsing

Times are medians of launch medians; deltas are medians of paired ratios. These can differ when conditions drift.
Every row below represents three fresh launches per side in each configuration.

| General-engine parse | Diagnostic before → after, µs | Paired delta (all pairs) | Production before → after, µs | Paired delta (all pairs) |
| --- | ---: | --- | ---: | --- |
| Nested ×256 | 53.231 → 38.406 | −28.5% (−29.1…−25.1%) | 32.979 → 26.829 | −18.6% (−22.3…−12.9%) |
| Alias records ×1,024 | 33.728 → 24.788 | −26.6% (−27.4…−26.5%) | 22.115 → 19.749 | −10.7% (−12.7…−8.5%) |
| PNG header | 0.192 → 0.175 | −8.6% (−9.4…−7.6%) | Not measured | — |

Diagnostic spreads were 7.3%, 3.5% and 2.9%, respectively; production spreads were 13.7% and 6.3%.
The reporting tool therefore labels nested parsing and both production rows unstable. Their consistent directions,
the removed bookkeeping, and independent configurations support a useful improvement, not a precise portable
percentage. The production identical-code A/A check used the same candidate on both sides: nested deltas were
−4.3…+3.1% (median +1.7%, spread 7.1%); aliases were −0.0…+6.4% (median +1.7%, spread 5.6%). Those controls do
not reproduce the observed before/after reductions. No canary correction or favorable-launch selection was used.

The tiny general-engine primitive record is a branch-cost control: 117.73 → 118.40 ns, +0.6% (−0.2…+1.4%),
7.3% spread. The union follow-up was 300.221 → 300.623 µs, +0.1% (−4.3…+2.6%). Neither establishes a regression.
No direct-root parsing improvement is claimed: that reader already constructed complete slots before this round.

### Generated serialization

| Owned matrix serialization | Diagnostic before → after, µs | Paired delta (all pairs) | Production before → after, µs | Paired delta (all pairs) |
| --- | ---: | --- | ---: | --- |
| 16×16 little-endian | 0.678 → 0.186 | −72.4% (−72.6…−55.8%) | 0.362 → 0.076 | −79.0% (−79.4…−78.7%) |
| 256×256 little-endian | 198.483 → 48.148 | −75.8% (−75.9…−75.1%) | 122.529 → 41.863 | −66.0% (−66.3…−65.4%) |
| 256×256 big-endian | 204.543 → 63.425 | −69.0% (−72.4…−61.1%) | 121.545 → 43.135 | −64.5% (−64.9…−64.4%) |

Diagnostic spreads were 61.5%, 2.9% and 37.2%; production spreads were 3.3%, 6.2% and 7.2%. The small and
big-endian diagnostic figures are especially variable. Every pair in both configurations still favors the candidate
by a large margin. Treat the direction as supported and the exact size as workload/configuration dependent.
The optimized LE writer replaces 65,536 scalar reservations with 256 row reservations and existing bulk encodes.
There is no scratch row, changed value shape, weaker validation or new output-buffer borrowing.

### Allocations, controls and broader coverage

The independent warmed allocation probe records **217,232 B/op on both sides** for nested general parsing and
**131,096 B/op on both sides** for the large generated matrix write. The small matrix write stays at 536 B/op;
alias parsing stays at 163,984 B/op. This round saves execution work, not the owned result storage. BDN sometimes
reports a few additional amortized diagnostic bytes or even slightly smaller values in short allocation batches;
those raw reports are preserved and are not interpreted as object-allocation savings. The initial separate allocation
probe accidentally included its 32-byte reporting object across 128 calls (+0.25 B/op on both sides); the
`*-allocations-verified.json` observations capture the counter before constructing that object and supersede that
instrumentation detail. First-call measurements were unaffected.

The complete **68-case Impact screen ran once**, after combining the changes. It covers compilation, tiny records,
large arrays, byte order, dynamic/nested layouts, strings, bitfields, unions, pointers, mapped/dynamic results,
span and buffer-writer output, streams, async and segmented input. Text and packet reads flagged +12.8…+27.9%.
Targeted confirmation found packet parsing +0.3% (−0.1…+2.3%), while text varied in the opposite direction:
untrimmed −21.2% (−23.0…−0.2%), trimmed −11.0% (−13.1…0.0%). Their generated source is byte-identical between
revisions. No repeatable slowdown was found, and no text improvement is claimed. Do not interpret an unflagged
screen result as equivalence, or its incidental faster mapped/path timings as benefits of this change.

Runtime serialization and generated parsing are separate controls. The screen's runtime primitive serialization was
65.50 → 65.39 ns; runtime matrix serialization was 642.4 → 658.4 µs (+2.5%, 12.5% spread), with 1,178,024 B/op
unchanged. Generated matrix parsing still allocated 1,072 / 139,312 B/op; its noisy timing offers no new improvement
claim. Runtime matrix parsing still allocated 2,638,056 B/op. Neither matrix runtime path was changed.

The handwritten primitive checksum (about 2.2 ns, zero allocation) is solely a drift canary. It does not validate,
materialize or own a nested graph or matrix output, so it is not a fair throughput target for these operations.
The earlier owned comparators informed the ranking but were not reused as this round's baseline. No new gap to
a contract-equivalent handcrafted implementation is claimed.

### First use, compilation, memory and code size

Ten fresh processes per side/configuration/workload produced these first-operation observations. The nested case
includes lazy program/static-plan preparation after schema construction; matrix values are constructed without
calling a writer first. These are not whole-application startup times.

| First operation | Diagnostic median ms (range), before → after | Production median ms (range), before → after |
| --- | --- | --- |
| General nested parse | 42.851 (42.790–43.791) → 43.235 (43.041–43.636) | 15.017 (14.949–15.757) → 15.106 (15.005–15.347) |
| Generated large matrix write | 4.174 (4.018–4.217) → 5.070 (4.788–5.217) | 3.220 (3.154–3.301) → 2.283 (2.210–3.218) |

Nested first-use ranges overlap: no startup improvement is established. Matrix diagnostic incremental JIT grows
3.569 → 4.608 ms, a real tradeoff for calling the bulk helper. Its roughly 0.90 ms first-operation cost is about six
times the measured diagnostic warmed saving, a rough workload-specific break-even rather than a guarantee.
Production first-write JIT decreases 2.627 → 1.886 ms and the first operation is faster. First-call allocations are
unchanged: matrix 395,304 B; nested 246,984 B diagnostic and 263,592 B production.

Generated matrix source grows by 966–968 bytes per tested layout, including private-parameter documentation.
Writer IL grows 444 → 503 bytes for the small matrix and 471 → 533 for the large matrices. Diagnostic main-writer
native code grows 1,007 → 1,192 bytes; runtime `RunStaticPlan<MemoryReadCursor>` grows 4,103 → 4,402 bytes. Those
native figures exclude separate callees. First-use JIT observations include their work.

No runtime schema compiler, delegate backend, cache, allocation pool or plan representation is added. The emitter
checks row eligibility once and caches one boolean while generating the layout. Runtime schema compilation in the
Impact screen has no credible timing change; its small variable allocation differences are not attributed to these
execution edits. Source-generator compilation throughput and retained native memory were not separately measured.
There is no multi-core or real asynchronous file-I/O throughput claim.

## Rejected and deferred work

- **Rejected before timing:** unrestricted row reservation and scratch-row alternatives change overlapping-span
  semantics. The adopted owned-output flag avoids both; borrowed destinations retain the original scalar loop.
- **Deferred:** direct memory access in boxed matrix blocks removes a pooled byte copy, but leaves the much larger
  boxing/flat-reference costs. The new runtime matrix cases are the baseline for a future isolated comparison.
- **Deferred:** final runtime rows and writer normalization have meaningful allocation headroom (2.64 MB read,
  1.18 MB write in the large matrix cases). Read directly into final lists while preserving block consumption;
  for writes, first prove identical eager validation/enumeration and failure order before removing normalization.
- **Deferred:** generated bitfield runs and larger fixed sections need new representative generated coverage and
  reader-specific eligibility. Repeated charges, packing, allocation direction, separators, unions and constructor
  side effects make them more complex than the retained changes. Do not repeat the rejected tiny-prefix experiment.
- **Deferred architectural backend:** a schema-specific executable block needs AOT fallback, bounded cache/code
  lifetime and concurrency tests, plus measured compilation/JIT break-even. Current gains require no second backend.
- **Deferred:** resumable buffered parsing and stream/async staging changes. First measure replay/discarded graphs
  or actual I/O copying on an eligible workload; never remove repeated codec calls, constructors, or observable
  stream/buffer-writer calls merely because they appear redundant.

## Reproduction and artifacts

Use Node from `.node-version`; on this machine it is
`$env:TEMP/cstructsharp-node-26.5.0/node-v26.5.0-win-x64/node.exe`. Commands below abbreviate it as `node`.
Capture names and result directories must be fresh when reproducing.

```powershell
node tools/quality/perf-check.mjs --capture overall-next-baseline
# Copy only MaterializationBenchmarks.cs additions to unchanged 649d927b reference code.
node tools/quality/perf-check.mjs --capture overall-next-supplement-baseline --checkout C:/Users/vmvol/.codex/worktrees/overall-next-reference/cstructsharp
node tools/quality/perf-check.mjs --baseline overall-next-baseline --rounds 3 --cpu 16 --filter '*MaterializationBenchmarks.Generated_Matrix256_Serialize' --filter '*ImpactParseBenchmarks*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord' --label overall-next-aa

node tools/quality/perf-check.mjs --baseline overall-next-supplement-baseline --rounds 3 --cpu 16 --filter '*MaterializationBenchmarks*Matrix*' --filter '*ImpactParseBenchmarks*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord' --label overall-next-prototype-screen
node tools/quality/perf-check.mjs --baseline overall-next-supplement-baseline --confirm --cpu 16 --filter '*MaterializationBenchmarks.Generated_*_Serialize' --filter '*ImpactParseBenchmarks*nested-x256*' --filter '*ImpactParseBenchmarks*parity-alias-x1k*' --filter '*ImpactParseBenchmarks*real-png*' --filter '*ImpactParseBenchmarks*prim-le-record*' --filter '*ImpactParseBenchmarks*union-x1k*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord' --label overall-next-confirm
node tools/quality/perf-check.mjs --baseline overall-next-supplement-baseline --cpu 16 --label overall-next-impact
node tools/quality/perf-check.mjs --baseline overall-next-supplement-baseline --confirm --cpu 16 --filter '*MaterializationBenchmarks.Generated_Text*' --filter '*PacketBenchmarks.Generated_Parse' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord' --label overall-next-impact-confirm

$reference = 'C:/Users/vmvol/.codex/worktrees/overall-next-reference/cstructsharp'
$candidate = 'C:/Users/vmvol/.codex/worktrees/overall-next-candidate/cstructsharp'
# Copy identical benchmark and OverallNext probe sources to both; copy product edits only to the candidate.
dotnet build "$reference/benchmarks/experiments/overall-next/OverallNext.csproj" -c Release
dotnet build "$candidate/benchmarks/experiments/overall-next/OverallNext.csproj" -c Release
$env:PERF_WARMUPS = '30'
node benchmarks/experiments/runtime-generated/production.mjs $reference $candidate artifacts/perf/overall-next/production-isolated '*ImpactParseBenchmarks*nested-x256*' '*ImpactParseBenchmarks*parity-alias-x1k*' '*MaterializationBenchmarks.Generated_*_Serialize' '*GeneratedBenchmarks.HandWritten_PrimRecord'
node benchmarks/experiments/runtime-generated/production.mjs $candidate $candidate artifacts/perf/overall-next/production-aa '*ImpactParseBenchmarks*nested-x256*' '*ImpactParseBenchmarks*parity-alias-x1k*' '*GeneratedBenchmarks.HandWritten_PrimRecord'
Remove-Item Env:PERF_WARMUPS
node benchmarks/experiments/overall-next/cold.mjs $reference $candidate artifacts/perf/overall-next/cold

# Generated-source inspection, outside timing, on each checkout with distinct output directories:
dotnet build benchmarks/experiments/overall-next/OverallNext.csproj -c Release --no-incremental -p:EmitCompilerGeneratedFiles=true -p:CompilerGeneratedFilesOutputPath=C:/projects/struct/cstructsharp/artifacts/perf/overall-next/generated-after
```

The `overall-next-aa` command ran before product edits. Running it against edited product code is a before/after comparison.
Immutable bundles are under `artifacts/perf/development/bundles/`, reports under `artifacts/perf/development/runs/`,
and supplementary cold/source-size evidence under `artifacts/perf/overall-next/`.

Both managed reference/candidate worktrees are retained at the paths above. Only benchmark/probe additions (and,
after timing, the new equivalence tests) were copied to the reference; its product sources remain at `649d927b`.
The candidate contains exactly the two measured product edits and identical benchmark sources.

The initial `artifacts/perf/overall-next/production/` attempt retains a completed unpaired reference launch and the
candidate setup failure. BenchmarkDotNet searched the main checkout's existing `.claude` and `.local-docs` experiment
trees and found duplicate benchmark project names. No candidate timing was produced. The isolated campaign uses
fresh directories and checkouts to correct that setup failure; the unpaired launch was not used in paired estimates.
No existing local experiment, worktree or benchmark artifact was removed.

The isolated `OverallNext` probe reuses the original matrix values and nested fixture. It measures one operation
per process, including lazy runtime plan preparation for the first parse; schema construction happens beforehand.
First-write values are built without invoking any writer. It reports elapsed time, managed thread allocation,
incremental JIT work and generated writer IL size. It does not claim whole-application startup or generator
compilation throughput.

## Correctness and final review

Six new runtime cases and eight generated writer cases pass on the unchanged reference and on the candidate.
They check complete mutable ownership/presence, promoted fields, qualified and sequential captures, empty shapes,
text trimming, truncations, read/write budgets, nesting and array limits, stream positions, three dimensions,
byte order, booleans, null/incorrect rows, union/update paths, odd-width failure priority, complete diagnostics,
overlapping destination mutation, custom codec order, cancellation checkpoints, and exact stream-write counts.
No existing test or assertion was weakened, skipped or removed. The character-scratch boundary assertion remains.

One generated-source snapshot (`Types.PropertyTypes.g.cs`) intentionally records the private ownership argument,
its propagation and documentation, and the row branch with its unchanged scalar fallback. Its diff was reviewed;
the existing mapping assertions still pass. No behavioral golden, fixture, language contract or API baseline changed.

Final checks:

- `dotnet build CStructSharp.NonWeb.slnf -c Release`: passed, zero warnings/errors;
  `artifacts/perf/overall-next/release-build.log`.
- `node tools/quality/test-managed.mjs --full --no-build`: **8,690 passed** — runtime 4,269 on each of .NET 8/.NET 10,
  parity 40 on each, generator 70, modern compiler two;
  `artifacts/test-results/development/run-pm4uUy/summary.json` and its original TRX/logs.
- New equivalence tests against unchanged reference product code: six runtime and eight generated cases on .NET 10;
  `artifacts/test-results/overall-next/reference-{runtime,generated}.trx`.
- Targeted runtime/static-plan checks: 13 passed; eight generated writer checks passed; the five exhaustive
  .NET 10 cursor-differential tests passed (`artifacts/test-results/overall-next/overall-next-cursor.trx`).
- `node tools/quality/managed-api-baseline.mjs compare`: passed, managed revision 62 unchanged.
- `node tools/documentation/validate-canonical-reference.mjs`: passed.
- `node tools/quality/feature-operation-matrix.mjs`: passed, including all 48 generated parity fixtures.
- `node --test tools/quality/benchmark-generated-layouts.test.mjs`: 12 passed.
- `node tools/quality/changed-documentation.mjs --base HEAD --language csharp` and `--language script`: passed.
- `git diff --check`: passed; product, new tests, snapshot and documentation diffs reviewed.

No authored `docs/` source or application code changed, so application and full DocFX checks were not needed.
The existing benchmark-guide edits and prior investigation directories are preserved. No commit, push, publication
or release was performed.
