# Performance measurement

`CStructSharp.Benchmarks/` contains BenchmarkDotNet timing/allocation cases that reference core. Benchmark output is
ignored; no timing baseline is stored in the repository. Performance checks are manual measurements on a quiet
machine, not a timing gate in CI: shared runners are too noisy to compare with a recorded baseline. A change is
judged by a before/after comparison of the same cases on the same machine
([Check a change quickly](#check-a-change-quickly-the-impact-category)). Retain instability and canary warnings, and do
not refresh baselines merely to make a result look better.

## Layout

| Path | Purpose |
| --- | --- |
| `CStructSharp.Benchmarks/` | BenchmarkDotNet host. `Scenarios/` holds one fixture-driven class per operation (category `Scenario`: compile, parse, stream, path and typed reads, write, update, debug, malformed input, hand-written comparators); the classes beside it measure single operations (addresses, reads, text writes, memory analysis in category `Memory`); `GeneratedBenchmarks` (category `Generated`) compares generated code with the runtime, `AsyncBenchmarks` (category `Async`) the stream forms with their awaitable twins, `SequenceBenchmarks` (category `Sequences`) segmented input and record sequences (`ParseMany`, `Records`, the view enumerator) with the loops a caller would write, `PacketBenchmarks` (category `Packet`) the compiled engine's reader and writer on the comparison's data-dependent record, and `CostModelBenchmarks` (category `CostModel`) synthetic layouts whose differences give the engine's cost per call and per member. The `Gate` category selects the headline cases for a careful run with the Gate job, and `Impact` the quick before/after subset. `--profile <scenario>` runs a manual loop for sampling profilers. |
| `CStructSharp.Benchmarks/GeneratedLayouts/` | The `[CStructLayout]` classes the `Generated`, `Async` and `Sequences` cases use. An attribute needs a constant string, so each class copies a fixture's definition, root and options. `tools/quality/benchmark-generated-layouts.test.mjs` (run in CI) fails when a copy differs from the fixture that `FixtureCase.LoadMatching` pairs it with. |
| `CStructSharp.Comparison/` | The serializer comparison shown in the root README: a fixed 79-byte record and a data-dependent record deserialized and serialized by CStructSharp and by other .NET serializers. It is outside both solutions; see [Compare with other serializers](#compare-with-other-serializers). |
| `CStructSharp.FixtureTool/` | Fills and verifies `fixtures/` expectations with the managed library; also the shared fixture loader the benchmarks use. |
| `fixtures/` | Seeded fixture corpus shared by .NET, Node, and browser harnesses (see its README). |
| `js/` | Node + headless-Chromium harness for the WASM bridge (see its README). |
| `profiling/` | `perf` and Chrome DevTools Protocol profiling scripts. |

## Run the .NET benchmarks

```sh
dotnet build ./CStructSharp.NonWeb.slnf -c Release
# Scenario matrix, both target frameworks, Short job (1 launch, 3 warmups, 5 iterations):
CSTRUCTSHARP_BENCHMARK_JOB=Short CSTRUCTSHARP_BENCHMARK_RUNTIMES=net10.0,net8.0 \
  dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 --no-build -- --filter '*' --anyCategories Scenario
# Headline cases, Gate job (3 launches, 5 warmups, 8 iterations), net10.0 only:
CSTRUCTSHARP_BENCHMARK_JOB=Gate dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 --no-build -- \
  --filter '*' --anyCategories Gate
# Cold start (5 fresh processes, one measured call each):
CSTRUCTSHARP_BENCHMARK_JOB=ColdStart dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 --no-build -- \
  --filter '*Scenarios.CompileBenchmarks*'
```

Environment variables: `CSTRUCTSHARP_BENCHMARK_JOB` = `Dry` | `Short` | `Gate` | `ColdStart`;
`CSTRUCTSHARP_BENCHMARK_RUNTIMES` = comma list of `net8.0`, `net10.0` (default `net10.0`);
`CSTRUCTSHARP_BENCHMARK_ARTIFACTS` = output directory (default `artifacts/baseline/benchmarks`);
`CSTRUCTSHARP_BENCHMARK_PROFILE=cpu` adds the EventPipe CPU-sampling diagnoser (writes `.nettrace` per case).

Normalize and compare:

```sh
node tools/quality/convert-benchmark-baseline.mjs <results dir or report-full.json> artifacts/summary.json
node tools/quality/compare-summaries.mjs --before before.json --after artifacts/summary.json --threshold 0.05
```

`convert-benchmark-baseline.mjs` is the only converter. Compare two summaries measured on the same machine; a
summary from another machine or another job is not a baseline.

The "Typical costs" tables in `docs/guides/performance.md` are rendered from the committed record
`CStructSharp.Benchmarks/typical-costs.json` and the packed runtime size in `npm-package.json`. To refresh them,
measure the shown cases (`node tools/quality/render-performance-table.mjs --filters` prints the `--filter` globs for
a `Short` run), convert the report, and record it together with the JS harness's `node-latest.json`:
`node tools/quality/render-performance-table.mjs --summary <summary.json> --js <node-latest.json>`. After
`npm run pack:npm`, `node tools/documentation/sync-documentation-facts.mjs --record-package` records the package
and runtime sizes. The documentation validator runs `sync-documentation-facts.mjs --check`, which fails when a page
no longer matches these records or `CStructSharp.Comparison/results.json`.

## Check a change quickly: the Impact category

The full suite takes about 45 minutes. The `Impact` category is a subset of 46 cases that covers every
execution path a change can affect: compilation, span parses of eleven fixtures chosen for their differences
(`ImpactParseBenchmarks`: fixed records, nested structs, big-endian arrays, runtime counts, conditions, strings, a
real file header, pointers, bitfields, unions, alias spellings), generated parse, view and serialize, a hand-written
canary, paths, typed reads, serialize and update, debug ranges, async and segmented input, and the data-dependent
`packet` record (`PacketBenchmarks`: parse from a span, a `MemoryStream` and a `FileStream`, read into a mapped class,
serialize a `StructValue` and a mapped instance). One run takes about eight minutes:

```sh
dotnet build ./CStructSharp.NonWeb.slnf -c Release
CSTRUCTSHARP_BENCHMARK_JOB=Short dotnet run --project benchmarks/CStructSharp.Benchmarks -c Release -f net10.0 \
  --no-build -- --filter '*' --anyCategories Impact
```

To compare a change with the code before it, build a second checkout of the earlier revision and let
`quick-perf-check.mjs` run both, interleaved, keeping the best median of each case. The `Quick` job measures the
whole `Impact` category on both sides, twice, in about a minute and forty seconds:

```sh
git worktree add ../cstructsharp-before HEAD
dotnet build ../cstructsharp-before/CStructSharp.NonWeb.slnf -c Release
node tools/quality/quick-perf-check.mjs --baseline ../cstructsharp-before --job Quick --categories Impact
git worktree remove ../cstructsharp-before
```

It prints each case's median and allocation before and after, flags differences above `--threshold` (default
3%), and reports how long the measurement took. Confirm a flagged case by rerunning only its class with
`--filter '*PathAndTypedBenchmarks*' --rounds 3` (under a minute): a real change repeats, noise does not. Nothing
else should run on the machine meanwhile.

The `Quick` job is fast because it avoids BenchmarkDotNet's fixed costs rather than measuring less carefully per
iteration:

- It runs the benchmarks in the host process, so no project is generated, built, or started for each case.
- Iterations last 25 ms instead of 500 ms, with one warmup and five measured iterations, and no separate
  overhead evaluation (both sides pay the same empty-loop cost).
- `quick-perf-check.mjs` starts the built host directly instead of through `dotnet run`.
- The host runs with tiered compilation off (`DOTNET_TieredCompilation=0`), so every method is fully optimized on
  its first call. With tiering on, a short in-process run measures a mix of unoptimized and optimized code that
  changes from run to run; without it, both sides measure optimized code, only without dynamic PGO.

Absolute Quick-job times are therefore higher than Short-job or Gate-job times and are not comparable with
them; use Quick for before/after comparisons only. The baseline checkout needs a benchmark host that knows the
`Quick` job: for a revision older than the job, copy `benchmarks/CStructSharp.Benchmarks/Program.cs` into it before
building. `--job Short` (the default) runs the out-of-process Short job, about seven minutes per side.

To compare two summaries you already have (for example a recorded run and a new one), convert each BenchmarkDotNet
report with `tools/quality/convert-benchmark-baseline.mjs` and compare them:

```sh
node tools/quality/compare-summaries.mjs --before before.json --after after.json --threshold 0.05
```

`--strict` makes it exit with code 1 when a case's median or allocation grew beyond the threshold.

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
  but the offsets of most members are known only after the earlier bytes are read. `CStructSharp.Benchmarks`
  compiles the same layout, mapped class, and sample from these source files for its `Packet` cases.

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
| Runtime rows of the data-dependent record | The compiled engine: each struct is compiled once into a program of small steps, which reads or writes the members in order and evaluates lengths and conditions while the bytes are read. The mapped class is filled by name after a full read, because only fixed layouts get a direct reader |

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
  and the data-dependent record shows the compiled engine that every other layout uses.
- Timings move by a few percent between runs, and more on laptops (power management, boost clocks) or shared
  virtual machines. Close the other programs and do not build or run tests while measuring. Treat differences
  below about 10 % as noise.
- Each table is sorted by deserialize time, fastest first. The serialize column is not sorted.

### Run it

The project is kept out of `CStructSharp.sln` (and so out of the `CStructSharp.NonWeb.slnf` filter) so that its third-party packages never
enter the library build, its tests, or the release artifacts.

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

`MemoryAnalysisBenchmarks` measures cross-page selected reads, cached reads, one scalar member read by name from a
byte-array source (`ScalarReadByName`), ISF import, bounded traversal, 4,096 stored-pointer links, a selected field in
a sparse one-million-byte record, and mapped offline updates.
Run `--filter '*MemoryAnalysisBenchmarks*'` with the same Release job and runtime
settings shown above. The synthetic consumer at `docs/examples/memory-analysis` also checks source-request
budgets, physical fragments, and preservation of the original image. Its sources do not depend on real captures.

For a before/after comparison, preserve a separate checkout and its Release binaries before editing code.
Run both checkouts repeatedly on the same machine with identical filters, runtime, input data, and job settings.
Keep separate artifact directories using `CSTRUCTSHARP_BENCHMARK_ARTIFACTS`. Compare allocations as well as timing;
do not run builds, tests, or mutation analysis while benchmarks are measuring.

## Cost model of the compiled engine

The *compiled engine* reads a layout member by member through a program compiled once per struct, and runs whenever a
root has no fixed size (for example, because an array's length comes from the data); see
[how a layout is run](../docs/guides/performance.md#how-a-layout-is-run-the-compiled-engine). `CostModelBenchmarks` parses seven synthetic layouts from
memory. Each one differs from another by one kind of member, so subtracting two medians gives the cost of that member:

| Shape | Members before `uint8 n; uint8 tail[n];` (`n = 0`) | Compare with | Gives |
| --- | --- | --- | --- |
| `fixed-1` | only `uint8 a;`, no tail: a fixed root | – | the direct fixed-root reader, for contrast |
| `general-1` | none | – | the fixed cost of one call |
| `scalars-16`, `scalars-32` | 16 or 32 `uint32` | each other | the cost of one scalar field (difference / 16) |
| `plain-8`, `conditional-8` | `uint8 k;` and 8 `uint32`, plain or each in `if (k == 1) { … } else { … }` | each other | the extra cost of one conditional group (difference / 8) |
| `char8-8` | 8 `char[8]` | `general-1` | the cost of one `char[8]` (difference / 8) |

The trailing runtime-counted array keeps every shape but `fixed-1` off the fixed-size fast paths. Seven cases take
about five seconds with the Quick job:

```sh
CSTRUCTSHARP_BENCHMARK_JOB=Quick DOTNET_TieredCompilation=0 \
  dotnet benchmarks/CStructSharp.Benchmarks/bin/Release/net10.0/CStructSharp.Benchmarks.dll --filter '*CostModel*'
```

## Profiling

```sh
benchmarks/profiling/profile-dotnet.sh ParsePrimitiveArray1KiB 10 artifacts/profiles     # Linux perf, inclusive frames
#   scenarios: CompileSmall, CompileMedium, ParsePrimitiveArray1KiB, ParseNestedUnaligned, SerializeNested256, ReadTypedNested256, ParseCondIf128, ParseDynamic1024,
#   SerializePocoToSpan, ParseRealPng, ParseArrayU32Be (benchmarks/CStructSharp.Benchmarks/ProfileDriver.cs)
node benchmarks/js/bench/profile-browser.mjs real-png 2000 artifacts/profiles            # Chromium CDP CPU profile
```

On any operating system, `CSTRUCTSHARP_BENCHMARK_PROFILE=cpu` samples one benchmark case with EventPipe and writes a
`.nettrace` and a `.speedscope.json` (open it at [speedscope.app](https://www.speedscope.app)) into the artifacts
directory. Use the Short job: the in-process Quick job does not support the profiler. A filter can include a
parameter value, so one fixture takes about 30 seconds:

```sh
CSTRUCTSHARP_BENCHMARK_JOB=Short CSTRUCTSHARP_BENCHMARK_PROFILE=cpu CSTRUCTSHARP_BENCHMARK_ARTIFACTS=artifacts/profiles \
  dotnet benchmarks/CStructSharp.Benchmarks/bin/Release/net10.0/CStructSharp.Benchmarks.dll \
  --filter '*ImpactParseBenchmarks*cond-if128*'
```

## Anti-benchmarking rules

The `Scenarios.ComparatorBenchmarks.HandWritten_*` cases exercise no library code, so they are the canary for a
measuring run: if any of them moves more than 10 % against the other side of the comparison, the machine was perturbed during the run
(this happens on shared VMs) — discard the run and repeat it rather than re-recording from it.


Release builds only; record `dotnet --info`/`process.versions`/CPU in every result (the converters and JS harness do
this); warm up before measuring; never compare means alone — the contracts store medians, allocations, and RSD, and a
case with RSD above 0.35 is reported as unstable instead of gated. Run nothing else on the machine while measuring.
