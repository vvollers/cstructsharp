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
or a prelude compile independently. No cache survives outside the schema, and no source bytes or pointer targets
are cached. Bit-slice compilation is independent.

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
