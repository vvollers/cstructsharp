# Managed test projects

Run the commands from the repository root after `dotnet build CStructSharp.NonWeb.slnf -c Release`. Tests consume
reviewed inputs from `contracts/`; do not regenerate expectations to hide a regression. Reports and build outputs are
ignored.

| Project | What it checks | How to run it |
| --- | --- | --- |
| `CStructSharpTests/` | The runtime library: unit, regression, property, concurrency and fixture tests, one folder per area (`Parsing`, `Compilation`, `Reading`, `Writing`, `Unions`, `Memory`, ...), with shared helpers in `Support/` and `TestStreams/`. Its tests are also the explorer's demonstrations. `Engine/` compares the interpreter with the compiled general engine: a differential harness, sweeps over inputs, limits, options, sources and destinations, and the repository corpora (parity layouts, benchmark fixtures, language contracts, well-known formats, the inspector catalog read from `apps/inspector/src/schema-catalog.ts`, and the fuzz corpus's replayed inputs). | `dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release --no-build` (net8.0 and net10.0) |
| `CStructSharp.Generators.Tests/` | The source generator, run in memory: golden `Snapshots/*.g.cs` compared byte for byte, reader, writer and conditional parity with the runtime, the analyzer, options, mapped classes and record sequences. | `dotnet test tests/CStructSharp.Generators.Tests -c Release --no-build`; `UPDATE_SNAPSHOTS=1` rewrites the snapshots, and the diff is reviewed in the commit |
| `CStructSharp.Generators.Modern.Tests/` | The generator under the newest Roslyn: the compiler-compatibility tests, shared with the project above, against the current compiler while the shipped generator keeps its Roslyn 4.8 floor. | `dotnet test tests/CStructSharp.Generators.Modern.Tests -c Release --no-build` |
| `CStructSharp.Generated.Parity/` | Every repository layout fixture compiled by the generator (`Layouts.g.cs` and `layouts.json` from `node tools/quality/generate-parity-layouts.mjs`, `--check` in CI), compared with the runtime: values, bytes, addresses, truncation failures (`TryParse` at every cut), the awaitable forms and record sequences. | `dotnet test tests/CStructSharp.Generated.Parity -c Release --no-build` |
| `CStructSharp.Fuzz/` | Bounded fuzz targets (the generated-differential target included) and the tracked replay corpus. | `dotnet run --project tests/CStructSharp.Fuzz -c Release -f net10.0 -- --target all --iterations 100`; `--help` lists the options; `node tools/quality/fuzz-corpus.mjs` validates the corpus |
| `CStructSharp.AotConsumer/` | A `PublishAot` application that runs typed reads, generated layouts and mapped classes. | `dotnet publish tests/CStructSharp.AotConsumer -c Release -r <rid>`, then run the published executable |
| `CStructSharp.PackageConsumer/` | The built NuGet package, installed without a project reference, with a generated layout and a `.cstruct` file. Kept out of the solution restore. | `node tools/packaging/test-package-consumer.mjs --package-directory <dir>` |
| `CStructSharp.Memory.PackageConsumer/` | The memory-analysis API through the built package only. Kept out of the solution restore. | `node tools/packaging/test-memory-package-consumer.mjs --package-directory <dir>` |

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
