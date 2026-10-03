# Investigating a 5–10 second Impact comparison

This is an isolated measurement experiment, not a replacement benchmark workflow. The library and the existing
BenchmarkDotNet host are unchanged. Run the normal workflow described in [the benchmark guide](../../README.md)
when collecting release evidence. The experiment calls the original benchmark methods and their setup/cleanup
methods. Neither workflow's threshold alone establishes a performance change.

## Recommendation and measured limits

**A full Impact report can finish in 5–8 seconds; a trustworthy full-suite 3–10% decision was not demonstrated.**
Keep BenchmarkDotNet and use affected-case measurements during editing, followed by independent, longer
confirmation. A possible opt-in full-suite screen is twelve 1 ms BDN measurements, three warmups, `GcForce=false`,
and the existing power plan. It took **7.39 seconds from command invocation through the report**, but flagged
identical code in all eight A/A trials. Label such output provisional and never turn a missing flag into a pass. Do not adopt
the custom worker as the default: it saves another two seconds but adds an engine to maintain and failed its
false-alarm audit even after a nominal multiple-comparison correction.

There is **no established minimum detectable CPU change** at an acceptable suite-wide false-alarm rate within
ten seconds. Near-3%, near-5%, and near-10% controls were often missed or measured in the wrong direction.
A 56-byte allocation increase/decrease was observed in all 30 trials in each direction for the tested packet
controls. That is evidence for those controls, not a universal one-byte sensitivity claim. The one measured
source-edit/build/compare loop took **12.98 seconds**, even using the custom runner.

These are measurements from 2026-10-03 on one machine and one base revision:
`f6cab27c5ab370da75f9ad3d8e338d0d8c263c9d`. Hardware was a Ryzen 9 9950X (16 cores/32 threads), about 64 GiB RAM,
Windows 11 build 26200.9550. Tools were SDK 10.0.204, runtime 10.0.12 x64, BDN 0.15.8, and Node 26.5.0, matching
the repository's version policies. GC was concurrent workstation; ReadyToRun stayed at its default. All timing
ran serially, without competing builds or tests. The machine was already using High Performance. No CPU affinity
was set. BDN raises process/thread priority; the custom workers use normal priority. Background OS activity,
temperature, clock speed, and core migration were not traced, so their individual contributions are unknown.

### Approach comparison

“Slow” below is the observed maximum, not a predicted tail bound. A/A means comparing identical code; any timing
change label in A/A is a false alarm. All full-suite rows measure 55 cases on each side, including allocations
unless stated otherwise. Asterisks mark timings that are **not** complete standalone command measurements.

| Approach | Coverage | Median / slow wall time | Reliability observed | Allocation support | Added maintenance |
| --- | --- | --- | --- | --- | --- |
| Existing Quick, two rounds/side | 55 | **116.58 / 116.99 s**, 3 commands | 8, 43, 14 false timing flags; 3/3 suites | BDN | None |
| BDN 25 ms × 5, `GcForce=false` | 55 | **49.05 s combined hosts***, one pair | No confidence policy established | BDN | Small config |
| BDN 5 ms × 5, `GcForce=false`, default power handling | 55 | **22.36 / 22.38 s combined hosts***, 3 pairs | Still exceeds budget | BDN | Small config |
| BDN 5 ms × 5, `GcForce=false`, current power plan | 55 | **12.08 / 12.14 s pipeline***, 5 comparisons | Still exceeds budget | BDN | Small config |
| BDN 1 ms × 12, `GcForce=false`, current power plan | 55 | **7.39 / 7.42 s**, 3 standalone commands | 11–26 false flags per trial; 8/8 suites across both campaigns | BDN | Small config; provisional reporting |
| Custom, 2 ms × 12 pairs, fresh workers | 55 | **5.15 / 5.18 s**, 3 standalone commands | Separate 30-trial audit: 18/30 suites false after correction | Process counters | High: lifecycle, async, inference, IPC |
| Same workers retained | 55 | **2.88 / 3.09 s warm***; first 4.99 s | 4/30 suites false; shared-process trials are correlated | Process counters | High, plus invalidation/service |
| Custom 5 ms × 12 pairs | 55 | **9.73 / 9.94 s***, 10 trials | 9/10 suites false after correction | Process counters | High |
| Custom 8 ms × 24 pairs, selected cases | 12 | **7.85 / 8.11 s***, 10 trials | 10/10 suites false after correction | Process counters | High, plus coverage selection |
| Existing Quick filtered to `PacketBenchmarks.ParseSpan` | 1 | **8.99 / 9.02 s**, 3 commands | 1/3 false flags; too few trials to estimate reliability | BDN | None |
| Longer BDN, 100 ms × 30, three warmups | 55 | **478.64 s combined hosts***, one pair | 33/55 A/A medians differ >3%; one long pair is not ground truth | BDN | Small experimental config |

Combined-host values omit conversion/controller/report costs and cannot establish an end-to-end budget.
Custom multi-trial values include both worker startups, fixture setup, JIT, calibration, measurement and cleanup
for each trial, but exclude the controller's initial startup and writing the final report. The three standalone
5.15-second measurements include those costs. Persistent warm statistics exclude the first trial; there is no
implemented edit-aware service. The warm maximum is 3.09 seconds; the first trial is the maximum of all 30.
The 12.08-second pipeline includes two hosts, conversion and reporting, but excludes driver startup. Five
similarly timed 1 ms pipelines took 7.35/7.39 seconds and flagged 22, 24, 18, 19, 26 cases. The three subsequent
standalone commands include driver startup and flagged 22, 11, 21 cases. Existing Quick includes four hosts.
The custom rule uses a suite correction; existing/raw BDN flags use only 3%, so the
false-alarm counts are not comparisons of identical decision rules.

Removing memory diagnosis from the default-power 5 ms job took 10.65–10.81 seconds **per host**, versus
11.06–11.25 with it. Losing allocation coverage did not make the two-host comparison fit. Custom forced-GC
batches took 11.92 seconds median and 12.68 slow, with 9/10 false-alarm suites. Custom 8 ms full-suite batches
took 14.50/14.90 seconds, with 8/10 false-alarm suites. Simply spending more time in the same correlated sampling
design did not repair its inference.

### Where the current two minutes go

The actual command was `node tools/quality/quick-perf-check.mjs --baseline . --job Quick --categories Impact`.
For the 115.18-second run, child-process tracing attributed **114.67 s to four benchmark hosts**, 0.418 s to four
converters, 0.037 s to comparison/reporting, and 0.046 s to remaining controller work. The 116.99-second run
attributed 116.46 s to hosts. Orchestration and Node startup are not the dominant cost.

An instrumented host with the same Quick job shape took 29.47 seconds. `TimedFactory` wraps BDN's original
factory and delegates; it does not implement a replacement engine. Logger timestamps, full iteration records,
and runtime counters separate the following costs. Some counters overlap; **do not add every row**.

| Component | Seconds in one 55-case host | Attribution |
| --- | ---: | --- |
| Process startup/finalization outside the BDN stopwatch | 0.129 | External wall minus internal wall; includes the diagnostic wrapper |
| Discovery and initial validation | 0.268 | Start through first discovery marker; includes initial managed work |
| In-process emit/build phase | 0.071 | BDN phase markers; no external compiler |
| Original fixture setup | 0.371 | Wrapped global setup calls |
| Engine creation, setup and initial JIT | 1.337 | Includes setup; not an additional 1.337 after setup |
| Runtime JIT compilation | 1.275 | Cumulative `JitInfo`; overlaps discovery, setup and workload |
| Logged overhead/workload Jitting iterations | 0.043 | Does not represent all JIT compilation |
| Pilot/calibration workload | **8.354** | Sum of pilot iteration times |
| Warmup workload | 1.406 | Sum of warmup times |
| Five measured iterations per case | **7.004** | Sum of actual times; includes natural GC during operations |
| Extra allocation-diagnostic workload | 1.672 | Wrapped workload total minus recorded workload stages |
| All GC pauses | 3.716 | Includes 0.486 inside workload delegates |
| GC pauses outside workload | **3.230** | Largely forced GC; also includes other collections, not a precise forced-GC wall timer |
| Export/summary windows | 0.933 | Logger marker windows; other per-case console work is outside them |

BDN's allocation stage executes another workload batch and its engine forces collections around iterations
when `GcForce` is enabled. These are separate costs from the five reported measurements.
`GcForce=false` disables per-iteration collections; the memory-diagnostic stage still forces a collection.
See the [version-pinned engine](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/Engine.cs)
and [measurement stages](https://benchmarkdotnet.org/articles/guides/how-it-works.html).
`Workload/Result` records are derived from actual samples; adding them would double-count measurement time.

Another substantial cost was Windows power-plan handling. BDN applied High Performance before each of 55 cases
and restored the original plan for each of 11 class summaries, even though High Performance was already active.
Holding the current plan with `PowerPlan.UserPowerPlan` reduced an otherwise matching profiled host from
**29.47 to 23.33 seconds**. Workload time itself changed by 0.63 seconds, so the whole 6.14-second wall difference
cannot be attributed exactly to native power calls. The controlled configuration change supports approximately
5–6 seconds of avoidable per-host overhead on this machine. This is Windows/machine-specific, not a portable
speed promise. See BDN's [power-plan option](https://benchmarkdotnet.org/articles/configs/powerplans.html) and
[runner call sites](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Running/BenchmarkRunnerClean.cs).

The dominant measured costs are calibration, actual work, power handling and collection pauses. Fixture setup
and initial discovery are small by comparison. The ledger leaves overlapping JIT/GC and uninstrumented per-case
framework work; it is not an invented perfectly additive profiler trace.

### Reliability and known changes

Thirty fresh-worker A/A trials took 5.03 seconds median per trial (p95 5.24, maximum 5.32). There were **636 raw
3% flags among 1,650 case comparisons**. Even the experimental corrected rule produced 47 flags and flagged
**18/30 suites: 60%, Wilson 95% interval 42–75%**. The 95th percentile of absolute A/A case deltas was 12.0%.
This empirically rejects its advertised 5% familywise hypothesis on this machine. The 4/30 persistent result
conditions on one pair of processes and is not 30 independent launch trials; a binomial confidence interval for
general future edits would be misleading. Adjacent fresh-launch trials can also share thermal/OS drift.

A nominal per-case 5% false-alarm policy has about a 94% chance of at least one false alarm across 55 independent
cases (`1 - 0.95^55`). Bonferroni can address multiplicity only with valid underlying probabilities. Twelve
adjacent batch pairs are not twelve independent process launches. More observations from a biased process can
make an invalid test more certain. Even zero failures in 30 independent trials would leave a one-sided exact
95% upper false-alarm bound of about 9.5%; this study cannot certify a 5% gate.

The controls parse the original packet once and add/remove redundant validation calls or a scratch allocation.
They model removable repeated checks, not arbitrary percentage delays. The longer BDN references use thirty
100 ms iterations and three warmups. The following rates count **correct-direction** custom flags out of 30
fresh-process trials; a reversed regression of +5% is an improvement of about −4.8%, not exactly −5%.

| Control | Longer BDN increase | Fast detection: add / remove | Observed limitation |
| --- | ---: | ---: | --- |
| Four cheap count checks | 3.36% median; 2.71–3.94% across 3 launches | **4/30 / 0/30** | Two additional forward flags pointed the wrong way; effect straddles the practical margin |
| Sixteen cheap count checks | 5.58%; 5.36–7.27% across 3 launches | **2/30 / 4/30** | One additional reverse flag pointed the wrong way |
| One dictionary membership check | 8.40%, one reference launch | **10/30 / 12/30** | Pure timing control near 10%; reference effect has launch uncertainty |
| Sixteen count checks plus scratch array | 9.16%; 7.62–9.67% across 3 launches | **6/30 / 8/30** | Includes +56 bytes/op; allocation detected 30/30 each way |
| Two dictionary checks | 12.92%, one reference launch | **11/30 / 13/30** | A larger effect still often misses this rule |
| Sixteen dictionary checks | 64.27%, one reference launch | **30/30 / 30/30** | Positive control only; not a suite-wide minimum detectable effect |

For scale, Wilson intervals for 4/30 and 10/30 correct detections are approximately 5–30% and 19–51%; 30/30
has a lower 95% bound of about 89%. These control trials time 12 parameter cases, while keeping the 55-case
correction. They do not validate every Impact workload or every optimization. No tested policy simultaneously
achieved high power near 3–10%, low suite false alarms, and the requested wall budget.

Two longer **full-suite** A/A runs took 241.59 and 237.05 seconds. Despite a median within-run relative standard
deviation of only 1.41%, 33/55 medians differed by more than 3%; the 95th percentile absolute between-run delta
was 16.57%, and the two 1 MiB array cases differed by about 61–62%. Their precise cause was not isolated.
This demonstrates why a narrow within-run interval and a long sequential A/B pair are insufficient. These runs
are references for comparison, not infallible ground truth. BDN's default upper-outlier filtering retained
23–30 samples in the longer controls; all original measurements remain in the full JSON.

An additional source-edit test used separate immutable before/after bundles. Only the isolated copy of
`PacketBenchmarks.ParseSpan` gained a redundant tag check; the core DLL hashes remained equal. Across ten full
suite trials each way, the fast target-case median was +7.45% forward and −8.29% reversed, with only 2/10
corrected detections each way. The forward empirical 5th–95th range was −29.1% to +13.9%. Longer targeted BDN
on those exact bundles measured 363.37 versus 370.54 ns (+1.97%, allocations 384 on both sides). One longer
pair does not settle the disagreement. **This change is inconclusive**, not a validated 6–9% result.

### Smallest-median audit

The current script alternates baseline/candidate twice and chooses each side's smaller median independently.
Compared with averaging those two medians, the selected value was lower by a median 0.87%, 2.57%, and 1.15%
in the three A/A comparisons. The 95th-percentile within-side spread was 17.0%, 22.5%, and 22.4%.
Single-round / mean-of-two / minimum-of-two flag counts were **12/10/8**, **47/32/43**, and **24/17/14**.
Minimum selection neither reliably removes false alarms nor displays instability. It rewards the side with a
more favorable extreme and can favor a noisier candidate even when central performance is unchanged.

Source inspection also found that allocation minima are updated only when a smaller timing median is selected;
they are not an independent aggregation over all allocation observations. The converter maps missing memory
diagnostics to zero, and the comparator treats a 3% timing **or** allocation delta as a flag without uncertainty.
This investigation does not modify those tools. Future inference must retain all rounds and distinguish missing
allocation data from zero allocation.

### Runtime, GC, caches, and correctness

Tiering was disabled for the principal comparisons, matching the current script. With tiering enabled, ten
custom A/A trials produced 193 corrected flags, all ten suites flagged, and a 152% 95th-percentile absolute
case delta. Brief startup does not establish stable tiered/PGO code. A longer out-of-process tiering/PGO control
run measured its zero-check baseline at 221.08 ns versus about 350–354 ns in the tier-off in-process references.
Its smaller controls measured +2.43%, +6.90%, and +9.63%. Toolchain and runtime mode both changed, so the absolute
difference cannot be attributed solely to PGO. Confirm in the intended deployment mode; see the official
[tiering and PGO configuration](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation).

The custom loop/consumer probe cost 1.08 ns median (1.08–1.27 ns observed). The clock has 100 ns ticks; whole
millisecond batches amortize timer resolution, but a fixed per-operation consumer cost remains. That matters
for the approximately 2 ns generated-view/handwritten cases and can attenuate percentages. Neither Quick nor
this prototype subtracts overhead. Different loop unrolling, generated method shape, priority and code placement
also prevent treating raw nanoseconds from the two engines as interchangeable.

Balanced AB/BA batches reduce simple time trends; they do not reset heaps, CPU caches or OS file caches. Each
case's original setup remains outside timing; workers hold more fixtures at once than BDN. Calibration warms
code and data, all allocations contribute to later heap state, and idle workers may still have GC activity.
File-stream measurements are warm cached reads. Reversing bundles exposed order/process effects but did not
isolate their cause. Cache flushing or forcing a collection changes the measured workload and must not be
assumed to produce a more representative answer.

On .NET 10, the pinned BDN [allocation implementation](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/GcStats.cs)
uses total-process allocation counters. The worker uses `GC.GetTotalAllocatedBytes(true)` outside its timer and
also records the current-thread counter. An audit allocating 100 arrays on real task-pool continuations saw
**422,680 process bytes versus 7,592 caller-thread bytes**. A thread-only replacement would miss the payload.
Total-process counting can include unrelated process activity, and native allocations are outside its scope.
See [.NET's counter contract](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes?view=net-10.0).
The current Impact async case completes synchronously over memory; genuine asynchronous completion remains
outside this experiment's coverage.

All scratch controls changed 384 to 440 bytes/op in BDN and ±56 bytes/op in all fast trials. In contrast,
identical-code full-suite allocation medians sometimes differed in nine compilation cases and the two large
array cases. Their cause is unresolved; precise counters do not make allocation behavior deterministic across
processes. A one-byte discrepancy in these cases is not automatically a library regression.

Correctness was checked outside timing through the original setup logic, packet serializer byte equality and
packet value fingerprints for injected controls, 63 canonical fixtures, and 4,245 managed tests on each target
framework. Each timed custom result is consumed through a typed non-inlined sink. Both sides use the same
invocation count. This does not constitute a new universal equivalence checker for all benchmark return types;
the generic worker must not be trusted with arbitrary new lifecycle or async shapes without further validation.

### Subset coverage and build costs

The twelve-case subset retains compile-small, primitive fixture parse, generated primitive parse, handwritten
canary, packet span parse, scalar memory read, natural scalar path at Index=0, mapped span write, bitfield update,
four-segment parse, UTF-8 write, and the memory async parse. It omits **43 timed cases**: eight other compilation
fixtures, ten other fixture parses, eight other generated/runtime comparisons, five other packet operations,
three reads, five path cases, two writes, one update and one sequence case. This loses large-array/large-schema,
pointer, endian, conditional, union, string-parse, debug, file-stream, generated serialization/view and enumerator
coverage. The exact selected/omitted identities are in the local `coverage.json` artifact. Setup/calibration still
runs all 55 cases in this prototype. The subset failed A/A even with more samples, so it is not recommended as a
gate. A one-case targeted command omits the other 54 and can only speak about that operation.

| Stage | Recorded cost | Scope |
| --- | ---: | --- |
| Initial main checkout Release build | 20.63 s | Dependencies/toolchains already installed; not an empty NuGet cache |
| Snapshot tracked source / build second checkout | 3.87 / 20.31 s | Separate baseline, warm package cache |
| Research host build | 4.46 s build-reported | Additional setup; external wall not recorded for this build |
| No-op managed solution build | 1.79 s | No edited source |
| Core source comment edit and dependent rebuild | 6.22 s | Forces a real rebuild without changing behavior |
| Compare rebuilt immutable bundles | 5.92 s | One observed invocation including report |
| Source edit, rebuild, copy bundles, compare | **12.98 s** | One normal-loop proxy; excludes developer typing |
| Benchmark-only source-change build | 5.23 s | Separate performance-control candidate |

Building two checkouts plus snapshot took 44.81 seconds before the research-host setup. SDK installation,
network restore, a different edit's rebuild graph, and a cold filesystem cache can add cost. No normal edit loop
under ten seconds was demonstrated. Existing binaries are mandatory for every fast timing in this report.
Persistent reuse saves initial startup/setup/JIT (1.56 seconds median for both workers) and preparation
(0.58 seconds), but edited assemblies cannot be silently loaded into the old worker. See the invalidation plan
below; this prototype restarts for every new command.

## Affinity follow-up

The development-tool implementation adds same-CPU affinity after a separate serial A/A experiment on the same
machine. The runner pinned each host before discovery/setup and left the active power plan unchanged. Configurations
rotated across rounds and the starting side reversed each round. Every comparison used fresh hosts and the full
55-case Screen configuration. These counts use medians of **all actual samples**, including BDN summary outliers;
do not compare their totals directly with the earlier filtered-summary campaign.

| Campaign | CPU selection | Comparisons | Raw flags above 3% | 95th percentile absolute A/A delta |
| --- | --- | ---: | ---: | ---: |
| Discovery | Unrestricted | 8 | 140 / 440 | 16.0% |
| Discovery | Logical CPU 8 | 8 | 123 / 440 | 10.8% |
| Discovery | Logical CPU 24 | 8 | 112 / 440 | 9.5% |
| Confirmation | Unrestricted | 12 | 226 / 660 | 15.2% |
| Confirmation | Logical CPU 16 | 12 | 176 / 660 | 10.3% |
| Confirmation | Logical CPU 24 | 12 | 161 / 660 | 10.0% |

All suites still crossed the raw threshold. CPU 16 reduced case flags by 22% relative to unrestricted execution
in the second campaign; CPU 24 reduced them by 29%. Case outcomes within a suite are correlated, so 660 case
observations are not 660 independent trials. The data supports using affinity as a modest noise control here,
not a statistically validated regression detector. It does not establish which core will be best on another
machine, nor isolate cache migration from clock, heap, or OS effects.

The maintained workflow uses `--cpu auto` (the middle allowed logical CPU, 16 here), supports an explicit CPU,
and records the applied mask. Both revisions use the same CPU; they never measure concurrently. It retains
BDN's process/thread priority behavior, uses fresh calibration, checks a hand-written canary, and reports excessive
sample/launch spread as inconclusive instead of suppressing the underlying samples. It does not reserve physical
cores or their SMT siblings, change firmware/power policy, or pin unrelated applications. See the
[.NET affinity documentation](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.process.processoraffinity?view=net-10.0),
[Windows processor-group limits](https://learn.microsoft.com/en-us/windows/win32/procthread/processor-groups), and
[BDN's in-process executor](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Toolchains/InProcess/Emit/InProcessEmitExecutor.cs).

Reproduce the affinity campaigns from the repository root, with no competing build/test/timing command:

```powershell
dotnet build benchmarks/experiments/fast-impact/FastImpact.csproj -c Release
node benchmarks/experiments/fast-impact/affinity.mjs 8 affinity-discovery none,8,24
node benchmarks/experiments/fast-impact/affinity.mjs 12 affinity-confirmation none,16,24
```

The arguments are this machine's logical CPU indices; use allowed indices on another machine. Raw samples,
logs and per-comparison durations are under `artifacts/perf/fast-impact/affinity-discovery/` and
`artifacts/perf/fast-impact/affinity-confirmation/`. Each contains `results.json` and every original BDN report.
The implemented command's checks and timings are in `artifacts/perf/development/runs/implementation-*/` and
`artifacts/perf/fast-impact/implementation-*.json`/`.log`; the production usage is in the
[development guide](../../README.md#development-comparisons).

The maintained command measured 7.907, 7.884 and 7.861 seconds from external invocation through report; capture
took 11.15 seconds internally, and a source edit/rebuild/compare took 18.05 seconds externally. The changed-source
`--no-build` attempt failed before benchmarking. The full A/A screens still emitted 10, 6 and 4 **possible** timing
signals, respectively, while preserving unstable rows as inconclusive. The new labels do not make noise disappear.
Targeted packet A/A confirmation took 37.0 seconds internally, measured −0.5% with a −1.9% to approximately zero
observed launch range and 384 bytes/op on both sides, and remained inconclusive.

For an end-to-end positive control, only the isolated checkout's packet benchmark allocated and retained a
4096-byte scratch array after the original parse. The core library and returned packet were unchanged. Three
confirmation launches per side measured **384 → 4504 bytes/op in every launch**, a +19.8% median timing delta
(observed +15.1% to +21.5%), and a repeatable slowdown signal. Reversing the bundles measured **4504 → 384 bytes/op**
in every launch and −17.2% timing (−17.5% to −14.9%), with a repeatable speedup signal. These are large-control
checks of the maintained workflow, not a newly established 3% detection guarantee. Both bundles and all six
launch reports per direction remain under `artifacts/perf/development/`. This later control replaces the current
isolated checkout's benchmark edit; the earlier immutable `revision-before`/`revision-after` bundles remain intact.

## Reproduce

Use the SDK selected by `global.json` and Node from `.node-version`. Run from the repository root. Finish each
command before starting another benchmark, build, or test. Reports are local, ignored files under
`artifacts/perf/fast-impact/`; the original command writes under `artifacts/perf/quick/`.

```powershell
dotnet build CStructSharp.NonWeb.slnf -c Release
dotnet build benchmarks/experiments/fast-impact/FastImpact.csproj -c Release
node tools/quality/quick-perf-check.mjs --baseline . --job Quick --categories Impact --label investigation-aa-1
node benchmarks/experiments/fast-impact/matrix.mjs current
node benchmarks/experiments/fast-impact/compare.mjs --out artifacts/perf/fast-impact/aa-cold.json --repeats 30
node benchmarks/experiments/fast-impact/matrix.mjs fast
node benchmarks/experiments/fast-impact/matrix.mjs bdn
node benchmarks/experiments/fast-impact/matrix.mjs power
node benchmarks/experiments/fast-impact/matrix.mjs more
$env:DOTNET_TieredCompilation = '0'
dotnet benchmarks/experiments/fast-impact/bin/Release/net10.0/FastImpact.dll audit > artifacts/perf/fast-impact/harness-audit.json
./benchmarks/experiments/fast-impact/revisions.ps1 -Node node
node benchmarks/experiments/fast-impact/analyze.mjs
```

These commands run a study lasting tens of minutes, not one ten-second check. For the single cold custom
comparison, omit `--repeats 30`. The single-command BDN screen is:

```powershell
node benchmarks/experiments/fast-impact/matrix.mjs screen single
```

Its three invocation-to-exit observations are in `artifacts/perf/fast-impact/screen-wall.json`. To apply that
configuration directly to already-built bundles, the equivalent component commands are:

```powershell
$env:DOTNET_TieredCompilation = '0'
$perfHost = 'benchmarks/experiments/fast-impact/bin/Release/net10.0/FastImpact.dll'
dotnet $perfHost bdn 1 12 false true artifacts/perf/fast-impact/screen-before user-power
dotnet $perfHost bdn 1 12 false true artifacts/perf/fast-impact/screen-after user-power
node tools/quality/convert-benchmark-baseline.mjs artifacts/perf/fast-impact/screen-before/results artifacts/perf/fast-impact/screen-before/summary.json
node tools/quality/convert-benchmark-baseline.mjs artifacts/perf/fast-impact/screen-after/results artifacts/perf/fast-impact/screen-after/summary.json
node tools/quality/compare-summaries.mjs --before artifacts/perf/fast-impact/screen-before/summary.json --after artifacts/perf/fast-impact/screen-after/summary.json
```

This example is A/A. Substitute the respective immutable worker DLLs for a real comparison. The printed
production comparator labels are raw threshold labels; this investigation does **not** endorse their statistical
meaning. `matrix.mjs power` measures these complete two-host/report sequences from the parent process.
For the unchanged targeted workflow, use:

```powershell
node tools/quality/quick-perf-check.mjs --baseline . --job Quick --filter '*PacketBenchmarks.ParseSpan*' --label targeted-aa
```

`--baseline .` deliberately compares identical binaries in fresh processes (A/A). It is not a historical
baseline. `compare.mjs --before <FastImpact.dll> --after <FastImpact.dll>` can point to separate built bundles.
Both must expose exactly the same cases. The controller records top-level DLL/JSON hashes; it does not hash all
external fixtures or establish source provenance. `revisions.ps1` separately snapshots tracked HEAD, builds an
isolated checkout and copies immutable bundles. It refuses to overwrite an existing study checkout/bundle.
Use a fresh checkout for a repeat study, or deliberately choose new output paths after retaining previous results.
The normal comparison command does not build or invalidate stale binaries. Do not edit bundles while it runs.

Early dictionary-control measurements preceded the addition of `ParseLight`; their worker setup covered 12
control cases. Current commands select the same 12 timed cases, but discover/prepare both control methods.
The extra untimed setup can change their startup and cache state. Main Impact discovery remains 55 cases.
The final prototype also retains full parameter strings in identities instead of BDN's abbreviated display name;
old raw files contain the abbreviated name for `compile-windows-header` but execute the same fixture.

The benchmark matrix tests 25 ms and 5 ms iterations, forced collection on/off, allocation diagnosis on/off,
and a longer reference with three warmups and thirty 100 ms measurements. These are explicit experimental
configurations, not new named jobs in the production host.

### Artifact map and checks

All paths below are relative to the repository root; raw results are ignored and remain local.

| Location | Contents |
| --- | --- |
| `benchmarks/experiments/fast-impact/` | Research source and reproduction drivers; no duplicated parser implementations |
| `artifacts/perf/fast-impact/analysis.json` | Per-case distributions, directional detection counts, minimum-selection audit and BDN stage totals |
| `artifacts/perf/fast-impact/coverage.json` | All 55 case identities and the exact 43 omitted by the subset |
| `artifacts/perf/fast-impact/aa-*.json`, `controls*.json` | All batch pairs, invocation counts, GC counts, allocation counters and worker metadata |
| `artifacts/perf/fast-impact/matrix-*.json`, `power-comparisons.json` | Exact executed arguments and parent-measured wall times |
| `artifacts/perf/fast-impact/screen-wall.json`, `screen-*.log` | Complete BDN screen invocations, reports and commands |
| `artifacts/perf/fast-impact/profile-*/` | Timestamped phase markers and original BDN full JSON |
| `artifacts/perf/fast-impact/long-*/`, `controls-*-long-*/` | Longer BDN measurements; dictionary controls use `controls-long-1/` |
| `artifacts/perf/fast-impact/current-aa-*-processes.jsonl` | Original-command child-process timing |
| `artifacts/perf/quick/investigation-aa-*/` | Original Quick rounds, minimum-selected summaries and reports |
| `artifacts/perf/fast-impact/revisions.json`, `revision-*/`, `revisions-*.json` | Build timings, distinct bundles, output verification, source-change trials and confirmations; `revision-source.zip` holds tracked source |
| `.local-docs/fast-impact/revision-checkout/` | Isolated edited source; the working library source is unchanged |
| `artifacts/perf/fast-impact/initial-build.json`, `current-aa-1.json`, `harness-audit.json` | First-command wall times and harness/async audit |

Validation after timing: Release `CStructSharp.NonWeb.slnf` build passed; core tests passed **4,245/4,245 on
both net8.0 and net10.0**; FixtureTool `verify` passed **63/63**. The research project builds separately.
Check the added comments, script syntax and whitespace with:

```powershell
dotnet build benchmarks/experiments/fast-impact/FastImpact.csproj -c Release
dotnet build CStructSharp.NonWeb.slnf -c Release
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release --no-build
dotnet benchmarks/CStructSharp.FixtureTool/bin/Release/net10.0/CStructSharp.FixtureTool.dll verify
node tools/quality/changed-documentation.mjs --base HEAD --language csharp
node tools/quality/changed-documentation.mjs --base HEAD --language script
git diff --check
```

## Measurement protocol and limits

The lightweight runner asks BenchmarkDotNet to expand the existing categories and parameters. It gives each
case its own original fixture instance and compiles a typed loop that calls the original method. Reflection,
setup, and JSON reporting are outside the operation timer but inside the comparison's wall-clock budget.
Every result is consumed without boxing. The `ValueTask<T>` adapter is valid for Impact's synchronously completed
memory-stream operation; it is not a general adapter for incomplete `IValueTaskSource` operations. Unsupported
iteration hooks or operation multipliers are rejected.

Coverage is nine compilation cases, eleven fixture parses, ten generated/runtime comparison cases, six packet
operations, four reads, six path cases, three writes, two updates, two sequence cases, one UTF-8 write and one
async memory-stream parse. The three natural scalar path reads repeat the same operation at different unused
`Index` values because BenchmarkDotNet expands the class parameters. The experiment preserves those cases.
Impact does not cover genuine asynchronous file completion, async writing/updating, every fixture, or cold disk
I/O. The packet file-stream case deliberately reads a buffered 62-byte file.

Each fresh worker calls every selected operation, calibrates by increasing invocation counts until a batch
takes at least 1 ms, then performs an untimed batch at the requested sample size. Both sides use the larger
calibrated invocation count, so a pair performs equal work. Cases are shuffled with a recorded seed. Each case
has twelve pairs with balanced AB/BA execution order. Only one worker executes a batch at a time. No slow
samples are discarded. The default experiment measures all 55 cases on both sides.

The comparison reports both a raw median-ratio flag at 3% and an experimental two-sided sign test corrected
for 55 comparisons with Bonferroni. The latter assumes independent, symmetric paired signs under identical
code. It is a hypothesis to audit with A/A measurements, not a confidence guarantee. Within-process timing
samples cannot reveal all process-level bias. Raw samples, collections, allocations, and every selected ratio
are retained. A missing flag does not demonstrate equivalence.

`Controls.cs` adds redundant dictionary membership checks to the original packet parse. Removing these checks
is an example of removing repeated validation while preserving the returned value. Each operation still parses
exactly one packet. A separate parameter adds a scratch byte array. Setup checks the original serializers'
bytes and control result fingerprints before timing. Both runners execute the same control method;
there is no copied parser or synthetic sleep. These controls test detection, not a proposed library patch.
Their measured effects must be established by longer runs; their parameter values are not percentages.
`ParseLight` uses cheaper count checks to probe smaller effects. The no-inlining annotations make these removable
helper calls observable; the controls do not establish the sensitivity of every possible library optimization.

Persistent mode retains the two worker heaps and code across repeated *unchanged* comparisons in one command.
It does not implement a service that accepts edited binaries. Every new command starts workers and calibration
again. Warm persistent timings must not be presented as the cost of editing, rebuilding, and comparing code.

## Smallest implementation worth considering

1. Keep the production jobs and scripts as the reference workflow. If adopting a fast screen, prefer a small
   BenchmarkDotNet job configuration over maintaining a second engine. Make its output explicitly provisional.
   A deadline must produce an incomplete/inconclusive report, never silently omit expensive cases.
2. Before measurement, snapshot both built bundles and record their hashes, revision and dirty-source identity,
   target framework, runtime, fixtures, generated code, job, GC/tiering/PGO, power plan and affinity. Verify exact
   method/parameter membership. Missing cases or diagnostics must fail the comparison. The current converter's
   revision comes from the controlling checkout, so it cannot establish the baseline binary's provenance.
3. Retain every launch and sample. Remove smallest-median aggregation from any future inference path. Report
   timing and allocation separately, with explicit uncertainty and a suite-level false-alarm budget. Calibrate
   that policy against fresh-process A/A trials before assigning improved/regressed labels.
4. Prefer immutable baseline reuse if startup warrants a service. Restart the candidate on *any* bundle or fixture
   hash change; invalidate calibration on those changes and runtime/settings changes. Reject requests while a
   build can overwrite the bundle. A PID and a path are insufficient cache keys. Never reuse old measurements
   as if they were a newly paired baseline.
5. For adaptive sampling, use a fixed pilot only to allocate work, then independent confirmation batches with a
   predefined error budget and deadline. Repeatedly testing accumulating samples without correction increases
   false alarms. The present sign test needs at least twelve unanimous pairs even to cross `0.05 / 55`; fewer
   samples cannot support that particular rule. Uncertain cases should leave the fast budget for targeted BDN.
   An adaptive inference engine was not implemented or validated here; this is a proposed experiment, not a
   measured way around the failed fixed-batch results.
6. Verify outputs outside timing using canonical fixtures, managed tests, generated parity, and explicit
   benchmark-output checks. Add functional tests for discovery, missing cases, stale bundles, failure/cleanup,
   allocation accounting and report decisions if this becomes maintained tooling. Keep timing gates out of CI.
   Update this report, `benchmarks/README.md`, script help and `CHANGELOG.md`; run the managed area checks and the
   documentation-comment checks. Documentation under `docs/` also requires its full validator.

## Interpretation rules

- **Improved or regressed:** reserve these labels for repeated independent confirmation with equivalent outputs.
  For a 3% timing margin, an adjusted interval entirely below −3% supports improved; entirely above +3% supports
  regressed. Report time and allocation separately and call a disagreement a tradeoff. None of the tested short
  methods established valid intervals for this rule.
- **Inconclusive:** use this when observations cross zero or the practical margin, vary by order/process,
  fail the canary, disagree between runtime modes, or exhaust the time budget. No flag is also inconclusive
  unless an equivalence interval lies entirely inside the acceptable margin.
- **Needs confirmation:** every short-run timing flag needs a longer run of the affected original methods.
  Preserve all launches. Use the intended production tiering/PGO settings as well as the diagnostic mode when
  that distinction matters. Never promote repeated looks at the same samples into independent evidence.
- **Allocation change:** check bytes per operation separately from time. Confirm a stable whole-byte change
  in independent batches and the original memory diagnoser. Missing allocation diagnostics are unknown,
  never zero. An async test must wait for its actual continuation work to finish.

## Sources

- [BenchmarkDotNet measurement stages](https://benchmarkdotnet.org/articles/guides/how-it-works.html)
  and [job configuration](https://benchmarkdotnet.org/articles/configs/jobs.html).
- Version-pinned [engine](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/Engine.cs),
  [factory](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/EngineFactory.cs),
  [pilot](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/EnginePilotStage.cs),
  and [allocation counters](https://github.com/dotnet/BenchmarkDotNet/blob/v0.15.8/src/BenchmarkDotNet/Engines/GcStats.cs).
- .NET [tiering and PGO settings](https://learn.microsoft.com/en-us/dotnet/core/runtime-config/compilation),
  [JIT compilation time](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.jitinfo.getcompilationtime?view=net-10.0),
  [GC pause time](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalpauseduration?view=net-10.0),
  [process allocations](https://learn.microsoft.com/en-us/dotnet/api/system.gc.gettotalallocatedbytes?view=net-10.0),
  and [thread allocations](https://learn.microsoft.com/en-us/dotnet/api/system.gc.getallocatedbytesforcurrentthread?view=net-10.0).
