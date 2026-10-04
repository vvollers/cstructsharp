# Text serialization investigation

Baseline: `2aa33f54`, committed and pushed before this round. Immutable capture:
`artifacts/perf/development/bundles/text-write-next-baseline` (86 Impact cases).
Earlier artifacts and unrelated research are preserved. The rejected generated numeric-section optimization
remains removed. All timings in this investigation use CPU 16 serially; affinity does not reserve the CPU.

The [runtime slowdown follow-up](../runtime-text-slowdown/README.md) preserves this investigation's artifacts and
extracts bounded stack staging into a separate helper. The timings below describe the starting implementation;
the follow-up records the retained helper boundary, reduced first-use cost and separate production evidence.

## Ranked opportunities before implementation

| Rank | Mechanism / workload | Evidence and expected benefit | Effort / correctness risk | Decisive experiment |
| --- | --- | --- | --- | --- |
| 1 | Runtime and generated bounded-text writes: encode validated immutable strings into existing or reusable storage | Both writers allocate an encoded array after `GetByteCount`; 256 text records allocate 14,336 bytes for UTF-8 alone | Small shared helper; preserve validation before reservation and separate runtime payload/padding writes | Existing 1/256 complete-record write benchmarks, failure/stream equivalence, then confirmation |
| 2 | Runtime wide-text writes: bounded staging without padded string / encoded array | Two arrays per record total 28,672 bytes at 256 records; shorter supplied strings also allocate padding | Small helper; invalid surrogate diagnostics, budget priority and whole-field stream writes must stay exact | Isolated prototype against rank 1 and original baseline, unpadded and boundary tests |
| 3 | Runtime/generated CP437 reads: remove byte and character intermediates | Shared span decoder copies bytes, then creates a character array; text workloads still exercise it | Moderate; owned string construction from borrowed span must stay simple and portable | Isolated CP437 allocation/time case; reject machinery disproportionate to benefit |
| 4 | Runtime numeric executable blocks replacing repeated dispatch | Existing compiled static plans still dispatch operations; earlier per-field delegates lost | Architectural/high effort; AOT fallback, cache lifetime/concurrency, compilation/JIT and complete owned graphs | One whole fixed-record block with cold/warm break-even and equivalent materialization; no backend without evidence |
| 5 | Buffered input continuation to avoid replay | Large or short-chunk inputs may retry; ordinary small records generally fit first window | High risk: custom codec/constructor replay and cancellation are observable | Count actual retries/discarded graphs on a real workload before designing continuations |

Generated wide writers already encode into the destination. Runtime terminated writers already use stack/pool
staging. Parsing and schema preparation remain controls for this round. Large numeric arrays already use bulk
operations. Existing handcrafted numeric benchmarks are canaries, not equivalent text serialization comparators.

The initial five-case identical-code screen is retained under `text-write-next-aa`: runtime write-256 paired
median +0.2%, launch range −3.8…+8.1%; write-1 −5.2%, range −5.5…+3.0%. Both unstable. Allocations match exactly.
Generated writes and the canary are also unchanged. No timing claim will rely on the short screen alone.

## Retained implementation and execution paths

The retained change primarily reduces allocation. Diagnostic timing supports an improvement for repeated short
records; production timing remains inconclusive, including a slower runtime median in the final run. First
runtime serialization in a fresh process costs about one additional millisecond. Parsing is unchanged.

- Runtime `Serialize`, caller spans, buffer writers, `Write` and the serialization phase of `WriteAsync` reach
  `WriteEngine.WriteText`. The dynamic count in `TextRecordLayout` exercises the general compiled engine. Its
  input is a complete `StructValue`, so normalization and the normal write traversal remain inside each operation.
  Bounded text still checks the string limit, UTF-16 capacity parity, strict byte count and field capacity in the
  original order. Payloads at most 512 bytes use stack storage. Larger payloads keep the allocating encoder.
  Payload and padding remain separate writes, including the empty-payload callback.
- Runtime wide fields of at most 256 UTF-16 code units stage the complete padded field in at most 512 stack bytes.
  They avoid both the encoded array and a padding string. Malformed input falls back to the original padded-string
  encoder, retaining its exact exception metadata. Larger fields use the original encoder. Stream writes stay
  outside the encoding exception handler, so an encoder exception thrown by a destination is not reclassified.
- Generated `WriteCursor.WriteBoundedText` validates through the original `GetByteCount` overload before reserving
  any destination bytes. The validated immutable string then encodes directly into the destination, followed by
  zero padding. This removes the temporary array for all bounded encodings, including CP437. Generated wide text
  already used its destination directly and is unchanged. No generator emission, snapshot or public API changed.

The shared runtime-only `BoundedTextCodec.EncodeValidated` helper avoids changing the generator's netstandard2.0
core sources. Only built-in immutable strict encodings are involved. User conversions, custom codec dispatch,
constructors, cancellation checks, traversal and budgets remain in their original locations. The generated
writer's validation still precedes reservation; the runtime writer still stages before submitting a whole payload.
Async output still owns the full serialized array and makes its existing single asynchronous write.

## Focused coverage and immutable baselines

The original capture has 86 cases. Six additional cases reuse existing inputs: four `TextOutputBenchmarks` methods
write the 256-record text fixture from **unpadded** strings to a caller span, buffer writer, synchronous memory
stream and asynchronous memory sink; two `MaterializationBenchmarks` methods serialize the existing 4 KiB text
fixture. The latter includes 1 KiB UTF-8/CP437 fields and a 512-code-unit wide field, covering the large fallback.
The benchmark source and inputs are byte-identical in the main, reference and candidate checkouts. Setup checks
every byte for all output forms. Returned arrays own complete output; reusable destinations receive every byte.
There is no checksum substitute, borrowed result or benchmark-specific product behavior.

`text-write-next-cases-baseline` captures all 92 cases on unchanged product `2aa33f54` with only these benchmark
additions. The original capture remains intact. Source checks: `artifacts/perf/text-write-next/benchmark-source-checks.json`.
The two new isolated checkouts are `C:/Users/vmvol/.codex/worktrees/text-write-next-{reference,candidate}/cstructsharp`.
All older worktrees and artifacts remain untouched.

These memory sinks measure library work, not disk latency or filesystem throughput. The existing cached async
file **read** cases remain controls. The handcrafted primitive-record benchmark does binary access and returns
a tiny result; it is only a canary here. There is no equivalent handwritten complete text serializer, and no
handcrafted speed ratio is claimed.

## Final allocation results

Bytes per warmed operation; repeated Screen/production counts agree. The existing independent cold probe also
confirms runtime array serialization at 68,640 → 25,632 bytes. Long diagnostic runs have small amortized harness
extras (for example 68,643 → 25,634); those raw counts remain in the reports.

| Operation | Before | Final | Interpretation |
| --- | ---: | ---: | --- |
| Runtime serialize, 1 padded record | 296 | 128 | Only the final 104-byte output array remains |
| Generated serialize, 1 padded record | 184 | 128 | Removes the UTF-8 temporary array |
| Runtime serialize, 256 padded records | 68,640 | 25,632 | Saves 43,008 bytes; only owned output remains |
| Generated serialize, 256 padded records | 39,968 | 25,632 | Saves 14,336 bytes |
| Runtime span, 256 unpadded records | 67,584 | 0 | Complete output written to caller storage |
| Runtime buffer writer, same records | 67,720 | 136 | Adapter overhead remains |
| Runtime stream, same records | 67,648 | 64 | Adapter overhead remains |
| Runtime async memory sink, same records | 93,216 | 25,632 | Full owned serialization output remains |
| Runtime serialize, mixed 4 KiB text | 7,800 | 7,800 | Large fields retain the original runtime path |
| Generated serialize, mixed 4 KiB text | 5,704 | 4,120 | Removes two 792-byte encoded arrays |
| Runtime / generated parse, 256 records | 86,192 / 63,544 | same | Opposite direction unchanged |

For a padded record the three eliminated arrays allocate 72 + 40 + 56 = 168 bytes. With unpadded strings, wide
padding strings add 112 bytes per record, while the shorter UTF-8 temporary is 16 bytes smaller: the eliminated
total becomes 264 bytes per record. This explains the exact 67,584-byte saving without attributing it to timing.
The existing narrow-character read scratch assertion above 256 code units is untouched and passes.

## Timing and variation

Windows 11, Ryzen 9950X, logical CPU 16; SDK 10.0.401 selected by `global.json`, .NET 10.0.12, Node 26.5.0.
All timing was serial with builds, tests and edits stopped. Affinity reduces migration but does not isolate the
machine. Every launch, actual sample, allocation observation, launch order and source/binary identity is retained.
No canary correction, favorable launch selection or smallest-median comparison was used.

The final diagnostic confirmation (`text-write-next-stack-confirm`) has three fresh pairs, thirty 100 ms
measurements per method, with tiering/PGO disabled. Times below are nanoseconds per operation. Paired deltas and
independently summarized side medians can differ when launches drift; ranges and spread are not confidence intervals.

| Method | Before | Final | Paired median Δ | Launch Δ range | Spread |
| --- | ---: | ---: | ---: | --- | ---: |
| Runtime serialize, 256 records | 38,531.19 | 32,793.50 | −14.8% | −16.4…−12.8% | 5.1% |
| Generated serialize, 256 records | 20,872.11 | 18,198.22 | −12.6% | −13.2…−12.5% | 1.3% |
| Runtime serialize, mixed 4 KiB | 2,672.76 | 2,701.10 | +0.3% | −1.6…+1.2% | 10.1% |
| Numeric canary | 2.19 | 2.18 | 0.0% | −0.5…+0.5% | 2.2% |

The short-record directions repeat; the large fallback is inconclusive. The final 11-case Screen
(`text-write-next-stack`, three pairs) covers tiny records and all output forms. Runtime write-1 has a paired
−10.0% change (−13.4…−9.0%, 8.3% spread); generated write-1 is −5.2% (−6.3…+2.1%, 10.2% spread). Span/buffer/stream/async
paired changes are −23.2/−18.1/−19.1/−20.3%, but all are unstable. These final output-form timings remain Screen
evidence; their allocation counts are reproducible. Longer confirmation of the earlier pooled prototype must not
be presented as an exact final-code timing result.

The final production run (`production-stack`) uses three fresh out-of-process pairs, tiering and PGO enabled,
30 warmups and fifteen 250 ms measurements. **Both writer rows are unstable.**

| Method | Before ns/op | Final ns/op | Paired median Δ | Launch Δ range | Spread |
| --- | ---: | ---: | ---: | --- | ---: |
| Runtime serialize, 256 records | 31,030.74 | 32,521.15 | +5.8% | −3.0…+64.5% | 61.9% |
| Generated serialize, 256 records | 15,064.11 | 13,789.45 | −8.5% | −44.7…−5.9% | 59.0% |
| Numeric canary | 2.19 | 2.21 | +0.6% | −2.8…+0.8% | 2.1% |

This does **not** establish a production CPU-speed improvement or equivalence. In particular, the runtime's
slower median is retained. The earlier pooled prototype's production run was also unstable, with a generated
pair at +77.3%; it remains under `production/`, not replaced by the final run. Stop measuring here: further
launches on this machine do not reliably resolve the variation. Retention is justified by substantial, exact
allocation savings, small bounded implementation and the diagnostic evidence, not by choosing favorable medians.

Parsing source is unchanged. The final 92-case Impact screen shows runtime record parsing 43,758.70 → 43,160.87 ns
(−1.4%, 31.4% spread) and generated parsing 29,523.53 → 29,966.67 ns (+1.5%, 19.9% spread), with identical allocation.
Both are inconclusive; no parsing speedup is claimed. Production parsing was not remeasured this round.

## Cold start, code size and memory

The reused `ordinary-next` probe separates schema preparation from first reads/writes and validates complete
values/bytes outside timing. `cold-stack/cold.json` retains 160 fresh-process observations (10 rounds × two sides ×
two tiering settings × four modes). Median milliseconds, with all observed ranges:

| Mode | Diagnostic before → final (ranges) | Production before → final (ranges) |
| --- | --- | --- |
| Compile | 152.002 → 151.997 (151.331–153.360 / 151.508–153.896) | 62.933 → 62.729 (62.381–63.594 / 62.232–62.938) |
| First read, 1 record | 61.347 → 61.334 (61.034–61.778 / 60.963–61.897) | 28.990 → 28.881 (28.898–29.094 / 28.713–29.175) |
| First read, 256 records | 61.208 → 61.318 (61.071–61.850 / 61.143–61.661) | 29.143 → 28.953 (28.956–30.003 / 28.829–29.305) |
| First write, 256 records | 29.597 → 29.974 (29.557–30.420 / 29.852–30.724) | 11.103 → 12.110 (11.055–11.229 / 12.048–12.357) |

The production first-write penalty is about **1.01 ms**. Its JIT component rises 10.403 → 11.451 ms; compiled IL
within the measured operation rises 13,619 → 13,765 bytes. Diagnostic first-write cost rises about 0.38 ms. These
are first writes in fresh processes, not measured per-schema costs. Compilation/read differences overlap normal
variation. No production break-even claim is possible with the noisy warmed measurements. As a diagnostic-only
illustration, 0.376 ms / 5.738 µs is about 66 repeated 256-record writes; this is not a portable workload threshold.

The Release library grows 900,096 → 900,608 bytes (+512 bytes including PE alignment). All **49 generated source
files, 1,615,159 bytes, are byte-identical**: `generated-comparison.json`. There is no per-schema generated growth.
Diagnostic native dumps for the memory writer show `WriteText` 1,598 → 1,743 bytes, plus 348 bytes for the new wide
helper and 199 for the validated encoder. These selected methods grow by 692 bytes; they do not measure total
process native code, framework helpers, the stream specialization or production PGO code size.

Final runtime staging adds at most 512 stack bytes per active helper and no new pool retention, executable cache
or cache lifetime/concurrency policy. Larger runtime arrays keep their original allocation behavior. Reduced
managed allocation is measured; peak working set, GC pauses, multicore scaling, NativeAOT, ARM and .NET 8 timing
are not. Generated first-write latency was not measured separately. Malformed short wide input does extra internal
encoding work before the original failure path; its externally returned diagnostics and mutation are verified.

## Prototype decisions and remaining candidates

The first bounded prototype used the existing stack/pool pattern for arbitrary runtime payloads; the wide
prototype added the bounded short-wide helper. Initial screens reduced runtime record allocation 68,640 → 54,304
and then → 25,632. The generated writer went 39,968 → 25,632 after the bounded change. All results are preserved.
The pool additionally saved 1,584 bytes on runtime mixed 4 KiB text, but longer diagnostic timing was only −1.4%
with canary drift; production variation was large. Retaining large pool buckets was unnecessary for the main
workload. The final version therefore limits runtime staging to the existing 512-byte stack budget and preserves
the large allocating path. The pooled source and its rental-return tests are archived under `pooled-prototype/`.
Functional reentry/failure tests cover both final paths; no pre-existing test was removed or weakened.

Next worthwhile experiments, not implemented in this round:

1. CP437 read materialization: keep the current owned byte copy but use it as state for direct string construction,
   potentially eliminating the character array without unsafe borrowed-span state. Measure one isolated mixed/high-byte
   case and all 256 byte values, on both runtimes; reject it if callback overhead consumes the benefit.
2. First-write overhead: profile the new helper JIT and strict span encoder on ordinary repeated writes before adding
   any schema specialization. Production timing needs a quieter machine or deployment-like sustained workload;
   repeating the same short launches here is not decisive.
3. A whole numeric executable block remains an architectural hypothesis. Require complete owned results, bounded
   lifetime/concurrency, AOT fallback and cold break-even evidence. Do not revive per-field delegates or the rejected
   six-byte prefix without a different measured workload.
4. Count retries, discarded graphs and observable codec/constructor calls in large buffered input before designing
   continuation state. Ordinary reads and the user-rejected numeric-section implementation remain unchanged.

## Impact, correctness and review

The full 92-case screen ran once for the combined pooled prototype and once after reducing it to the final stack-only
scope; it was not rerun unchanged to obtain a favorable result. Prototype address/primitive/sequence flags were
confirmed in `text-write-next-final-confirm` and did not repeat consistently. Final flags for primitive update,
generated small matrix parsing, mapped packet writing and POCO buffer output were confirmed in
`text-write-next-stack-impact-confirm`: paired changes −0.2%, +0.3%, −4.3%, −0.2%, respectively. The mapped packet
result remains unstable; the others are within margin. No consistent regression was established. This is not proof
of equivalence. The noisy unchanged async file-read screen (+35% for one record, 52.3% spread) remains inconclusive.
Small compilation allocation differences vary between fresh processes and overlap unchanged source controls;
no schema-memory improvement or regression is inferred from them.

Release solution build: **zero warnings/errors**. Full managed suite: **8,806 passed** (4,320 runtime tests per
framework, 47 parity tests per framework, 70 generator and two compiler tests). Results:
`artifacts/test-results/development/run-6s7jx1/`; build log: `artifacts/perf/text-write-next/release-build.log`.
All 18 new tests also pass against untouched reference product on both runtimes (`reference/.../run-lDaTRg/`).

The new golden manifest contains 7,128 baseline outcomes: complete written bytes, positions, span/array callbacks,
partial stream failures, competing limits, invalid encodings and all encoding-specific exception fields. It was
recorded only on untouched `2aa33f54`, then compared on the candidate. An initial new-test setup used unsupported
anonymous writable objects; this was corrected to dictionaries on both sides before recording the authoritative
traces. The rejected setup's traces and failed logs are preserved, not treated as evidence. No existing golden
manifest or generated-source snapshot changed.

Independent value/byte tests cover all five bounded encodings, wide LE/BE, empty/NUL/padded values, exact staging
boundaries, large fallbacks, owned arrays, spans, buffer writers, streams, async output and reentrant stream calls.
Three deliberately planted faults in the isolated candidate (CP437 bytes, wide padding and generated capacity
comparison) were caught through assertion failures, not compilation failures. Sources were restored afterward.
Logs: `mutations/`. This focused fault check is not a full Stryker score.

API baseline revision 62, canonical reference, all 48 feature fixtures, 14 generated-layout checks and changed
declaration documentation pass. Existing exhaustive engine, cancellation, ownership, conversion and codec tests
pass unchanged. Source comments, benchmark guidance and CHANGELOG are updated. No authored DocFX, app, browser,
API contract or fixture changes require extra application builds. The complete diff and local links are reviewed.
This round was measured before committing; the runtime tiering issue is resolved in the
[follow-up investigation](../runtime-text-slowdown/README.md).

## Reproduction and artifact index

Use the pinned Node version and SDK. These are the recorded commands; captures/output names are immutable, so use
fresh names for a rerun. Capture before editing product code. For supplementary cases, copy only identical benchmark
sources into the reference checkout, leaving its `src/` at `2aa33f54`.

```powershell
$reference = 'C:/Users/vmvol/.codex/worktrees/text-write-next-reference/cstructsharp'
$candidate = 'C:/Users/vmvol/.codex/worktrees/text-write-next-candidate/cstructsharp'
node tools/quality/perf-check.mjs --capture text-write-next-baseline
node tools/quality/perf-check.mjs --capture text-write-next-cases-baseline --checkout $reference
node tools/quality/perf-check.mjs --baseline text-write-next-cases-baseline --rounds 3 --cpu 16 --label text-write-next-stack --filter '*TextRecordBenchmarks.*Serialize*' --filter '*TextOutputBenchmarks*' --filter '*MaterializationBenchmarks.*Text_Serialize*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'
node tools/quality/perf-check.mjs --baseline text-write-next-cases-baseline --confirm --cpu 16 --label text-write-next-stack-confirm --filter '*TextRecordBenchmarks.*Serialize*256*' --filter '*MaterializationBenchmarks.Runtime_Text_Serialize*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'

dotnet build "$reference/benchmarks/experiments/ordinary-next/OrdinaryNext.csproj" -c Release
dotnet build "$candidate/benchmarks/experiments/ordinary-next/OrdinaryNext.csproj" -c Release
node benchmarks/experiments/ordinary-next/cold.mjs $reference $candidate artifacts/perf/text-write-next/cold-stack
$env:PERF_WARMUPS = '30'
node benchmarks/experiments/runtime-generated/production.mjs $reference $candidate artifacts/perf/text-write-next/production-stack '*TextRecordBenchmarks.*Serialize*256*' '*GeneratedBenchmarks.HandWritten_PrimRecord*'
Remove-Item Env:PERF_WARMUPS

node tools/quality/perf-check.mjs --baseline text-write-next-cases-baseline --cpu 16 --label text-write-next-stack-impact
node tools/quality/perf-check.mjs --baseline text-write-next-cases-baseline --confirm --cpu 16 --label text-write-next-stack-impact-confirm --filter '*GeneratedBenchmarks.Runtime_PrimRecord_Update*' --filter '*MaterializationBenchmarks.Generated_Matrix16_Parse*' --filter '*PacketBenchmarks.SerializeMapped*' --filter '*WriteBenchmarks.Serialize_Prim_Poco_ToBufferWriter*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'

dotnet build CStructSharp.NonWeb.slnf -c Release
node tools/quality/test-managed.mjs --full --no-build
node tools/quality/managed-api-baseline.mjs compare
node tools/documentation/validate-canonical-reference.mjs
node tools/quality/feature-operation-matrix.mjs
node --test tools/quality/benchmark-generated-layouts.test.mjs
node tools/quality/changed-documentation.mjs --base HEAD --language csharp
git diff --check
```

Diagnostic runs are under `artifacts/perf/development/runs/text-write-next-*`: `aa`, `bounded`, `wide`, `outputs`,
`confirm`, `impact`, `final-confirm` belong to the initial/prototype phase; `stack`, `stack-confirm`, `stack-impact`,
`stack-impact-confirm` describe final code. Production, both 160-observation cold campaigns, native dumps, generated
source comparisons, prototype copies, mutation results and build logs are under `artifacts/perf/text-write-next/`.
Each performance report retains its exact launch arguments and environment, every raw BDN sample and source hashes.
