# 0.11.1 to 0.12.1 release measurements

These measurements compare the latest 0.11 patch with the library changes included in 0.12.1. They include changes
shipped in 0.12.0; they are not a comparison against 0.12.0. The breaking changes in 0.12.0 still apply. The selected
benchmark operations are supported by both versions and use identical inputs and the same result and ownership contracts.

## Sources and controls

- Baseline library and generator: `v0.11.1`, commit `640a7e9bd7bbd45c2534dba32ed2a54412bd5028`.
- Candidate library and generator: commit `96458af1991143054d9f95e7ba4c938ea26cb2cb`.
- Both use the candidate's complete benchmark, fixture-tool, comparison and fixture source trees. Product sources
  remain those of their respective revisions. The fixture verification and benchmark setup checks run on both.
- The memory-image consumer uses identical application sources from local commit
  `9055ede3`, compiled against the released 0.11.1 package or a freshly packed candidate. Its complete result hashes
  are checked outside timing; recovered image contents remain private.
- Windows 11, AMD Ryzen 9 9950X, .NET SDK 10.0.401, .NET 10.0.12, BenchmarkDotNet 0.15.8 and Node 26.5.0.
  Before/after processes run serially on logical CPU 16. Production measurements enable tiered compilation and PGO.
  Native AOT measurements execute separately published Windows x64 binaries.

## Measurement protocol

The 100-case Impact screen is diagnostic: it disables tiering and uses short in-process iterations. Its results select
follow-up work, not release speed claims. The production comparison uses 30 cases spanning generated and runtime
reads and writes, schema preparation, memory selection and arrays, plus unchanged controls and screen-detected
slowdowns. Three fresh process pairs alternate order: baseline/candidate, candidate/baseline, baseline/candidate.

The production runner records all 15 actual workload iterations from each launch, targeting 250 ms per iteration,
including observations that BenchmarkDotNet excludes from its default summary. Tables summarize launch medians and paired ratios. Observed
ranges are not confidence intervals. A small change, inconsistent direction, unstable timing or drifting control
does not establish a speedup. Allocated bytes are managed allocations per operation, not retained memory or disk I/O.

The initial six-warmup campaign exposed tier transitions during measurement and is retained as inconclusive evidence.
The complete 30-case selection is repeated with 24 warmup iterations. An iteration count does not guarantee an elapsed
warmup duration: code can become faster after BenchmarkDotNet calibrates its invocation count. Remaining instability
still disqualifies a precise timing claim; increasing warmups is not itself evidence of success.

The separate README serializer comparison uses BenchmarkDotNet's default job and verifies every field and output
before timing. It compares a 79-byte fixed record, serializers with their own formats, and a 62-byte data-dependent
record. Those tables use the existing renderer's overhead-adjusted BenchmarkDotNet medians; their absolute values
are not interchangeable with the unadjusted workload samples used for paired release comparisons.
All 37 serializer cases passed verification and completed the default job on 2026-10-04. The
[README comparison provenance](readme-comparison.json) records the source, runtime controls, sample counts and raw
report checksums. No affinity override was passed for that separate suite. Its current snapshot is not used to claim
a release speedup by comparing it with an older README run.

## Production JIT results

The [24-warmup report](production-warm24.md) has eight repeatable reductions, five cases within the 3% timing margin
and 17 unstable cases. Its unchanged hand-written control stayed within the screening margin. The classification
is descriptive and does not establish statistical confidence across the suite. The
[initial six-warmup report](production.md) is entirely inconclusive because its control drifted and tier transitions
continued during measurement. Both reports retain every launch, including the unfavorable ones.

The clearest reductions are generated 256 × 256 `uint16` matrix parsing (15.93–16.23× across pairs), runtime 4 KiB
text parsing (6.38–6.43×), generated bitfield parsing (3.45–3.55×), generated matrix serialization (2.48–2.51×),
runtime fixed-record serialization (1.29–1.32×), runtime matrix serialization (1.18–1.29×), and complete owned byte
arrays read by memory sessions (1.21–1.27× for 16 bytes; 1.23–1.28× for 4 KiB). The changelog presents their timings
and allocations. Complete actual samples and launch statistics are in [production-warm24.json](production-warm24.json)
and [production.json](production.json).

Every other selected case keeps its disposition in the full report. In particular:

- The fixed generated read, hand-written control and three natural scalar selections remain within the margin.
  This does not prove equivalence.
- Small generated serialization has paired time changes of +2.3% to +6.2%; its median delta is +4.1%, but one pair
  fails the 3% repeatability rule. It is inconclusive, not a claimed gain.
- Mapped packet reads range from −33.1% to +25.7%, and 256 generated text records from −30.0% to +29.9%.
  Neither supports a timing claim; the latter allocates about 106,553 → 63,544 bytes per operation.
- Wide selection allocates 26,400 → 288 bytes, and 1,000-pointer preparation about 18.7–19.0 MB → 0.515 MB.
  Their large timing spreads remain inconclusive after the longer warmup.
- Runtime fixed-root reads, numeric-matrix reads, text-output spans, short text records, tiny-schema preparation,
  scalar writes, tiny selections, update planning and generated nested/text reads have unstable timing. Their
  observed medians are not promoted to release speed claims. Allocation changes are reported independently.

There was no product-code adjustment during this comparison. Unstable cases were retained and documented, not
removed from the suite or rerun until a favorable launch appeared. No inference is made about .NET 8 timings,
cold-disk throughput, or a general ranking across input shapes.

## Complete memory-image workflows

The six complete result kinds (symbols, processes, open files, mounts, modules and network connections) have identical
hashes in every process and across JIT and Native AOT. No recovered contents are included here. Application sources
are identical, so application-level optimizations are present on both sides and are not attributed to the library.

Each time range below is the range of three process medians, in milliseconds. First calls each have one sample per
process; repeated calls include every sample starting after five seconds. Ranges are observations, not confidence
intervals. The JSON summaries retain sample counts, minimum/maximum individual call times, allocations and whole-process
retained-heap/working-set observations. Raw per-call measurements and complete-result hashes remain in the local evidence.

| Runtime | Operation | Phase | 0.11.1 median range (ms) | 0.12.1 median range (ms) | Paired speedup range |
| --- | --- | --- | ---: | ---: | ---: |
| JIT | Bootstrap | first | 1,042.9–1,062.0 | 257.9–262.1 | 4.04–4.09× |
| JIT | Process listing | first | 92.4–93.7 | 34.5–35.4 | 2.65–2.67× |
| JIT | Process listing | repeated | 18.3–18.7 | 2.280–2.298 | 7.96–8.14× |
| JIT | Open-file listing | first | 16.2–16.4 | 5.958–6.360 | 2.58–2.73× |
| JIT | Open-file listing | repeated | 12.5–12.7 | 2.671–2.698 | 4.67–4.74× |
| JIT | Mount listing | first | 1.757–1.833 | 1.539–1.590 | 1.13–1.15× |
| JIT | Mount listing | repeated | 0.256–0.258 | 0.093–0.095 | 2.71–2.77× |
| JIT | Module listing | first | 870.5–932.1 | 26.2–26.6 | 32.73–35.43× |
| JIT | Module listing | repeated | 0.808–0.846 | 0.224–0.229 | 3.57–3.69× |
| JIT | Network listing | first | 2,061.6–2,127.5 | 121.5–123.3 | 16.72–17.29× |
| JIT | Network listing | repeated | 84.0–86.1 | 36.8–38.7 | 2.22–2.32× |
| Native AOT | Bootstrap | first | 686.1–695.7 | 147.4–154.0 | 4.49–4.72× |
| Native AOT | Process listing | first | 85.5–94.9 | 6.178–6.302 | 13.61–15.37× |
| Native AOT | Process listing | repeated | 31.7–32.2 | 5.273–5.370 | 5.93–6.07× |
| Native AOT | Open-file listing | first | 22.7–24.0 | 6.021–6.386 | 3.68–3.78× |
| Native AOT | Open-file listing | repeated | 22.4–22.5 | 5.427–5.436 | 4.12–4.15× |
| Native AOT | Mount listing | first | 0.679–0.701 | 0.316–0.323 | 2.11–2.19× |
| Native AOT | Mount listing | repeated | 0.499–0.502 | 0.177–0.177 | 2.81–2.83× |
| Native AOT | Module listing | first | 498.8–511.5 | 8.557–11.8 | 42.58–58.29× |
| Native AOT | Module listing | repeated | 1.546–1.565 | 0.395–0.400 | 3.89–3.96× |
| Native AOT | Network listing | first | 1,941.8–1,979.4 | 117.2–136.9 | 14.46–16.76× |
| Native AOT | Network listing | repeated | 128.3–129.3 | 70.8–70.9 | 1.81–1.82× |

See [JIT measurements](memory-jit.json) and [Native AOT measurements](memory-aot.json). The reference image
and the application snapshot are private local inputs; reproducing those rows requires them. The synthetic cases
above can be reproduced from this public repository.

At the final full collection, observed managed retention was 505.0–506.4 → 89.3–121.0 MiB for JIT and
461.7–464.0 → 110.4 MiB for Native AOT. These whole-process observations include verification and runtime state;
the differing call counts and JIT retention range prevent treating them as a precise library-cache size estimate.
The Windows x64 native probe grew from 6,327,296 to 6,365,184 bytes (+37 KiB, about 0.6%). Those executable sizes
include the application, verification code and runtime, rather than measuring the library in isolation.

## Reproduction

Prepare two isolated checkouts at the revisions above. Copy `benchmarks/CStructSharp.Benchmarks`,
`benchmarks/CStructSharp.FixtureTool`, `benchmarks/CStructSharp.Comparison` and `benchmarks/fixtures` from the candidate
to the baseline. Keep each checkout's `src` tree unchanged. Build and verify each before timing:

```powershell
dotnet build BEFORE/benchmarks/CStructSharp.Benchmarks/CStructSharp.Benchmarks.csproj -c Release -f net10.0
dotnet build AFTER/benchmarks/CStructSharp.Benchmarks/CStructSharp.Benchmarks.csproj -c Release -f net10.0
dotnet BEFORE/benchmarks/CStructSharp.Benchmarks/bin/Release/net10.0/CStructSharp.FixtureTool.dll verify BEFORE/benchmarks/fixtures
dotnet AFTER/benchmarks/CStructSharp.Benchmarks/bin/Release/net10.0/CStructSharp.FixtureTool.dll verify AFTER/benchmarks/fixtures
$filters = Get-Content benchmarks/experiments/release-0.12.1/production-filters.json | ConvertFrom-Json
node benchmarks/experiments/runtime-generated/production.mjs BEFORE AFTER NEW_OUTPUT @filters
$env:PERF_WARMUPS = '24'
node benchmarks/experiments/runtime-generated/production.mjs BEFORE AFTER NEW_WARM24_OUTPUT @filters
Remove-Item Env:PERF_WARMUPS
```

Use Node 26.5.0 and the repository SDK. Each output directory must be new. Run nothing else that builds, tests or
measures on the machine during timing. The runner retains source inventories, exact commands, process order, host
logs and full BenchmarkDotNet JSON. The production runner pins logical CPU 16; choose an allowed CPU and record its
mask when adapting it to another host. The initial diagnostic command is:

```powershell
node tools/quality/perf-check.mjs --capture release-baseline --checkout BEFORE
node tools/quality/perf-check.mjs --baseline release-baseline --checkout AFTER --cpu 16 --label release-screen
```

Run the README comparison in a fresh candidate checkout, so its `artifacts/comparison/results` cleanup cannot erase
earlier evidence:

```powershell
node tools/quality/comparison-benchmarks.mjs --job default
node tools/quality/comparison-benchmarks.mjs --check
node tools/documentation/sync-documentation-facts.mjs --write
```

The private application probe runs bootstrap once, then complete process, open-file, mount, module and network
workflows for ten seconds each. First calls are reported separately; repeated-call summaries use every call starting
after five seconds. Every property of the five workflow results, and the complete symbol dictionary, is serialized
and hashed outside timing. The same probe
source is compiled for JIT and published with `-r win-x64 -p:PublishAot=true`; the probe records whether dynamic
code is compiled, and the runner fingerprints each executable. Both application source trees are identical.
Reproduction requires the local application snapshot and reference image, which are not part of the public suite:

```powershell
$evidence = 'artifacts/perf/release-0.12.1-20261004'
python "$evidence/run-sustained.py" recheck-jit jit-before jit-after jit 10
python "$evidence/run-sustained.py" recheck-aot aot-before aot-after aot 10
```

The campaign names must be new. Each command runs three alternating pairs; run them one after the other. These
measurements use a warm operating-system file cache and do not measure cold-disk throughput. Managed allocation
counts cover the timed synchronous operation; retained heap and peak working set cover the whole process, including
verification, and are recorded separately.

Local evidence is preserved in `artifacts/perf/release-0.12.1-20261004`, including source archives, build logs,
production launches and the application probes. Immutable diagnostic bundles and the 100-case screen are under
`artifacts/perf/development` with names beginning `release-0121-`. Earlier benchmark artifacts were preserved.
