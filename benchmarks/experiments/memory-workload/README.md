# Memory workload measurements

The memory API resolves explicit metadata offsets and decodes selected values. Wide metadata structs and many
distinct pointer definitions exercise work that ordinary Portable-layout benchmarks do not cover.
`MemoryWorkloadBenchmarks` supplies public synthetic inputs; no private capture is needed to run it.

Member selection constructs a region and selection only for a matching or promoted member. Schema validation
already proves that all field extents fit. Checked address addition remains in declaration order for every field:
a zero-length member at the end of a region ending at `ulong.MaxValue` still overflows. Promotion searches still
visit later branches after finding a match, preserving ambiguity and nesting failures.

Schema construction temporarily indexes built-in scalar layouts by their exact codec spelling and effective byte
order. The pointer width and options are fixed within one constructor. Each ID still validates its size and pointer
target independently. Definitions with declarations and configurations with custom codecs, defined-name collections
or a prelude compile independently. This constructor-local index is discarded after preparation. No source bytes
or pointer targets are cached. BTF's additional metadata-owned compilation cache is described below.

On Windows x64, .NET 10.0.12, SDK 10.0.401, logical CPU 16, three serial diagnostic confirmation pairs measured:

| Complete operation | Allocated bytes before | Allocated bytes after | Timing observation |
| --- | ---: | ---: | --- |
| Select one byte from 273 members | 26,400 | 288 | Paired launch changes -84.5% to -83.5%; spread still marked unstable |
| Prepare 1,000 pointer definitions | 15,207,098 | 517,212–517,292 | About 8.62 ms to 0.265 ms in every pair |
| Prepare one scalar definition | 16,137 | 16,377–16,457 | Within the 3% practical margin; extra local dictionary storage |
| Serialize one scalar | 120 | 120 | No timing claim |
| Plan a scalar replacement | 1,112 | 1,112 | No timing claim |

These are local observations, not portable guarantees or confidence intervals. All allocations include the harness's
amortized diagnostic overhead. The synthetic array controls were unchanged in this comparison.

A separate full-application comparison used identical application and probe sources, with complete output hashing
outside timing. Ten seconds per workflow, three alternating pairs, production tiering/PGO enabled: repeated process
listing allocated 74.4 MB before and 2.75 MB after; its seconds-five-through-ten launch medians were 14.90–15.03 ms
and 2.39–2.40 ms. Repeated network listing allocated 215 MB and 171 MB, with later medians 49.53–52.71 ms and
43.18–45.14 ms. Every symbol/workflow hash matched. Short initial JIT runs had slower second/third network calls
after the change; those contrary observations remain in the evidence. The sustained fixed sequence supports a
later-call improvement, but does not establish the cause of that early transition.

Actual Native AOT full-workflow executables also produced identical complete hashes in three fresh pairs. First
bootstrap medians were about 242 ms before and 184 ms after. OS caches were not evicted. This measures CPU work,
allocations and cached image access, not cold-disk throughput. Managed allocations are not retained heap or disk I/O.

Reference-compatible tests cover high-address failure order, wide/tiny records, promotion, stateful source/resolver
callbacks, budgets, cancellation, custom codec initialization, aliases, endian settings, concurrent reads/writes and
patch planning. They pass on both untouched reference and candidate. Existing tests and golden outcomes are retained.

## Arrays and BTF metadata

Plain numeric memory arrays write directly to their final owned storage. Each element still checks nesting,
charges a request, and completes the same source reads. Scratch bytes are cleared between elements because a
custom source can inspect its destination. Text, enums, wide-number fallbacks and caller-defined configurations
keep the general path. Three diagnostic confirmation pairs measured 16-byte arrays at 1,912 to 224 B/op
and 4 KiB arrays at about 430,320 to 4,309 B/op. Paired timing changes were -39.2% to -36.8% and -41.7% to -41.3%.
Actual Native AOT complete workflow hashes matched; later module listing allocated 767,800 to 159,608 bytes.

BTF name lookup retains at most 256 outcomes per metadata table, including missing and ambiguous names. Every
failure still creates a fresh exception. In three fresh application pairs, repeated cached-root imports fell from
about 0.7–0.9 ms to 0.002–0.005 ms after the first hit's JIT work. This removes name scanning; it does not make an
uncached graph import free. Parsing the table adds only the lock object before any name is queried.

BTF imports also share a bounded cache of successful core scalar and bit-slice layouts. Full declarations and
all compilation settings form the key. The cache belongs to one metadata table, has 256 entries and a 1,048,576
source-character budget, and keeps no failed compilation. Public schema construction and ISF imports keep their
existing independent behavior. Each BTF root still traverses, validates and owns its graph and ordered diagnostics.

After schema-local scalar reuse, this further reduced a fresh overlapping root from about 21.4 to 10.7 MB allocated.
The first such root took 63–66 ms before and about 24 ms after in three production JIT pairs. A diagnostic sequence
retaining 24 complete imports kept about 209–210 MB before and 102 MB after; these heap observations include the
probe's retained objects and runtime effects. Complete ordered descriptor and diagnostic hashes matched for every
root. A disjoint scalar control remained valid. Actual Native AOT first module calls fell from 18.5–19.1 to
10.1–10.3 ms; first network calls from 167–169 to 130–132 ms. Bootstrap timing overlapped: no additional startup
CPU gain is claimed for this cache. Allocation and repeated-root preparation improvements justify retaining it.

The array/name-cache tests add 45 reference-compatible cases per runtime; seven further cases cover overlapping
and disjoint imports, exact root-dependent diagnostic order, fresh failures after warmed success, pointer widths,
endianness, split-table independence, ownership and concurrency. All 296 memory cases pass on both runtimes.

## Combined application validation

The final comparison combines library selection, scalar preparation, numeric arrays and BTF caches with the
application's symbol decoder and private mapped source. The reference is library `8bcac5d3` and application
`ed3e7f00`; the candidate product sources are `7dd7fc26` and `dc1f1bba`. These are separate from the already
adopted package change from 0.11.1 to 0.12.0. The reference and candidate use the same complete workflow probe.

Three alternating process pairs per runtime ran each analysis for ten seconds on logical CPU 16. The table
shows the range of each launch's median during the predeclared seconds-five-through-ten interval. Bootstrap
shows all three first-process measurements. Every call and outlier is retained; allocation is per complete call.

| Operation | Production JIT before / after, ms | Native AOT before / after, ms | JIT allocated MB before / after |
| --- | ---: | ---: | ---: |
| Bootstrap | 380.0–398.3 / 259.8–267.7 | 234.1–245.1 / 149.6–158.2 | 525.3–525.6 / 265.9–266.0 |
| Processes | 15.01–15.07 / 2.25–2.29 | 29.38–29.45 / 5.27–5.33 | 74.394 / 2.340 |
| Open files | 10.57–10.60 / 2.67–2.69 | 20.60–20.68 / 5.52–5.54 | 54.885 / 10.507 |
| Mounts | 0.259–0.264 / 0.093–0.094 | 0.526–0.527 / 0.181–0.182 | 1.445 / 0.441 |
| Modules | 0.830–0.831 / 0.222–0.226 | 1.621–1.626 / 0.398–0.400 | 2.236 / 0.160 |
| Network | 50.62–52.35 / 36.92–38.40 | 93.99–95.17 / 72.43–73.10 | 215.042 / 170.374 |

MB means 1,000,000 bytes. Candidate p95 times were lower for every workflow in every pair. For example, later
network p95 ranges were 51.31–53.80 to 37.24–39.25 ms in JIT and 95.42–96.37 to 73.43–76.31 ms in AOT.
First calls remain separate in the raw summaries: first network calls were 468–476 to 120–122 ms in JIT and
347–359 to 121–131 ms in AOT. These observations support the combined workload improvement, not a universal
guarantee or a suite-adjusted statistical claim.

Complete symbol and workflow hashes matched across both runtimes and all twelve processes. Separate diagnostic
runs also matched the complete source interaction sequence, including source-property accesses, read order,
requested bytes, returned bytes and accounting. All 37 CLI regression commands matched; six commands from
actual Native AOT CLI executables were byte-identical. Baseline and candidate Native AOT Studio smoke tests
finished without failures. Both Studio builds had the same 45 existing dependency/XAML trimming and AOT warnings.

A separate serial CLI check ran 72 fresh command processes, including runtime startup, bootstrap, analysis and
rendering. Complete stdout/stderr matched across both runtimes and all pairs. End-to-end network commands took
1,335–1,352 to 723–738 ms in JIT and 786–805 to 340–342 ms in AOT. JIT bootstrap-command ranges overlapped
(469–700 versus 366–576 ms); AOT bootstrap commands took 258–288 versus 175–183 ms. These process wall times
include output to private files and OS scheduling and stay separate from in-process workflow timings.

Final managed validation passed 8,966 tests across both supported runtimes and the generator/compiler projects.
The application passed all 716 tests with its released dependency and again with the private package, without
dump-test skips. API, canonical reference, feature matrix, changed-documentation and isolated package-consumer
checks passed. Existing tests and assertions remain intact.

The full 100-case Impact screen was followed by 43 targeted confirmation cases. No screen slowdown repeated
as a stable slowdown. The generated enumerator remained unstable, including an identical-binary control with
a paired change as large as +26.4%. An unrelated packet serialization speedup was not credited to this work;
its identical-binary control varied from -5.4% to +1.6%. Compiler allocation estimates also varied between
processes. Raw screen and confirmation results remain available, including inconclusive observations.

The CLI executable grew by 54,784 bytes (8,228,864 to 8,283,648); Studio grew by 92,160 bytes (69,821,952 to
69,914,112). End-of-probe managed heaps were 262–263 to 94–127 MB in JIT and 249–252 to 116 MB in AOT.
Peak working sets were 727–731 to 440 MB and 698–707 to 419–420 MB respectively. These include the probe,
retained roots, runtime lifetime decisions and mapped pages; they are not isolated cache sizes or physical I/O.

Whole import-result sharing is rejected because discovery and diagnostic order depend on the root. Global or
caller-code codec sharing is rejected because callback and lifetime contracts differ. A member index is deferred:
the simple selection change already removes most allocation without retaining more per-schema state. Eager BTF
name indexing is deferred in favor of bounded lazy lookup. Application byte-copy removal, read coalescing, read
caching and shared mutable task-walk contexts remain outside the retained changes; their costs and constraints
are recorded in the application's performance note and the local final report.

The earlier short JIT network transition remains unresolved. An identical-binary 30-call control also showed
third-call variation from 92 to 215 ms, but that does not establish the cause of the earlier between-version
slowdown. Native AOT 4 KiB sequential transport timing remains inconclusive. Only Windows x64 and one private
image were measured; no cold-disk, other-platform or universal application claim follows.

## Reproduction

Use the repository's pinned Node and SDK. Capture before changing production code, preserving both checkouts and
using identical benchmark source on each side:

```powershell
node tools/quality/perf-check.mjs --capture memory-reference
node tools/quality/perf-check.mjs --baseline memory-reference --confirm --filter '*MemoryWorkloadBenchmarks*'
```

The original bundle remains `memory-workload-baseline`; identical supplementary cases are in
`memory-workload-cases-baseline`. Local raw samples, source ZIP hashes, package identities, all failed attempts and
complete private command outputs are under `artifacts/perf/memory-workload-20261004`. Diagnostic confirmation is
under `artifacts/perf/development/runs/memory-workload-bc-confirm`. These ignored artifacts must stay local.
The application remains on its released dependency unless explicitly built against a private local package.
The local `final-report.md` contains every candidate's disposition, exact package/build/probe commands, source
and binary identities, first/cached-session measurements and complete validation paths. Final raw campaigns are
`final-sustained-jit`, `final-sustained-aot`, `final-source-traces` and `final-triage-clean`; diagnostic benchmark
reports are `memory-workload-final-screen`, `memory-workload-final-confirm` and `memory-workload-identical-control`
under `artifacts/perf/development/runs`. Neither the image nor recovered data is included in Git.
