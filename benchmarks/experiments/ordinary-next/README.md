# Runtime text and ordinary workloads

The numeric-section generator optimization was removed at the user's request; its code, tests, benchmarks,
probe sources and results remain archived under `artifacts/perf/execution-next/removed-numeric-sections/`.
Product code starts at `81ddaab0`. `ordinary-next-baseline` was captured after removal and before new hot-path
edits. Existing experiments, previous worktrees and artifacts are preserved. Earlier timings are context only.

## Ranked hypotheses before implementation

| Rank | Mechanism and workload | Evidence / expected benefit / effort | Correctness and maintenance risks | Smallest experiment |
| --- | --- | --- | --- | --- |
| 1 | Runtime `wchar[N]` from memory: decode whole owned string | `ReadCharacters` allocates a character array and boxes each code unit; generated `DecodeWideCharacters` already provides equivalent owned decoding. Large expected CPU/allocation saving; small reuse | Preserve byte-order, invalid UTF-16 encoder metadata, trimming, partial unit and budget failure position; leave physical stream reads unchanged | Existing mixed text fixture, both trim settings, then small/multilingual/both-order tests and scalar controls |
| 2 | Runtime bounded UTF/Latin-1: remove input byte copy | `ReadBoundedText` always allocates `byte[byteCount]`; generated span decoder already preserves malformed-input metadata. Saving one field-sized array; small memory-cursor specialization | String-limit ordering, short reads, budget consumption, debug and buffered continuation; owned string remains mandatory | Isolated change on existing runtime/generated text cases and exact error equivalence |
| 3 | Runtime text serialization: pooled encoding storage | Bounded fields allocate encoded arrays; `wchar[N]` also pads into an intermediate string. Potential substantial memory saving for text-rich records; moderate effort | Stream callback overloads and partial writes, exact inner exceptions and invalid-input validation order; pooling does not remove output ownership | First inspect destination callback contracts; prototype one complete field with byte/call-trace equivalence before timing |
| 4 | Shared CP437 decoding into final string | Array overload allocates characters; generated span overload first copies bytes. Useful for OEM names but less common; moderate complexity with net8 span/string APIs | Mapping all 256 bytes, ownership, compiler target support; avoid unsafe-only helper merely for one codec | Small allocation prototype if text work leaves worthwhile headroom |
| 5 | Runtime whole numeric executable blocks (architecture) | Static plans still dispatch and box; prior per-field delegates failed. Potential repeated-record CPU saving; high effort | AOT fallback, schema preparation and JIT break-even, concurrency, cache/code growth | One bounded block producing the same full `StructValue`; reject unless savings repay measured preparation cost |
| 6 | Generated writer sections / generated reader fallback extraction | Repeated reservations / larger fallback body exist; numeric-section reader rejected for ordinary-workload relevance | Getter order, conversions, aliasing, partial writes, new code/JIT growth | Defer section family; require a demonstrated ordinary workload before revisiting |
| 7 | Buffered resumable parsing | Default window often covers input; no evidence ordinary fixtures discard graphs | Codec/constructor replay and stream interactions observable; high architecture cost | Count actual retries and discarded bytes in a representative sparse workload before changing execution |

The existing `char[N]` scratch-storage assertion above 256 code units is preserved. It does not apply to
`wchar[N]` or byte-counted UTF fields. No borrowed public result, cache or new execution backend is proposed.

## Initial correctness and measurement

Fresh development baseline: runtime 3,100, generator 70, generated parity 47, compiler compatibility 2 passed
(`artifacts/test-results/development/run-O59miJ`). The final full validation is recorded below.

## Retained implementation and paths

Only two existing runtime methods change. The generator, schema compiler, writer, plan representation and public
API are unchanged. Both changes reuse the existing generated text-decoding helpers; they add no cache, delegate,
new execution backend or per-schema generated body.

- `ReadWideCharArray` uses the existing memory-extent/budget guard for complete `wchar[N]` and counted wide fields.
  It decodes directly into the final owned string, avoiding a temporary character array and one boxed `char` per
  code unit. Short input, insufficient byte budget, oversized extents and physical stream sources use the original
  per-character reader. Invalid UTF-16 still recreates the original encoder exception, including its metadata.
  Debug wide-character recording retains its per-character path and records.
- `MemoryReadCursor.ReadBoundedText` decodes complete bounded text directly from its input span after the same
  string-limit check and byte charge. Short input and string-limit rejection use the original reader. Invalid
  UTF input is copied only on the failure path to reproduce the array decoder's exact exception. CP437 still uses
  its original temporary storage; no CP437 allocation improvement is claimed.

`MaterializationBenchmarks.Runtime_Text*` uses the general engine: a 4 KiB record with fixed Latin-1 characters,
UTF-8, CP437 and wide text. Generated forms use their already optimized helpers and are unchanged controls.
The new `TextRecordBenchmarks` returns one or 256 full owned records, each with a number, a 24-code-unit LE name,
an eight-code-unit BE tag and 32 UTF-8 bytes. Text includes accented Latin, CJK, surrogate pairs and NUL padding.
Every value, byte and ownership result is checked outside timing against manually constructed input.

The synchronous non-exposable stream calls remain unchanged; their per-character reads can execute user code.
The file case opens a real `FileStream` with asynchronous I/O, then uses the existing buffered async parser.
Its repeated reads have a warm operating-system cache; this is not uncached storage or a real filesystem decoder.
Buffer sizes, replay, physical reads, cancellation and restoration behavior were not changed.

## Measurements and variation

All timings ran serially on CPU 16, with builds, tests and agent work stopped. Affinity does not isolate the
machine. Windows 11 / Ryzen 9 9950X, SDK 10.0.401 selected by `global.json`, .NET 10.0.12, Node 26.5.0.
Diagnostic jobs disable tiering/PGO. Production uses the existing out-of-process runner, tiering/PGO enabled,
30 warmups and 15 measured 250 ms iterations, three fresh launches per side. Every launch and sample is retained.

`ordinary-next-baseline` contains the original 74 cases and precedes edits. `ordinary-next-cases-baseline` is a
supplementary 86-case capture from untouched product revision `81ddaab0` with the identical two new benchmark
source files. Both captures remain immutable. The new cases add coverage, not a changed benchmark implementation
between compared sides.

The initial six-case identical-code Screen was unstable: runtime mixed text had paired deltas -4.7…+1.6%, while
unchanged generated mixed text appeared -24.1…-0.1% faster. The canary stayed within the margin. This prevents
attributing arbitrary changes in generated or small-control medians to the prototype.

Separate prototype screens establish the allocation contribution of each change:

| Existing mixed-text runtime parse | Allocated B/op | Screen paired time delta / all pairs |
| --- | ---: | --- |
| Baseline | 26,952 | A/A -2.7% (-4.7…+1.6%) |
| Wide text alone | 13,616 | -77.8% (-80.0…-76.9%) |
| Wide plus bounded text | 12,568 | -77.0% (-77.5…-76.9%) |

These are separate noisy screens, not evidence that the bounded change makes the wide change slower. The bounded
change removes one 1,048-byte UTF-8 input array in this fixture. The wide change removes 512 boxes (12,288 bytes)
and a 1,048-byte character array. The implementation was not tuned to obtain a favorable launch.

Times below are ns/op, medians of launch medians. Deltas are medians of paired ratios, so they need not equal the
ratio of the displayed medians. Ranges and spread are observations, not confidence intervals.

| Runtime diagnostic Confirm | Before → after | Paired delta (all pairs) | Spread |
| --- | ---: | --- | ---: |
| Mixed text parse | 5,267.65 → 1,032.41 | -80.4% (-80.7…-80.1%) | 2.4% |
| Mixed text parse, trimmed | 5,987.81 → 1,725.70 | -71.2% (-71.3…-71.1%) | 31.5% |
| One short record parse | 613.84 → 368.87 | -40.2% (-40.7…-38.4%) | 4.1% |
| 256 short records parse | 111,358.31 → 45,159.53 | -59.5% (-59.8…-59.3%) | 5.2% |
| Async file, one record | 12,101.22 → 11,828.42 | -2.4% (-2.5…-1.6%) | 3.1% |
| Async file, 256 records | 126,189.81 → 58,383.64 | -53.7% (-54.2…-53.6%) | 3.2% |
| Serialize one record, control | 358.95 → 355.54 | -0.9% (-2.2…-0.5%) | 4.8% |
| Serialize 256 records, control | 39,873.04 → 39,893.19 | +0.1% (-4.8…+0.3%) | 4.9% |

The diagnostic canary was +0.4% (-1.3…+1.0%), spread 3.6%. Mixed trimmed text remains unstable. The other large
parse differences repeat beyond the practical margin without that warning; this is descriptive evidence, not
suite-adjusted statistical confidence. The one-record async case has no demonstrated timing benefit.

| Runtime production | Before → after | Paired delta (all pairs) | Spread |
| --- | ---: | --- | ---: |
| Mixed text parse | 5,603.40 → 852.45 | -85.0% (-85.6…-82.7%) | 17.9% |
| One short record parse | 469.83 → 271.63 | -42.2% (-50.4…+1.3%) | 93.9% |
| 256 short records parse | 97,104.49 → 35,121.84 | -63.8% (-64.1…-63.0%) | 8.7% |
| Async file, 256 records | 117,754.75 → 52,356.78 | -55.8% (-63.1…-55.0%) | 26.2% |
| Serialize 256 records, control | 30,718.79 → 31,209.46 | +2.7% (-2.6…+3.3%) | 6.8% |

All production rows are marked unstable by the tool. In particular, one small-record pair reversed direction.
The large repeated-record effect is consistent across production and diagnostic processes, supported by removed
work and exact allocation savings; the displayed percentages are not precise portable speedup claims. Production
canary: -1.3% (-3.5…-0.3%), spread 3.0%, within its median margin. No canary correction is applied.

| Allocation, B/op | Before → after | Evidence |
| --- | ---: | --- |
| Runtime mixed text / trimmed | 26,952 → 12,568 / 32,424 → 18,040 | Every diagnostic and measured production launch |
| Runtime one / 256 short records | 1,448 → 512 / 325,808 → 86,192 | Screen and independent warmed thread counters |
| Runtime serialization one / 256 records | 296 → 296 / 68,640 → 68,640 | Screen, confirmation and warmed counter for 256 |
| Runtime physical stream one / 256 records | 1,616 → 1,616 / 325,976 → 325,976 | All three Screen pairs |
| Runtime async file 256 records | approximately 326,300 → 86,700 | Diagnostic and production; async/harness overhead varies |
| Generated parse one / 256 records | 304 → 304 / 63,544 → 63,544 | All three Screen pairs |
| Generated serialize one / 256 records | 184 → 184 / 39,968 → 39,968 | All three Screen pairs |
| Generated mixed text / trimmed | 10,432 → 10,432 / 13,832 → 13,832 | All original-text screens |

Long runs sometimes report a few amortized diagnostic bytes (for example 325,816 rather than 325,808); they are
retained in raw reports and are not treated as different result ownership. The 936-byte short-record saving is
768 bytes of character boxes, 112 bytes of character arrays and 56 bytes of UTF-8 input storage, repeated 256 times.
Peak process memory, native allocations and garbage-collection latency were not measured.

Generated execution has no new optimization or claimed speedup. Its 256-record Screen parsing was 29,432 → 29,914 ns
(+1.7%, pairs +1.2…+3.3%, spread 39.7%); serialization 21,083 → 20,910 ns (-1.2%, pairs -1.6…+16.6%, spread 17.5%).
The unchanged tiny handwritten primitive checksum is only a drift canary. It does not perform equivalent owned
text materialization or validation, so no handcrafted relative-speed claim is made.

## Compilation, first use, code size and memory

Ten fresh processes per side, operation and configuration produced 160 cold observations. Read measurements begin
after schema construction and include lazy read-program preparation/JIT. Compile measurements begin before the
first schema preparation and include helper initialization. They exclude whole-process startup. Writes use a
complete prepared graph but no prior writer call. Each output is checked after timing.

| First operation, ms | Diagnostic before → after (ranges) | Production before → after (ranges) |
| --- | --- | --- |
| Compile schema | 152.715 → 151.724 (151.229–153.977 / 151.338–154.171) | 62.879 → 62.883 (62.242–64.024 / 62.627–64.060) |
| Parse one record | 64.245 → 61.433 (63.894–64.963 / 60.894–62.033) | 29.315 → 29.078 (29.193–29.920 / 28.621–29.553) |
| Parse 256 records | 64.492 → 61.494 (64.134–64.844 / 61.143–65.663) | 29.791 → 29.082 (29.673–30.480 / 28.737–29.299) |
| Serialize 256 records | 29.429 → 29.630 (29.246–29.577 / 29.538–29.970) | 11.017 → 11.097 (10.947–11.079 / 11.053–11.316) |

These observations show no consistent added first-read or schema-compilation penalty. Small shifts in unchanged write
and compilation code are not claimed as changes. Production read JIT medians were 28.034 → 27.761 ms for one record
and 27.928 → 27.696 ms for 256. There is no measured startup cost needing a per-schema throughput break-even.

All 49 generated benchmark files are byte-identical (`generated-comparison.json`). The Release runtime DLL remains
900,096 bytes on both sides; PE file-size rounding does not establish identical IL size. Runtime implementation adds
one guarded branch to each of two shared methods, with no per-schema code or retained cache. Targeted native dumps
show different inlining: the old wide helper disappears into its caller while the new helper has a standalone body.
Their emitted method totals therefore cannot fairly measure total native code growth; the dumps are retained without
claiming a native-memory reduction. There is no new AOT-incompatible mechanism, but no AOT or ARM performance run.

## Combined Impact and rejected/deferred work

The complete 86-case Impact screen ran once. It included tiny records, large arrays, nested/dynamic records,
strings, BE, bitfields, unions, pointers, typed/dynamic results, caller spans, buffer writers, updates and async.
It flagged generated primitive serialization (+17.4%) and terminated strings (+11.2%), with a noisy generated
view enumerator (+32.7%). Targeted Confirm found respectively -0.4% (-0.6…+0.6%), +1.2% (-9.1…+5.0%), and -0.0%
(-1.1…+0.9%), with unchanged allocations. No repeatable regression was established; terminated strings and the view
remain timing-inconclusive. Minor allocation variation also appeared in unchanged compile/generated-array cases;
no memory improvement is claimed there. Measurement stopped without rerunning the broad suite or chasing medians.

- **Removed:** numeric generated sections and their dedicated cases/tests, at the user's request. Historical code,
  timings, cold penalty and measured checkouts are retained. Fallback extraction for this removed feature is not pursued.
- **Already rejected, not repeated:** pooled block-copy removal, per-field runtime delegates and the tiny guarded
  six-byte prefix. There is no new evidence that justifies repeating them.
- **Next concrete write candidate:** `WriteText` allocates complete encoded arrays for bounded and wide strings;
  wide padding can add a string. The new 256-record serialization control allocates 68,640 B/op, of which source
  inspection accounts for 43,008 bytes in three temporary encoded arrays per record. Prototype pooled encoding into
  exactly the same single-field span; preserve `GetByteCount`/validation order, span/array stream callback overloads,
  partial writes, nested writes during callbacks and exact encoder exceptions. Measure owned, caller-span, buffer
  writer and throwing-stream cases before retaining it. No writer behavior was changed in this round.
- **Shared CP437:** the existing 1 KiB OEM field retains a byte copy and character array. A span-to-owned-string
  implementation must work on both frameworks without excessive unsafe or custom-encoding machinery; test all
  256 values and measure non-ASCII workloads first. The legacy narrow-character scratch assertion remains intact.
- **Executable blocks / continuations:** remain lower priority than the demonstrated text costs. Require the
  preparation/concurrency/AOT evidence or actual replay/callback counts in the ranked table before building them.

The new repeated-record workload is synthetic. An actual target disk format, mixed schema workload, uncached
storage, many concurrent readers, async failures under realistic device latency and .NET 8 performance remain
unmeasured. Normal memory/buffered parsing of wide or bounded text benefits; numeric-only and ordinary physical
stream character reads do not gain from these changes. No whole disk-scan percentage is implied.

## Reproduction and artifacts

Run from the repository root, with the Node version in `.node-version` on PATH. Captures and output directories
must be fresh: the recorded names below already exist and must not be overwritten. Keep the untouched reference
at `81ddaab0`; add only the identical benchmark/probe sources to it. Candidate contains the same files plus the
two runtime edits. The original pre-edit capture has 74 cases; the supplementary capture has 86.

```powershell
$reference = 'C:/Users/vmvol/.codex/worktrees/ordinary-next-reference/cstructsharp'
$candidate = 'C:/Users/vmvol/.codex/worktrees/ordinary-next-candidate/cstructsharp'

# Recorded before any runtime edits, with the original benchmark sources:
node tools/quality/perf-check.mjs --capture ordinary-next-baseline
node tools/quality/perf-check.mjs --baseline ordinary-next-baseline --rounds 3 --cpu 16 --label ordinary-next-aa --filter '*MaterializationBenchmarks.*Text*' --filter '*TextWriteBenchmarks.SerializeTerminatedUtf8*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'
# Repeat that filter after the isolated wide edit, then the bounded edit, using labels
# ordinary-next-wide and ordinary-next-bounded. Keep every report.

# Unchanged product with identical added TextRecordBenchmarks and TextRecordLayout:
node tools/quality/perf-check.mjs --capture ordinary-next-cases-baseline --checkout $reference
node tools/quality/perf-check.mjs --baseline ordinary-next-cases-baseline --rounds 3 --cpu 16 --label ordinary-next-records --filter '*TextRecordBenchmarks*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'
node tools/quality/perf-check.mjs --baseline ordinary-next-cases-baseline --confirm --cpu 16 --label ordinary-next-confirm --filter '*TextRecordBenchmarks.Runtime_Parse*' --filter '*TextRecordBenchmarks.Runtime_FileAsync*' --filter '*TextRecordBenchmarks.Runtime_Serialize*' --filter '*MaterializationBenchmarks.Runtime_Text*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'

dotnet build "$reference/benchmarks/experiments/ordinary-next/OrdinaryNext.csproj" -c Release
dotnet build "$candidate/benchmarks/experiments/ordinary-next/OrdinaryNext.csproj" -c Release
node benchmarks/experiments/ordinary-next/cold.mjs $reference $candidate artifacts/perf/ordinary-next/cold
$env:PERF_WARMUPS = '30'
node benchmarks/experiments/runtime-generated/production.mjs $reference $candidate artifacts/perf/ordinary-next/production '*MaterializationBenchmarks.Runtime_Text_Parse*' '*TextRecordBenchmarks.Runtime_Parse*' '*TextRecordBenchmarks.Runtime_FileAsync*256*' '*TextRecordBenchmarks.Runtime_Serialize*256*' '*GeneratedBenchmarks.HandWritten_PrimRecord*'
Remove-Item Env:PERF_WARMUPS

node tools/quality/perf-check.mjs --baseline ordinary-next-cases-baseline --cpu 16 --label ordinary-next-impact
node tools/quality/perf-check.mjs --baseline ordinary-next-cases-baseline --confirm --cpu 16 --label ordinary-next-impact-confirm --filter '*GeneratedBenchmarks.Generated_PrimRecord_Serialize*' --filter '*ImpactParseBenchmarks.ParseSpan*strings-1024*' --filter '*SequenceBenchmarks.Generated_Records256_ViewEnumerator*' --filter '*GeneratedBenchmarks.HandWritten_PrimRecord*'
```

Diagnostic reports, wall times, source/binary hashes, launch order, allocation counts and original full BDN samples:
`artifacts/perf/development/runs/ordinary-next-{aa,wide,bounded,records,confirm,impact,impact-confirm}/`.
The bundles are `artifacts/perf/development/bundles/ordinary-next-baseline` and `ordinary-next-cases-baseline`.
Production, cold, generated-source comparisons and the Release build log are under `artifacts/perf/ordinary-next/`.
`cold/cold.json` retains all 160 observations; `*-allocations.json` are independent warmed counters.
The two isolated checkouts remain available and all earlier investigation checkouts/artifacts remain untouched.

## Correctness and delivery

The complete Release non-web build passed with zero warnings/errors. Full managed suite: **8,770 passed**:
4,302 runtime tests on each framework, 47 generated parity tests on each, 70 generator driver and two compiler
compatibility tests. Logs: `artifacts/test-results/development/run-7iVMvE/`; Release log:
`artifacts/perf/ordinary-next/release-build.log`. The 13 new text-equivalence cases also passed against unchanged
reference product code on .NET 8 and .NET 10 (`ordinary-next-reference/.../run-jWbmg8/`).

Coverage includes complete values and ownership after input mutation, LE/BE and aliases, multilingual text,
BOM/NUL preservation, trimming, selected reads, empty arrays, every small truncation and total-budget boundary,
per-string limit precedence and all inner encoder/decoder failure fields. Existing full engine/cursor/buffered
and cancellation suites passed without changed golden outcomes or assertions. The narrow-character scratch test
above 256 code units is unchanged and passes. The independent matrix ownership regression from the rejected
block-copy investigation is retained.

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
```

All applicable checks passed: API revision 62 unchanged, canonical contracts and 48 feature fixtures, 14 generated
layout checks, changed declaration documentation (10 C# / four script files). Source comments, benchmark guidance
and CHANGELOG were updated. No authored `docs/`, application or browser bridge source changed; unrelated app and
DocFX builds were not required. Local Markdown links and identical benchmark/probe source across both checkouts were verified. The complete diff
was reviewed. No commit, push, publication or release was made.
