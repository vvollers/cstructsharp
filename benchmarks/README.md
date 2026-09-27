# Performance measurement

`CStructSharp.Benchmarks/` contains BenchmarkDotNet timing/allocation cases that reference core. Baselines live under
`contracts/performance/`; benchmark output is ignored. Performance checks are maintained manual measurements plus a
non-failing drift report in CI (`.github/workflows/benchmark-drift.yml`), not an automatic timing gate on every push.

Pull requests changing the runtime, shared compiler, source generator, benchmark inputs or shared build/toolchain
configuration schedule that report. Weekly and manual runs remain available. Timing comparisons stay advisory:
retain instability and canary warnings, and do not refresh baselines merely to make a report look better.

## Layout

| Path | Purpose |
| --- | --- |
| `CStructSharp.Benchmarks/` | BenchmarkDotNet host. Original release-gate cases (`ReleaseGate` category) plus the Phase 0 `Baseline0/` scenario-matrix cases and hand-written comparators; `GeneratedBenchmarks` (category `Generated`) compares generated code with the runtime, `AsyncBenchmarks` (category `Async`) the stream forms with their awaitable twins, `SequenceBenchmarks` (category `Sequences`) segmented input and record sequences (`ParseMany`, `Records`, the view enumerator) with the loops a caller would write. The release gate holds 18 cases: 15 runtime, 2 generated parses, and the generated view enumerator. `--profile <scenario>` runs a manual loop for sampling profilers. |
| `CStructSharp.Comparison/` | The serializer comparison shown in the root README: a fixed 79-byte record and a data-dependent record deserialized and serialized by CStructSharp and by other .NET serializers. It is outside both solutions; see [Compare with other serializers](#compare-with-other-serializers). |
| `CStructSharp.FixtureTool/` | Fills and verifies `fixtures/` expectations with the managed library; also the shared fixture loader the benchmarks use. |
| `fixtures/` | Seeded fixture corpus shared by .NET, Node, and browser harnesses (see its README). |
| `js/` | Node + headless-Chromium harness for the WASM bridge (see its README). |
| `profiling/` | `perf` and Chrome DevTools Protocol profiling scripts. |

## Run the .NET benchmarks

```sh
dotnet build ./CStructSharp.NonWeb.sln -c Release
# Phase 0 scenario matrix, both target frameworks, Short job (1 launch, 3 warmups, 5 iterations):
CSTRUCTSHARP_BENCHMARK_JOB=Short CSTRUCTSHARP_BENCHMARK_RUNTIMES=net10.0,net8.0 \
  dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 --no-build -- --filter '*Baseline0*'
# Release-gate cases, Gate job (3 launches, 5 warmups, 8 iterations), net10.0 only:
CSTRUCTSHARP_BENCHMARK_JOB=Gate dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 --no-build -- \
  --filter '*' --anyCategories ReleaseGate
# Cold start (5 fresh processes, one measured call each):
CSTRUCTSHARP_BENCHMARK_JOB=ColdStart dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 --no-build -- \
  --filter '*Baseline0.CompileBenchmarks*'
```

Environment variables: `CSTRUCTSHARP_BENCHMARK_JOB` = `Dry` | `Short` | `Gate` | `ColdStart`;
`CSTRUCTSHARP_BENCHMARK_RUNTIMES` = comma list of `net8.0`, `net10.0` (default `net10.0`);
`CSTRUCTSHARP_BENCHMARK_ARTIFACTS` = output directory (default `artifacts/baseline/benchmarks`);
`CSTRUCTSHARP_BENCHMARK_PROFILE=cpu` adds the EventPipe CPU-sampling diagnoser (writes `.nettrace` per case).

Normalize and compare:

```sh
node tools/quality/convert-benchmark-baseline.mjs <results dir or report-full.json> artifacts/summary.json
node tools/quality/compare-benchmark-baseline.mjs --baseline contracts/performance/non-web-rc2.json --summary artifacts/summary.json
node tools/quality/non-web-release-budgets.mjs --benchmark-summary-path artifacts/summary.json   # rc1 hard gate
```

`convert-benchmark-baseline.mjs` is the only converter.

The "Typical costs" table in `docs/guides/performance.md` is rendered from a converted summary (plus the JS
harness's `node-latest.json` and the web artifact measurement) by
`node tools/quality/render-performance-table.mjs --summary <summary.json> --js <node-latest.json> --web artifacts/performance/web.json`;
`--check` reports whether the page still matches the inputs. Re-render it when a release re-baselines the
performance contracts.

## Check a change quickly: the Impact category

The full suite takes about 45 minutes. The `Impact` category is a subset of about 30 cases that covers every
execution path a change can affect: compilation, span parses of eleven fixtures chosen for their differences
(`ImpactParseBenchmarks`: fixed records, nested structs, big-endian arrays, runtime counts, conditions, strings, a
real file header, pointers, bitfields, unions, alias spellings), generated parse, view and serialize, a hand-written
canary, paths, typed reads, serialize and update, debug ranges, async and segmented input. One run takes about five
minutes:

```sh
dotnet build ./CStructSharp.NonWeb.sln -c Release
CSTRUCTSHARP_BENCHMARK_JOB=Short dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 \
  --no-build -- --filter '*' --anyCategories Impact
```

To compare a change with the code before it, build a second checkout of the earlier revision and let
`quick-perf-check.mjs` run both, interleaved, keeping the best median of each case:

```sh
git worktree add ../cstructsharp-before HEAD
dotnet build ../cstructsharp-before/CStructSharp.NonWeb.sln -c Release
node tools/quality/quick-perf-check.mjs --baseline ../cstructsharp-before --categories Impact --rounds 1
git worktree remove ../cstructsharp-before
```

It prints each case's median and allocation before and after, and flags differences above `--threshold`
(default 3%). Short-job medians vary by a few percent between runs; confirm a flagged case with `--rounds 2`
before treating it as a regression. Nothing else should run on the machine meanwhile.

## Compare with other serializers

`CStructSharp.Comparison/` produces the three tables in the root README's "Speed compared with other .NET
serializers" section. This section explains what those numbers are, how they are produced, and how far they can
be trusted.

### What is measured

A *benchmark case* is one method that performs one operation on one record. BenchmarkDotNet calls it millions of
times and reports how long one call takes. Every case works on one of two records:

- **The fixed record** (`Model/ReadingLayout.cs`): 79 bytes, packed (no padding), little-endian, with nested
  structs and a fixed array of eight `int32` samples. Every member has the same offset in every record.
- **The data-dependent record** (`Variable/PacketLayout.cs`): a count-sized `int32` array, a length-prefixed
  name, a member chosen by `kind` (`float64` or `uint32`), and a zero-terminated note. The sample is 62 bytes,
  but the offsets of most members are known only after the earlier bytes are read.

The cases fall into four groups, each a BenchmarkDotNet category:

| Category | Class | What each case does |
| --- | --- | --- |
| `SameBytes` | `DeserializeBenchmarks`, `SerializeBenchmarks` | Reads or writes the fixed record's exact 79 bytes |
| `OwnFormat` | `DeserializeBenchmarks`, `SerializeBenchmarks` | Reads or writes the same values in the library's own format |
| `VariableRead` | `VariableBenchmarks` | Reads the data-dependent record |
| `VariableWrite` | `VariableBenchmarks` | Writes the data-dependent record |

**Deserialize cases** decode one record from a byte array held in memory and then read every member once into a
*fingerprint*, a cheap hash that all cases compute the same way (`Fingerprints.cs`, `PacketSample.cs`). Reading
every member matters: a *lazy* reader, such as a generated view or FlatSharp, decodes a member only when it is
read, so without the fingerprint it would appear to do almost nothing. The fingerprint's own cost is a few
nanoseconds and is included in every deserialize row alike.

**Serialize cases** write one record from an object that already exists (built once in `[GlobalSetup]`) into a
destination the benchmark owns and reuses: a byte array for writers that fill a span, an `ArrayBufferWriter<byte>`
for writers that append. Reusing the destination keeps the measurement on encoding; allocating the output array
would add the same cost to every row.

The CStructSharp rows show the different ways to use one layout:

| Row | How it works |
| --- | --- |
| Generated view | Source-generated `readonly ref struct` over the bytes; each member is decoded when read, nothing is allocated |
| Generated `Parse` / `Serialize` | Source-generated class and straight-line reading and writing code, emitted at build time |
| Runtime `ReadValue<T>` / `Serialize` (layout-bound mapped class) | Layout compiled at run time; a `[CStructMapped(Layout = ...)]` class whose generated direct reader and writer the runtime uses when the layout fingerprint matches |
| Runtime view (`CreateView` + accessors) | Layout compiled at run time; members decoded from the bytes through prepared accessors, nothing allocated |
| Runtime `Parse` + accessors | Layout compiled at run time; the record decoded into a `StructValue`, members read through prepared accessors |
| Runtime `Parse` / `Serialize` (`StructValue`, path strings) | As above, but each member is found by a path string such as `"samples[3]"`, resolved again on every lookup |
| Runtime rows of the data-dependent record | The general reader and writer: the layout is walked field by field, and lengths and conditions are evaluated while the bytes are read. The mapped class is filled by name after a full read, because only fixed layouts get a direct reader |

### How it is measured

- BenchmarkDotNet's `default` job runs each case in its own process, in Release, after a pilot stage that picks the
  number of calls per measurement and a warm-up that lets the just-in-time (JIT) compiler reach its optimized code.
  It then measures until the result is statistically stable and reports the **median**, which is less sensitive to
  occasional interruptions than the mean.
- The memory diagnoser counts the bytes allocated on the managed heap per call (**Allocated**). It does not measure
  the cost of collecting that garbage later, which grows with allocation and depends on the rest of the program.
- Only .NET 10 on one x64 machine is measured. The README table states the processor, operating system, runtime
  version, BenchmarkDotNet version, job, and date.
- Before any timing, the script runs every case once and checks its output (`Verification.cs`): each deserializer
  must return the sample record, each `SameBytes` and variable serializer must write exactly the reference bytes,
  and each `OwnFormat` serializer must write bytes its own library reads back to the same values. A
  `[Benchmark]` method without a check fails this step, so no row can time a broken implementation.

### What the numbers mean

A row's time is the cost of one operation in a tight loop, with the input and all code already in the processor's
caches. It is the right number for comparing the *processing cost* of approaches and for estimating throughput
when a program handles many records in memory (multiply by the record count). Comparing rows within one table is
meaningful; comparing absolute times across machines is not.

### What the numbers do not measure

- **One-time costs.** Compiling a layout (`new CStruct(text)`), building accessors, the first call of each method
  (JIT compilation, *cold start*), and loading types are excluded. A program that parses one record and exits is
  dominated by these; `CStructSharp.Benchmarks` has separate `Compile` and `ColdStart` measurements.
- **Input and output.** All bytes are in memory. Reading from a file, socket, or pipe usually costs far more than
  decoding, and the async and stream overloads add their own costs.
- **Larger or different records.** Two small records cannot represent every format. Long arrays favor block reads,
  deeply nested or pointer-heavy layouts cost more per byte, and data that does not fit the processor's caches is
  slower for every approach.
- **Garbage collection and concurrency.** Allocation is reported, but the time the collector later spends is not,
  and every case runs on one thread without contention.
- **Equal safety.** The approaches check different things. `MemoryMarshal` copies whatever bytes are present; the
  hand-written code relies on the bounds checks of `Span<T>`; CStructSharp also enforces its read limits and
  reports the failing field and offset. Faster rows are not doing the same work.
- **Format qualities.** In the `OwnFormat` table the libraries write different formats. Size is shown, but schema
  evolution, versioning, cross-language support, and self-description differ and are not scored.
- **Developer time.** Hand-written code is fast but must be written, reviewed, and kept in sync with the format by
  hand for every layout; the table cannot show that cost.

### Limitations and considerations

- `MemoryMarshal.Read`/`Write` and `Marshal` only match the fixed record because its C# struct is declared with
  `Pack = 1` and the machine is little-endian. `MemoryMarshal.Write` is one 79-byte copy, below the resolution
  of the measurement. None of the stock .NET approaches can express the data-dependent record, so that table has
  only CStructSharp and hand-written code.
- Views and Kaitai Struct have no serialize column: a view reads in place (write with the generated `Serialize` or
  the typed setters), and the Kaitai Struct C# runtime only reads.
- FlatSharp runs in lazy mode, the mode that suits reading a record once. Other FlatSharp modes trade time for
  allocation differently.
- The runtime's fast paths apply to fixed-size structs. The fixed record therefore shows the runtime at its best,
  and the data-dependent record shows the general reader and writer that every other layout uses.
- Timings move by a few percent between runs, and more on laptops (power management, boost clocks) or shared
  virtual machines. Close the other programs and do not build or run tests while measuring. Treat differences
  below about 10 % as noise.
- Each table is sorted by deserialize time, fastest first. The serialize column is not sorted.

### Run it

The project is kept out of `CStructSharp.sln` and `CStructSharp.NonWeb.sln` so that its third-party packages never
enter the library build, its tests, or the release gate.

```sh
node tools/quality/comparison-benchmarks.mjs               # build, verify, measure (default job), update README
node tools/quality/comparison-benchmarks.mjs --job short   # a quicker, noisier measurement
node tools/quality/comparison-benchmarks.mjs --verify      # build and check every case once, no timing
node tools/quality/comparison-benchmarks.mjs --render      # re-render the README tables from results.json
node tools/quality/comparison-benchmarks.mjs --check       # fail when the README tables differ from results.json
```

A full measurement takes about 15 minutes. The measured medians, allocations, encoded sizes, and machine
description go to `CStructSharp.Comparison/results.json` (committed). The README block between the
`comparison-benchmarks` markers is rendered from that file. The script's row lists (`SAME_BYTES_ROWS`,
`OWN_FORMAT_ROWS`, `VARIABLE_ROWS`) decide which methods appear in the tables; the renderer sorts them. To add a
case, add the `[Benchmark]` method with a check in `Verification.cs`, add it to a row list, and measure again.

`Kaitai/SensorReading.g.cs` is compiled from `Kaitai/sensor_reading.ksy` and committed, so a run needs no Kaitai
compiler. After changing the schema, run `node tools/quality/comparison-benchmarks.mjs --regenerate-kaitai`. It
uses the npm build of `kaitai-struct-compiler`, so no Java is required.

## Memory analysis workloads

`MemoryAnalysisBenchmarks` measures cross-page selected reads, cached reads, ISF import, bounded traversal,
4,096 stored-pointer links, a selected field in a sparse one-million-byte record, and mapped offline updates.
Run `--filter '*MemoryAnalysisBenchmarks*'` with the same Release job and runtime
settings shown above. The synthetic consumer at `docs/examples/memory-analysis` also checks source-request
budgets, physical fragments, and preservation of the original image. Its sources do not depend on real captures.

For a before/after comparison, preserve a separate checkout and its Release binaries before editing code.
Run both checkouts repeatedly on the same machine with identical filters, runtime, input data, and job settings.
Keep separate artifact directories using `CSTRUCTSHARP_BENCHMARK_ARTIFACTS`. Compare allocations as well as timing;
do not run builds, tests, or mutation analysis while benchmarks are measuring.

## Profiling

```sh
benchmarks/profiling/profile-dotnet.sh ParsePrimitiveArray1KiB 10 artifacts/profiles     # Linux perf, inclusive frames
#   scenarios: CompileSmall, CompileMedium, ParsePrimitiveArray1KiB, ParseNestedUnaligned, SerializeNested256, ReadTypedNested256, ParseCondIf128, ParseDynamic1024,
#   SerializePocoToSpan, ParseRealPng, ParseArrayU32Be (benchmarks/CStructSharp.Benchmarks/ProfileDriver.cs)
node benchmarks/js/bench/profile-browser.mjs real-png 2000 artifacts/profiles            # Chromium CDP CPU profile
```

## Anti-benchmarking rules

The `Baseline0.ComparatorBenchmarks.HandWritten_*` cases exercise no library code, so they are the canary for a
recording run: if any of them drifts more than 10 % against the contract, the machine was perturbed during the run
(this happens on shared VMs) — discard the run and repeat it rather than re-recording from it.


Release builds only; record `dotnet --info`/`process.versions`/CPU in every result (the converters and JS harness do
this); warm up before measuring; never compare means alone — the contracts store medians, allocations, and RSD, and a
case with RSD above 0.35 is reported as unstable instead of gated. Run nothing else on the machine while measuring.
