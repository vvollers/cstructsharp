# Mutation testing

Code coverage tells us which lines ran during a test. It does not tell us whether the test would notice a mistake on
those lines. Mutation testing helps answer that second question.

Stryker.NET makes small, temporary changes to the library. For example, it may change `>` to `>=`, remove a condition,
or replace `true` with `false`. It then runs the tests:

- A **killed** mutation made a test fail. The tests noticed the change.
- A **surviving** mutation did not make a test fail. A useful assertion may be missing.
- A **compile error** means the temporary change did not produce valid C# and could not be tested.

Stryker changes a temporary copy of the code. It does not edit your source files.

## Why CStructSharp uses it

CStructSharp has many rules for parsing, layout, byte order, limits, and reading and writing values. A test can run
one of these code paths without checking the important result. Mutation testing is useful here because it shows
whether the assertions notice a small logic error.

Normal CI checks code coverage. Mutation testing is slower, so the complete mutation run is scheduled separately
and is also used before a release.

## Memory analysis

The memory-analysis scope covers `src/CStructSharp/Memory/**/*.cs` in the library project:

```sh
node tools/quality/mutation-partitions.mjs --mode memory --output-directory artifacts/mutation/memory
```

This scope selects the `CStructSharp.Tests.Memory*` test classes and uses per-test coverage to select relevant
tests for each mutation. The mutation score threshold is 75%; compile errors do not count as detected behavior.
Review timeouts separately from assertion kills. Stream-cursor corpus tests run in the normal suite and the
parser mutation scope; they do not exercise the address-space APIs in this scope.
Run mutation testing independently of normal builds, tests, and benchmarks because it replaces test output assemblies.

## Run the complete check

Run these commands from the repository root:

```sh
dotnet tool restore
cd src/CStructSharp
dotnet stryker \
  --config-file ../../stryker-config.json \
  --solution ../../CStructSharp.NonWeb.sln \
  --project CStructSharp.csproj \
  --test-project ../../tests/CStructSharpTests/CStructSharpTests.csproj \
  --target-framework net10.0 \
  --configuration Release \
  --output ../../artifacts/mutation/permanent \
  --skip-version-check
cd ../..
node tools/quality/mutation-report.mjs --report-path artifacts/mutation/permanent/reports/mutation-report.json
```

The tool restore installs the version of Stryker listed in `.config/dotnet-tools.json`. Run Stryker from the source
project directory with explicit project paths. Its solution-directory mode discovers additional test projects,
even when the configuration lists only the core tests. Project mode preserves the complete configured core test
suite without rerunning generator/parity suites for every mutant; those suites retain their ordinary CI gates.
The last command checks that the report was made with the repository's approved settings, including core-test
project identity. See [Stryker operating modes](https://stryker-mutator.io/docs/stryker-net/operating-modes/).

This can take much longer than an ordinary test run. Progress is shown in the terminal. The JSON and HTML reports
are written below `artifacts/mutation/permanent/`. The `artifacts/` directory is ignored by Git.

## Complete parallel workflow

The scheduled/manual `mutation.yml` workflow runs the same permanent scope in 16 file-based partitions. It assigns
larger source files first to the smallest group, using file size only as an initial scheduling estimate. Every one
of the 71 configured files belongs to exactly one partition. No character ranges or changed-file filters are used.
Each partition runs the full permitted test suite with coverage-based test selection disabled. The memory scope
runs in an independent job with its existing per-test selection and 75% score floor.
The runner uses source-project context and records the working directory and command arguments in its evidence.

Inspect the matrix locally without running mutations:

```sh
node tools/quality/mutation-partitions.mjs --mode plan
```

Run one planned partition after restoring tools and projects, using a new output directory:

```sh
node tools/quality/mutation-partitions.mjs --mode run --partition p00 --output-directory artifacts/mutation/p00
```

One partition is development evidence, not a complete gate. The workflow downloads all partition artifacts into
separate directories and runs:

```sh
node tools/quality/mutation-partitions.mjs --mode aggregate --input-directory artifacts/mutation-input --output-directory artifacts/mutation-aggregate
```

Aggregation requires every partition, the same source revision and configuration, matching report hashes and
source text, complete mutation outcomes, and valid references to killing/covering tests. It gives test and mutant
identifiers a partition prefix so independently numbered reports cannot collide. The original full-scope validator
then enforces all 71 files, the raw 75% score floor and zero unexplained surviving/uncovered/runtime-error mutations. Missing reports
fail; compile errors remain compile errors. Raw JSON/HTML reports and Stryker trace logs are retained even when a
partition fails. A job timeout cannot be counted as a completed report.

A declaration with no authored executable body may offer no mutation opportunities. Exact reviewed cases are
recorded in `contracts/quality/mutation-non-mutable.json`, with their LF-normalized source hash, pinned Stryker
version and reason. `ReadAttempt<T>` is such a positional record. It stays in the 71-file scope and must appear
exactly once with its current source and an explicitly empty mutation array. The validator reports it as
**not applicable**, not as killed or successfully mutation-tested. A missing source/report, changed declaration,
tool-version change, or ignored/compiler-rejected mutations cannot use this qualification. Review changed code
before updating the policy; executable code still requires valid mutations. This declaration qualification does
not alter the raw score or classify any survivor.

Some generated mutations preserve the documented result for every supported input. These **equivalent mutations**
are different from a declaration with no mutation opportunities. Each reviewed case in
`contracts/quality/mutation-equivalents.json` identifies an exact source hash, Stryker version, mutation operator,
source span, replacement and explanation. For example, skipping an exact-name fast path can still return the same
name through the following case-insensitive search. Do not invent string-reference identity requirements to test
a method that promises only a name's spelling.

The validator reports individually qualified equivalent survivors separately. Their Stryker status remains
`Survived`: they stay in the raw score's denominator and are never counted as killed, timed out or ignored.
Every unexplained survivor still fails, as do uncovered and runtime-error mutations. A source/tool change requires
review again; a similar mutation at another location cannot borrow a proof. Ordinary test gaps, missing reports,
resource leaks and changed externally visible behavior are not equivalence. Keep the complete scope and raw 75%
minimum even when all remaining survivors have individual proofs.

The parser partition (`p00`) has a 240-minute execution limit within a 255-minute job budget. Every other permanent
partition and the independent memory job have a 180-minute execution limit within a 195-minute job budget.
The remaining allowance covers normal setup and diagnostic uploads after a step times out; it does not turn a timeout
into successful evidence.
Runner loss can still prevent uploads. Partitions run concurrently where runner capacity permits. The
`aggregation.json` artifact records each Stryker process's elapsed time and their sum. Compare those measurements
with the Actions job start/end times: total runner time also includes setup and uploads, while workflow elapsed
time includes queuing. Parallelism can shorten feedback without reducing runner cost. Revisit the partition count
or grouping when measured jobs approach their limit; keep the complete scope and score gates unchanged.

### Mutating only what changed

A full run over the allowlist takes hours. To check the files a branch or a series of commits touched, add Stryker's
`--since` flag with the commit the work started from; Stryker then mutates only the files that differ from that
commit (and runs every test as usual):

```sh
cd src/CStructSharp
timeout 90m dotnet stryker \
  --config-file ../../stryker-config.json \
  --solution ../../CStructSharp.NonWeb.sln \
  --project CStructSharp.csproj \
  --test-project ../../tests/CStructSharpTests/CStructSharpTests.csproj \
  --target-framework net10.0 \
  --configuration Release \
  --since:<start-commit> \
  --output ../../artifacts/mutation/<name> \
  --skip-version-check
cd ../..
```

`<start-commit>` must be a full SHA (or a branch or tag name): Stryker resolves it with LibGit2Sharp, which does not
expand an abbreviated SHA ("No branch or tag or commit found with given target"). Stryker still generates every
mutant of the project first and reports the ones outside the diff and the allowlist as "Removed by mutate filter";
the count on the "total mutants will be tested" line is what the run will take (the full 2025 run managed about
18 mutants a minute; a 1,029-mutant `--since` run in September 2026 did not finish in 90 minutes at the default
concurrency, so budget for 10 a minute and pass `--concurrency <cores>` on a machine with spare cores). `--mutate
<glob>` on the command line narrows the allowlist further when that count is too large for the time available -
the mutants of the files named this way, inside the diff, are the run.

The `timeout` keeps an interactive run bounded; a run that is cut off has no report, so scope it down (a smaller
`--since` range, or `--mutate` for a few files) rather than reading a partial one. The allowlist names the shared
compile-time sources by their folder (`**/CStructSharp.Core/...`): they are compiled into `src/CStructSharp` as
linked files, and Stryker matches a pattern against a file's full path or its path relative to the project.

## What the configuration does

`stryker-config.json`:

- mutates the main `src/CStructSharp` library;
- uses `tests/CStructSharpTests` to test each mutation;
- limits mutation to 71 files that contain the main parsing and binary-data logic, including the compile-time core in
  `src/CStructSharp.Core`, the `Generated` support types the source generator's output calls, and the awaitable and
  record-sequence forms (`CStructOperations.Async.cs`, `.AsyncWrite.cs`, `.Sequences.cs`, `Streams/AsyncStreamBuffer.cs`,
  `Reading/RecordParser.cs`, `Generated/RecordSequence.cs`, `Values/ReadAttempt.cs`);
- runs the complete test project instead of selecting tests from coverage data;
- writes progress, JSON, and HTML reports; and
- requires a mutation score of at least 75%.

The report validator also checks that there are no unexplained surviving mutations, uncovered mutations, or mutations
that fail at runtime. Only the exact reviewed equivalent survivors described above qualify. This is stricter than
checking the percentage alone.

## The layout parser

`Parsing/LayoutParser.cs`, the hand-written layout parser, is an ordinary member of the mutation list: its mutants must be
detected like any other semantic file's.

A compile error is not treated as proof that a test found a bug. If a future Stryker or source change produces valid
mutations for this file, the result must be reviewed and the tests should run against those mutations.

## Use a focused run while developing

When you change a risky file, start by mutating only that file. For example:

```sh
cd src/CStructSharp
dotnet stryker \
  --config-file ../../stryker-config.json \
  --solution ../../CStructSharp.NonWeb.sln \
  --project CStructSharp.csproj \
  --test-project ../../tests/CStructSharpTests/CStructSharpTests.csproj \
  --target-framework net10.0 \
  --configuration Release \
  --mutate CStructWriter.cs \
  --output ../../artifacts/mutation/writer \
  --skip-version-check
cd ../..
```

Do not pass a focused report to `mutation-report.mjs`; that validator expects the complete permanent scope.
After the focused run is useful and the normal tests pass, run the complete check before finishing a high-risk
change.

## What to do with a surviving mutation

1. Open the HTML report and read the changed expression.
2. Decide whether the mutation represents behavior users care about.
3. If it does, add a small test with an assertion that fails for that mutation.
4. Run the normal test by itself and make sure its purpose is clear.
5. Run mutation testing again and confirm that the mutation is killed.

Sometimes a mutation changes an implementation detail without changing useful behavior. Review that case rather
than adding a test that knows too much about private code.

Do not improve the score by removing difficult files, lowering the threshold, or counting compile errors as killed
mutations. The goal is to find weak tests, not to make the percentage look better.

## Performance implementation scope refresh

The reviewed scope replaces six obsolete partial-class filenames with their current enum, exception, bitfield,
layout-math, read/write-state and symbol-validation implementations. It also includes the new conditional-selection,
compiled scope/field/size metadata, debug-path and fixed-point helpers. This expands semantic coverage; the 75%
threshold, zero unexplained survivors, and zero uncovered/runtime-error requirements remain unchanged. Compiler-rejected mutations
remain tool limitations, not detected behavior.

The mutation run excludes exactly the API export-list reflection test through `test-case-filter`. Stryker changes
the assembly's public surface by injecting instrumentation; that test otherwise falsely kills unrelated mutants.
Environment-based instrumentation detection was insufficient in a full run and is not used. Normal test runs and
the managed API baseline comparison still enforce the complete public surface without exceptions. The mutation
validator pins this one-test filter; behavioral tests remain included. Review `killedBy` evidence when mutation
results look unexpectedly perfect.
