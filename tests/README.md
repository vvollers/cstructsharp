# Managed test projects

Run the commands from the repository root after `dotnet build CStructSharp.NonWeb.slnf -c Release`. Tests consume
reviewed inputs from `contracts/`; do not regenerate expectations to hide a regression. Reports and build outputs are
ignored.

| Project | What it checks | How to run it |
| --- | --- | --- |
| `CStructSharpTests/` | The runtime library: unit, regression, property, concurrency and fixture tests (every benchmark fixture against its recorded expectation, through the fixture tool), one folder per area (`Parsing`, `Compilation`, `Reading`, `Writing`, `Unions`, `Memory`, ...), with shared helpers in `Support/` and `TestStreams/`. Its tests are also the explorer's demonstrations. `Engine/` checks the compiled engine against reviewed golden outcomes (the manifests in `Engine/Golden/`): a differential harness, sweeps over inputs, limits, options, sources and destinations, and the repository corpora (parity layouts, benchmark fixtures, language contracts, well-known formats, the inspector catalog read from `apps/inspector/src/schema-catalog.ts`, and the fuzz corpus's replayed inputs). | `dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release --no-build` (net8.0 and net10.0); `node tools/quality/engine-golden.mjs record` records the golden manifests again, only for an intended change whose diff is reviewed and explained in the commit ([CONTRIBUTING.md](../CONTRIBUTING.md#engine-golden-outcomes)) |
| `CStructSharp.Generators.Tests/` | The source generator, run in memory: golden `Snapshots/*.g.cs` compared byte for byte, reader, writer and conditional parity with the runtime, the analyzer, options, mapped classes and record sequences. | `dotnet test tests/CStructSharp.Generators.Tests -c Release --no-build`; `UPDATE_SNAPSHOTS=1` rewrites the snapshots, and the diff is reviewed in the commit |
| `CStructSharp.Generators.Modern.Tests/` | The generator under the newest Roslyn: the compiler-compatibility tests, shared with the project above, against the current compiler while the shipped generator keeps its Roslyn 4.8 floor. | `dotnet test tests/CStructSharp.Generators.Modern.Tests -c Release --no-build` |
| `CStructSharp.Generated.Parity/` | Every repository layout fixture compiled by the generator (`Layouts.g.cs` and `layouts.json` from `node tools/quality/generate-parity-layouts.mjs`, `--check` in CI), compared with the runtime: values (strictly: member order, CLR types and array kinds, apart from the differences `ParityComparer` names), bytes, addresses (the runtime's `ResolveAddress` against the reader's placement and the generated `Offsets` constants), truncation failures (`TryParse` at every cut), the awaitable forms and record sequences. | `dotnet test tests/CStructSharp.Generated.Parity -c Release --no-build` |
| `CStructSharp.Fuzz/` | Bounded fuzz targets (the generated-differential target included) and the tracked replay corpus. | `dotnet run --project tests/CStructSharp.Fuzz -c Release -f net10.0 -- --target all --iterations 100`; `--help` lists the options; `node tools/quality/fuzz-corpus.mjs` validates the corpus |
| `CStructSharp.AotConsumer/` | A `PublishAot` application that runs typed reads, generated layouts and mapped classes. | `dotnet publish tests/CStructSharp.AotConsumer -c Release -r <rid>`, then run the published executable |
| `CStructSharp.PackageConsumer/` | The built NuGet package, installed without a project reference, with a generated layout and a `.cstruct` file. Kept out of the solution restore. | `node tools/packaging/test-package-consumer.mjs --package-directory <dir>` |
| `CStructSharp.Memory.PackageConsumer/` | The memory-analysis API through the built package only. Kept out of the solution restore. | `node tools/packaging/test-memory-package-consumer.mjs --package-directory <dir>` |

## Recorded expectations

Four kinds of test compare the current output with a reviewed file in the repository. Each has its own update
command. Update a file only for an intended change, and review and explain the diff in the same commit; never
update one to make a failing check pass.

| Mechanism | What it records | Checked by | Update command | When updating is allowed |
| --- | --- | --- | --- | --- |
| Engine golden outcomes | `tests/CStructSharpTests/Engine/Golden/*`: each engine operation's value or failure, stream positions, written bytes and debug records (readable text, or one SHA-256 per group for sweeps) | The managed tests in `CStructSharpTests/Engine/` | `node tools/quality/engine-golden.mjs record` (sets `CSTRUCTSHARP_ENGINE_GOLDEN_RECORD=1`; `--filter` limits the tests) | An intended read, write or update behavior change, or a new or renamed test; explain every changed entry in the commit ([CONTRIBUTING.md](../CONTRIBUTING.md#engine-golden-outcomes)) |
| Generator snapshots | `tests/CStructSharp.Generators.Tests/Snapshots/*.g.cs`: the generated source of the hand-picked type and feature tests | `CStructSharp.Generators.Tests` (`Snapshot.Match`) | `UPDATE_SNAPSHOTS=1 dotnet test tests/CStructSharp.Generators.Tests -c Release` | An intended change to the generated text; review the diff in the commit ([testing](../docs/project/testing.md)) |
| Managed API baseline | `contracts/api/managed/`: the public surface of `src/CStructSharp`, its hashes and the review history | `node tools/quality/managed-api-baseline.mjs compare` (CI) | `node tools/quality/managed-api-baseline.mjs update --kind additive\|breaking\|correction --rationale "..." --impact "..."` | An intended public API change, with a CHANGELOG entry and migration notes for a breaking change ([CONTRIBUTING.md](../CONTRIBUTING.md#public-net-api)) |
| Benchmark fixture expectations | `benchmarks/fixtures/cases/*.json`: each case's expected canonical JSON (SHA-256, length and, when small, the value) or expected exception type | `BenchmarkFixtureExpectationTests` and `CStructSharp.FixtureTool verify` | `dotnet run --project benchmarks/CStructSharp.FixtureTool -c Release -f net10.0 -- fill` (after `node benchmarks/fixtures/generate-fixtures.mjs` for new or changed cases) | A new or changed fixture case, or an intended behavior change that alters a recorded value ([benchmarks/fixtures](../benchmarks/fixtures/README.md)) |

## Opt-in tests

`tests/CStructSharpTests/default.runsettings` excludes the `OptIn` test category from ordinary runs, and
`opt-in.runsettings` runs only that category. The one opt-in test is the dissect corpus sweep
(`Dissect/DissectCorpusSweepTests`): it compiles every layout definition extracted from the dissect.cstruct ecosystem
and compares the outcome with a recorded status file. The corpus is Apache-2.0 source owned by the dissect project,
so it is not in the repository and the sweep runs only locally, never in CI:

```sh
node tools/quality/extract-dissect-corpus.mjs <ecosystem-dir> corpus.json
CSTRUCTSHARP_DISSECT_CORPUS=corpus.json dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release -f net10.0 --settings tests/CStructSharpTests/opt-in.runsettings
```

Without `CSTRUCTSHARP_DISSECT_CORPUS` the test is inconclusive. The status file defaults to `corpus-status.json`
beside the corpus (`CSTRUCTSHARP_DISSECT_CORPUS_STATUS` overrides it), and every run writes
`corpus-status.latest.json`; `CSTRUCTSHARP_DISSECT_CORPUS_RATCHET=1` also replaces the recorded status with the latest.
