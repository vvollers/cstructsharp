# Managed test-loop measurements

Measured on 2026-10-03 on the development Windows 11 machine: Ryzen 9 9950X, 32 logical CPUs,
.NET SDK 10.0.204 selected through `global.json`, .NET 8 and .NET 10, MSTest 4.4.1.
The source baseline was `d786682d`. Timing commands ran one at a time, without competing builds or benchmarks.
Parallel test hosts within a command are part of the experiment. These observations are not performance gates.

## Findings

The runtime project already uses method-level parallelism. The .NET SDK already runs its two target frameworks
concurrently. Splitting this project into assemblies would not create parallelism that is currently absent.
The long cursor tests are individual exhaustive loops; an assembly split would not divide those loops.

The generator driver project had no parallelization setting. Its 70 tests own independent Roslyn compilations and
consumer assemblies. Enabling bounded method parallelism reduced its elapsed time substantially without changing
the tests, their fixtures, snapshots or assertions.

The core profile's slowest cases were the two cursor cross-product sweeps, engine corpus comparisons and exhaustive
read/write sweeps. In the initial two-framework run, the small-input cursor test took 15.7 seconds on .NET 10 and
19.7 seconds on .NET 8. The block-boundary test took 10.1/8.9 seconds. These tests overlapped; their times must not
be added to estimate the command's duration. Allocation checks were retained in the fast profile.

## Observed wall times

All test-only rows use already-built Release binaries. Each row is one invocation unless a range is shown.
The maintained tool's total includes invocation startup, test hosts, TRX validation and summary reporting.

| Command or configuration | Test coverage | Wall time |
| --- | --- | ---: |
| Initial non-Web solution build | All solution projects | 20.63 s |
| Original core command, automatic workers | 4,245 cases on each framework | 23.72 s |
| Core command, eight workers per framework | Same 8,490 cases | 16.64 s |
| Core .NET 10, automatic workers | All 4,245 cases | 13.37 s |
| Core .NET 10, eight workers | All 4,245 cases | 13.86 s |
| Core .NET 10, omit only two cursor sweeps | 4,243 cases | 11.19 s |
| Generator driver, sequential | All 70 cases | 29.28 s |
| Generator driver, four workers | Same 70 cases | 12.38 s |
| Generator driver, eight workers | Same 70 cases | 10.01 s |
| Original compiled parity, both frameworks | 27 cases per framework, many fixture comparisons per case | 4.88 s |
| Newer-compiler compatibility | Both cases | 2.48 s |
| Maintained default development command, three runs | 3,056 core cases on .NET 10 | 5.22–5.35 s; median 5.30 s |
| Development command, all suites | 3,155 cases on .NET 10 | 11.28 s |
| Full command, two hosts/eight workers each | All 8,616 normal cases across supported frameworks/projects | 25.91 s |
| Default command including an unchanged incremental build | Development runtime profile | 6.63 s: 1.26 s setup/build + 5.38 s tests |
| Default command after touching a test source file | Development runtime profile, test project recompiled | 12.46 s: 6.98 s setup/build + 5.48 s tests |

The four original test commands together took 60.37 seconds, excluding the solution build. The full command's
25.91-second observation covers the same test populations. This is a large coarse improvement, not a claim about
small timing differences. Warm incremental build time is not the cost of rebuilding changed library or generator
sources; the tool reports actual build and test time separately on every invocation.

Eight workers improved the two-framework run but did not improve the single-framework full run. More workers
are not universally faster. The maintained command caps workers at eight and divides available logical CPUs
between its host slots; machines with fewer than eight logical CPUs default to one host. The generator project
itself uses four workers for ordinary `dotnet test`; the tool can supply its bounded override.

## Development coverage and its limits

The default command selects .NET 10 and omits 1,189 `Extended` test cases:

| Extended selection | Cases in this revision | Coverage deferred to full runs |
| --- | ---: | --- |
| `EngineCorpusTests` | 633 | Full repository corpora across source forms, paths, expected outcomes and large benchmark fixtures |
| `EngineSweepTests` | 352 | Read truncations, budgets, limits, options, variables, record sequences and destination combinations |
| `EngineWriteSweepTests` | 195 | Supplied values, span capacities, path writes/updates and update-option combinations |
| `CursorDifferentialTests.Cursors_MatchReadBudgetStream_OverSmallInputs` | 1 | Every small input/script/source/budget combination |
| `CursorDifferentialTests.Cursors_MatchReadBudgetStream_AtBlockBoundaries` | 1 | Cursor/stream comparisons around 64 KiB block boundaries |
| `ExpressionDifferentialTests.CorpusExpressions_EvaluateLikeTheDictionaryModel` | 7 | Every corpus expression against the dictionary model under many contexts |

All method bodies, sweep inputs and assertions are unchanged. The other 3,056 core cases remain, including focused
engine differentials, cursor regressions, expressions, allocations, ownership, async, stream, language and boundary
tests. The generated parity project still traverses its complete existing fixture selection. It is included by
`--suite generator`, `--suite parity`, `--suite all`, and unfiltered `--full`, but not the default runtime-only command.

A development pass cannot establish the correctness of omitted cross-products or .NET 8 behavior. Run the relevant
Extended class when changing its subsystem, and unfiltered `--full` before handoff. CI and coverage still use ordinary
full commands, so exhaustive checks run on changes, not only at release. The existing environment-dependent `OptIn`
external corpus remains separate from both normal profiles.

## Reproduction

From the repository root, using the SDK and Node version pinned by the repository:

```powershell
dotnet build CStructSharp.NonWeb.slnf -c Release
node tools/quality/test-managed.mjs --no-build
node tools/quality/test-managed.mjs --no-build --suite all
node tools/quality/test-managed.mjs --no-build --full
node tools/quality/test-managed.mjs

# Native development profile, without the wrapper:
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release -f net10.0 --no-build --settings tests/development.runsettings

# Full core comparison with automatic and bounded workers:
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release --no-build --logger trx --results-directory artifacts/test-loop/reproduce-auto
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release --no-build --logger trx --results-directory artifacts/test-loop/reproduce-eight -- MSTest.Parallelize.Workers=8

# Reproduce the generator's sequential control despite its new default parallelism:
dotnet test tests/CStructSharp.Generators.Tests/CStructSharp.Generators.Tests.csproj -c Release --no-build -- RunConfiguration.DisableParallelization=true
dotnet test tests/CStructSharp.Generators.Tests/CStructSharp.Generators.Tests.csproj -c Release --no-build -- MSTest.Parallelize.Workers=4 MSTest.Parallelize.Scope=MethodLevel
dotnet test tests/CStructSharp.Generators.Tests/CStructSharp.Generators.Tests.csproj -c Release --no-build -- MSTest.Parallelize.Workers=8 MSTest.Parallelize.Scope=MethodLevel
```

Each maintained invocation writes a new directory under `artifacts/test-results/development/`, containing its
complete command plan, build logs, one log/TRX per host, counts, slowest test observations and wall times.
Initial experimental logs, stopwatch JSON and per-case TRX files are under `artifacts/test-loop/`:
`core-baseline`, `core-workers8`, `core-net10`, `core-net10-workers8`, `core-dev-prototype`,
`generator-baseline`, `generator-workers4`, `generator-workers8`, `parity-baseline`, and `modern-baseline`.
`implemented-*.log` records the maintained command and its exact result-directory path.
Raw outputs are ignored locally; the commands and authored report are versioned.

## Sources

- Microsoft documents [MSTest parallelization settings](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-configure)
  and [execution controls, including DoNotParallelize](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests-controlling-execution).
- The [.NET test CLI documentation](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-test) describes test
  selection and the runtime's test-runner integration. Existing coverage collection remains separate because
  Coverlet modifies output assemblies during instrumentation.
