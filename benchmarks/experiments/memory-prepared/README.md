# Prepared memory operations and import investigation

This synthetic probe separates repeated execution from preparation, binding, first use and expected failure.
It accepts no memory-image path. Production projection modes exercise ImportProjection; the restricted prototype
is retained only as the explicitly named `prototype` comparison mode.

## Reproduce

From the repository root, build both executables before measuring:

```powershell
dotnet publish benchmarks/experiments/memory-prepared -c Release -p:PublishAot=false -o artifacts/memory-jit
dotnet publish benchmarks/experiments/memory-prepared -c Release -r win-x64 -p:MemoryPublishAot=true -o artifacts/memory-aot
artifacts/memory-jit/MemoryPrepared.exe bound 200000
artifacts/memory-aot/MemoryPrepared.exe bound 200000
artifacts/memory-jit/MemoryPrepared.exe bind 10000
artifacts/memory-jit/MemoryPrepared.exe bound 1 cold
```

Set `MemoryReferenceAssembly` to an immutable captured CStructSharp assembly to compile the common reference
workloads. The conditional reference build excludes new APIs. Do not set the global `PublishAot=true` property
for this probe: `MemoryPublishAot` enables it on the executable without applying it to its source-generator dependency.

## Method and retained evidence

Measured on 27 October 2026, Windows x64, AMD Ryzen 9 9950X, SDK 10.0.403, .NET 10.0.14 and Native AOT
compiler/runtime pack 10.0.14. Release JIT uses default tiering and PGO. Affinity is unrestricted. The OS cache
is left alone. Builds, tests, edits and other measurements were kept outside the timing runs.

Five serial alternating process pairs per comparison retain every launch and consume every result in a checksum.
Each process warms `min(iterations, 2000)` operations, then measures its complete operation loop. Scalar reads
use 200,000 iterations; owned/caller arrays 1,000; first imports and preparation 50; overlapping imports,
descriptions and projections 100; binding 10,000; absent reads 5,000. The first-use control performs one read
without operation warmup in each of ten fresh processes. Binding and schema setup before that read are excluded.

The initial table below records the restricted prototype before the public projection implementation.
Its projection rows describe that restricted control, not the current `projection` command.
The table reports the mean and full range of five loop averages, in ns/op. Allocation comes from
`GC.GetAllocatedBytesForCurrentThread` around that loop. Startup is excluded. Caller destinations are allocated
before timing; their storage still belongs to the caller. Default operation contexts remain in each read cost.
Small first-import allocation variations are retained, not rounded away. Timings are descriptive local samples,
not confidence intervals or performance guarantees.

Raw final launches and summaries are retained locally in `artifacts/improvement/measurements-final/`;
the serial driver is `artifacts/improvement/measure-final.py`. Earlier exploratory samples remain separately in
`artifacts/improvement/measurements/`. The immutable 0.16.0 reference bundle is
`artifacts/perf/development/bundles/memory-api-20261027/`. Recreate these ignored outputs before comparing
on another machine.

| Runtime / operation | Before mean (range), ns/op | After mean (range), ns/op | B/op before → after |
| --- | ---: | ---: | ---: |
| JIT / Ordinary scalar, 0.16 baseline → candidate | 462.1 (457.5–470.4) | 494.3 (488.1–507.5) | 312 → 312 |
| JIT / Owned 4096-byte array, baseline → candidate | 99,789.0 (97,674.5–102,639.7) | 99,567.5 (97,796.2–104,149.5) | 4304 → 4304 |
| JIT / New metadata + first 256-type import, baseline → candidate | 2,193,875.2 (2,179,794.0–2,204,400.0) | 2,176,005.2 (2,159,298.0–2,197,950.0) | 1009485–1009805 → 1010317–1010637 |
| JIT / Warm overlapping import, baseline → candidate | 1,236,578.0 (1,231,612.0–1,245,471.0) | 1,200,797.8 (1,193,572.0–1,205,543.0) | 802597–802917 → 773509 |
| JIT / Candidate ordinary scalar → bound scalar | 490.7 (486.8–494.1) | 279.7 (277.1–285.7) | 312 → 64 |
| JIT / Candidate owned array → caller array, elementwise | 101,334.3 (98,699.1–103,998.1) | 97,665.2 (96,332.8–100,697.2) | 4304 → 64 |
| JIT / Candidate caller array, elementwise → bulk | 104,214.2 (96,482.8–133,263.3) | 88,757.9 (88,455.5–89,341.6) | 64 → 64 |
| JIT / Candidate full import → shallow description | 1,191,865.6 (1,189,758.0–1,193,656.0) | 1,437.8 (1,363.0–1,533.0) | 773509 → 656 |
| JIT / Candidate full import → restricted projection | 1,207,495.2 (1,198,394.0–1,211,429.0) | 38,268.4 (34,031.0–49,713.0) | 773509 → 33792–33952 |
| JIT / 256-type import + 1000 reads → projection + 1000 reads | 1,463,140.8 (1,446,618.0–1,478,680.0) | 281,252.8 (278,742.0–284,984.0) | 838047 → 99048–99528 |
| JIT / 4-type import + 1000 reads → projection + 1000 reads | 271,840.0 (269,626.0–275,650.0) | 283,403.2 (278,298.0–288,714.0) | 79280 → 99208–99528 |
| JIT / Ordinary throwing absent read → bound TryRead | 3,148.0 (3,112.7–3,186.6) | 282.8 (278.4–291.9) | 1104 → 288 |
| AOT / Ordinary scalar, 0.16 baseline → candidate | 142.9 (139.9–145.4) | 143.3 (136.9–153.9) | 312 → 312 |
| AOT / Owned 4096-byte array, baseline → candidate | 111,805.9 (110,748.8–112,252.4) | 111,952.1 (110,667.7–112,694.5) | 4304 → 4304 |
| AOT / New metadata + first 256-type import, baseline → candidate | 595,390.0 (587,804.0–614,122.0) | 608,376.8 (599,984.0–614,862.0) | 1022235–1022555 → 1023227–1023387 |
| AOT / Warm overlapping import, baseline → candidate | 416,776.8 (405,703.0–439,990.0) | 404,038.2 (402,345.0–407,314.0) | 815272–815592 → 786040 |
| AOT / Candidate ordinary scalar → bound scalar | 138.4 (137.0–139.6) | 46.5 (46.3–47.1) | 312 → 64 |
| AOT / Candidate owned array → caller array, elementwise | 112,101.7 (111,352.0–112,606.8) | 102,820.4 (102,555.7–103,017.8) | 4304 → 64 |
| AOT / Candidate caller array, elementwise → bulk | 102,815.2 (102,204.8–103,277.2) | 20,491.3 (20,446.3–20,527.4) | 64 → 64 |
| AOT / Candidate full import → shallow description | 405,668.8 (398,719.0–414,937.0) | 442.4 (438.0–450.0) | 786040 → 656 |
| AOT / Candidate full import → restricted projection | 418,201.8 (408,978.0–434,024.0) | 16,512.0 (16,228.0–17,168.0) | 786040 → 33984–34144 |
| AOT / 256-type import + 1000 reads → projection + 1000 reads | 443,340.8 (438,524.0–446,170.0) | 60,908.4 (60,046.0–61,778.0) | 850630 → 99400–99560 |
| AOT / 4-type import + 1000 reads → projection + 1000 reads | 55,833.6 (53,594.0–59,180.0) | 63,617.6 (60,424.0–69,724.0) | 79456 → 99400–99720 |
| AOT / Ordinary throwing absent read → bound TryRead | 1,398.9 (1,347.3–1,477.0) | 105.1 (99.5–116.9) | 880 → 288 |

Binding and first use are independent controls; their two identically configured sides are combined below.

| Runtime / control | Mean (range), ns/op | B/op |
| --- | ---: | ---: |
| JIT / Bind one scalar reader | 1,364.7 (1,331.9–1,417.6) | [496] |
| JIT / First bound read, zero operation warmups | 1,690,350.0 (1,608,100.0–2,191,400.0) | [64] |
| AOT / Bind one scalar reader | 249.2 (234.6–271.9) | [496] |
| AOT / First bound read, zero operation warmups | 3,340.0 (2,900.0–4,200.0) | [104] |

## Interpretation and transport

Prepared scalar reads remove repeated path work and numeric boxing. With a caller-supplied context, focused
regression tests observe no warmed intermediate allocation. The table includes a new 64-byte context per call.
The absent-read comparison combines preparation with non-throwing outcomes; it does not isolate exception
unwinding alone. No universal speedup is claimed for legacy reads: the default-tiered JIT ordinary-scalar
comparison was about 7% slower, while its AOT samples overlapped. First ISF import showed no clear timing gain
and pays for the bounded cache; warm overlapping imports reduce allocation by about 29 KiB.

The 4096-byte caller-array case uses the same byte checksum in both modes:

| Operation | Context work requests | Leaf reads | Leaf bytes requested |
| --- | ---: | ---: | ---: |
| Ordinary scalar | 2 | 1 | 4 |
| Bound scalar | 2 | 1 | 4 |
| Caller array, elementwise | 8193 | 4096 | 4096 |
| Caller array, explicit bulk | 3 | 1 | 4096 |

Context work requests include zero-byte validation/iteration work. Bulk intentionally changes transport and
failure boundaries. The warmed shared staging pool avoids a new 4096-byte staging allocation per operation;
pool storage is not a caller-owned result or a promise of zero retained memory. A page-translating source can
return short prefixes and cause additional reads and charges. These flat synthetic counts do not predict Linux
page-table traffic.

The documented performance screen covered 111 existing Impact cases. Targeted confirmation covered all eight
existing memory cases, with three fresh launches per side on logical CPU 16, default guide controls and
retained raw BDN data. All eight results remained inconclusive under the guide: allocation counts were stable
for runtime reads, while tiny compiler-allocation variations and timing spread prevent broader claims.
Reports are retained locally under:

- `artifacts/perf/development/runs/2026-10-27T22-04-32-850Z-2247a6/` (screen).
- `artifacts/perf/development/runs/2026-10-27T22-04-52-939Z-6ecaa3/` (confirmation).

## Field-directed import implementation

The public BTF and ISF `ImportProjection` methods now resolve arbitrary supported member/index/pointer paths,
combine their type-level closure, preserve promoted-member ambiguity and validate selected layouts through the
ordinary importer. Terminal composite/array values include their by-value dependencies; unrequested pointer
targets retain native identity as explicitly unselected address-only definitions. The metadata-owned scalar cache
is shared with full imports. No schema mutates when another import requests additional fields.

The initial restricted control measured only the known `value` field of a synthetic ring of four or 256 records.
It retained root size and pointer identity but omitted general closure, validation and availability diagnostics.
It also constructed a fresh standalone scalar schema rather than reusing the metadata cache. Its large-graph
potential and small-control regression motivated separate production measurements; neither predicts the complete
public importer. The executable's `projection`, `prepare-projection` and `prepare-small-projection` modes now use
the public API. `prototype` retains the restricted control for explicit comparisons.

Production measurements and their raw launch paths are recorded below.
No real capture, full kernel-analysis timing, cold-disk throughput or cross-platform result is claimed.

## Validated public importer measurements

Measured on 28 October 2026 with the same machine, SDK, runtimes and controls described above.
These results use the complete public ImportProjection implementation, including bounded text/path planning,
promotion ambiguity checks, native descriptor occurrence identities, transitive scalar dependencies and
metadata-owned successful compilation reuse. Both sides use the same final executable and compiler-buffer fix.

Five serial alternating process pairs per operation and runtime retain all launches. Fresh-document cases use
50 iterations, warm graph imports 100, large preparation 50 and small preparation 100. Each launch warms the
same number of operations before timing. Fresh-document operations construct and parse a new metadata object
on every call: their document/table/scalar caches are fresh, but the process and JIT/parser code are warm.
Overlapping cases reuse parsed metadata after an untimed import of r0. Preparation imports r1, binds its value
and consumes 1000 complete reads. Their before/after checksums match in every launch. Pure import cases return
the full versus selected schema, so their type counts deliberately differ; those are not equal whole-schema results.

The table gives mean and full range in microseconds per operation. Allocation is measured B/op. These final
launches have visible timing variation, especially JIT preparation. The JIT small-control ranges overlap, so its
elapsed-time difference is inconclusive. Allocation reductions are stable. The large-graph and fresh-document
comparisons show substantial local benefit despite that variation; they do not predict a complete kernel analysis.

| Runtime / operation | Full import mean (range), us/op | Projection mean (range), us/op | B/op full → projection |
| --- | ---: | ---: | ---: |
| JIT / Fresh metadata + first root import | 3,051.91 (2,354.30–3,421.32) | 1,348.63 (1,004.49–1,552.45) | 1016365–1016685 → 229946–230106 |
| JIT / Fresh metadata + import/bind/1000 reads | 3,441.79 (2,755.00–4,414.48) | 1,538.53 (1,322.56–2,026.42) | 1081941–1082261 → 295522–295682 |
| JIT / Overlapping 256-record graph import | 1,389.72 (1,347.96–1,429.40) | 11.94 (9.96–14.26) | 779685 → 7888 |
| JIT / Warm 256-record import/bind/1000 reads | 2,271.46 (1,682.92–2,970.36) | 357.64 (287.53–437.67) | 844223 → 72424 |
| JIT / Warm 4-record import/bind/1000 reads | 424.28 (379.94–511.06) | 371.73 (327.47–402.09) | 79410 → 72424 |
| AOT / Fresh metadata + first root import | 832.20 (742.27–1,062.20) | 252.93 (232.96–293.80) | 1029115–1029275 → 230162–230242 |
| AOT / Fresh metadata + import/bind/1000 reads | 961.91 (862.68–1,275.51) | 296.37 (255.14–320.99) | 1094851–1095011 → 295578–295738 |
| AOT / Overlapping 256-record graph import | 497.75 (449.40–620.15) | 4.27 (4.09–4.71) | 792216 → 7824 |
| AOT / Warm 256-record import/bind/1000 reads | 548.26 (476.37–761.39) | 51.77 (50.08–52.83) | 856806 → 72360 |
| AOT / Warm 4-record import/bind/1000 reads | 65.54 (61.76–72.04) | 54.27 (51.48–58.02) | 79638 → 72360 |

The final raw data and driver are `artifacts/improvement/projection-measurements-final/` and
`artifacts/improvement/measure-projection-final.py`. An earlier production batch with only overlapping workloads
remains separately in `artifacts/improvement/projection-measurements/`; it is not substituted for noisier final
observations. JIT/AOT build outputs are `artifacts/improvement/projection-probe-jit/` and `projection-probe-aot/`.

The original synthetic stream has one selected scalar per record; it is a preparation control rather than a
full application workload. New metadata costs include parsing/indexing; warm cases do not. Binding and first-use
controls from the earlier table remain separate. The restricted prototype is not used to establish these gains.

The compiler follow-up transfers exact-sized field buffers rather than copying them. Its original allocation
regression passes unchanged on both runtimes. A separate three-launch-per-side BDN confirmation of the tiny scalar
compiler remains timing-inconclusive, with allocation-diagnostic variation; no compiler elapsed-time claim is made.
Raw confirmation: `artifacts/perf/development/runs/2026-10-28T08-47-37-760Z-5f526c/`.
