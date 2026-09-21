---
title: Testing and quality checks
description: Choose focused tests first, then run the broader checks required by the kind of change you made.
---

# Testing and quality checks

The repository uses several test layers because a binary-format bug can affect values, byte positions, failure
behavior, package compatibility, or performance. You do not need to run every expensive check after every edit.
Start narrow, then widen according to the changed behavior.

## Run a focused test on both frameworks

After building the test project, run the smallest relevant class or method separately for .NET 8 and .NET 10:

```sh
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release -f net8.0 --no-build --filter "FullyQualifiedName~ManualLanguageFixtureTests"
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release -f net10.0 --no-build --filter "FullyQualifiedName~ManualLanguageFixtureTests"
```

Replace the sample filter with the test that covers your change. Running both targets catches differences hidden by
one runtime. A successful result reports no failed tests and exit code 0 for each command.

Then run the full managed suite:

```sh
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release
```

This builds as needed and runs unit, integration, regression, property, stream-adapter, concurrency, limit, and
compatibility tests on both frameworks.

## Generator snapshots and parity

The source generator has two test projects of its own:

```sh
dotnet test tests/CStructSharp.Generators.Tests/CStructSharp.Generators.Tests.csproj -c Release
dotnet test tests/CStructSharp.Generated.Parity/CStructSharp.Generated.Parity.csproj -c Release
```

The first runs the generator in memory over small sources: the generated file for each fixture is compared byte
for byte with the golden file in `Snapshots/*.g.cs`, and behavior tests compile the output and compare it with
the runtime (`ReaderParityTests`, `WriterParityTests`, `ConditionalParityTests`, the analyzer's diagnostics). When
a change to the emitter alters the generated text on purpose, rewrite the snapshots and review the diff in the
commit:

```sh
UPDATE_SNAPSHOTS=1 dotnet test tests/CStructSharp.Generators.Tests/CStructSharp.Generators.Tests.csproj -c Release
```

A snapshot is never rewritten to make a failing test pass; the diff is the review.

The second project generates every layout fixture the runtime is tested with into one assembly. Its
`Layouts.g.cs` and `layouts.json` come from the fixture sources; regenerate and check them with:

```sh
node tools/quality/generate-parity-layouts.mjs
node tools/quality/generate-parity-layouts.mjs --check
```

For each layout the tests compare the generated `Parse` with `Layout.Parse` member by member, round-trip the
value through both writers, and cut the bytes at every length expecting the same exception type and text from
both paths (the runtime is the oracle); at every cut `TryParse` must return `false` exactly when `Parse` throws,
with the same message. The awaitable forms are compared the same way over a hidden-buffer stream (`ParseAsync`
and `WriteAsync` on both sides, unwrapped by a reflection `Await` helper), and the fixture's bytes as one record,
three times over, and with the third record cut short go through `ParseMany` and the generated `Records` - the
same records member by member or the same failure text. The runtime suite pins the stream forms against their
awaitable twins over three stream kinds (`AsyncReadTests`, `AsyncWriteTests`) and every manual fixture through
`ReadValueAsync`/`WriteAsync` (`ManualLanguageFixtureTests`). A new fixture added to the runtime tests reaches the parity project
through the generator tool; `--check` in CI fails when the generated files are stale.

## Check repository reference data

Some behavior is also recorded in JSON/text files so tests, docs, and release automation agree. Run the checks
related to your change:

```sh
node tools/quality/feature-operation-matrix.mjs
node tools/documentation/validate-canonical-reference.mjs
node tools/quality/compiler-fixture.mjs validate
node tools/quality/fuzz-corpus.mjs
node tools/quality/managed-api-baseline.mjs compare
```

These commands check, respectively, language operations, the Portable data tables, compiler
observations, replayable fuzz inputs, and public API signatures. Each prints a concise pass summary or exits nonzero
with the mismatched file/entry.

A language change normally updates parser tests, operation tests, manual fixtures, the feature matrix, and prose
together. A public API change needs an explicit compatibility decision; do not regenerate the baseline merely to
make the comparison pass.

## Coverage and mutation testing

*Coverage* records which lines and branches the tests execute. CI requires at least 78% aggregate line coverage and
80% aggregate branch coverage. It also rejects every critical or high-risk runtime file. A critical file has
less than 60% line coverage, or less than 50% branch coverage when it has at least ten branches. A high-risk
file has less than 75% line coverage, or less than 65% branch coverage with at least ten branches.

Collect the same whole-library measurement used by CI after building `CStructSharp.NonWeb.sln` in Release:

```sh
node tools/quality/collect-library-coverage.mjs
node tools/quality/coverage-risk.mjs --coverage-path artifacts/test-results/library-coverage/coverage.cobertura.xml --collection-manifest artifacts/test-results/library-coverage/collection.json --population-policy contracts/quality/coverage-population.json --output-directory artifacts/test-results/library-risk --minimum-line-percent 78 --minimum-branch-percent 80 --maximum-high-risk-files 0 --maximum-critical-risk-files 0
```

The collector runs the core, compiled-parity and generator-consumer suites on .NET 10. Coverlet merges its
JSON measurements sequentially before producing one Cobertura report, so distinct branch outcomes keep their
identity. The measured assembly is `CStructSharp`, including its shared compiler sources; this is not a coverage
claim for the separate generator assembly. Missing suite reports or failed tests stop collection. CI retains the
intermediate reports, TRX results, collection hashes and final risk report.

Three generator attributes are configuration declarations, not runtime algorithms. Roslyn reads their arguments
without executing their constructors or property accessors. `contracts/quality/coverage-population.json` lists
these exact files, reviewed source hashes (with LF line endings), reasons and required generator tests. Changed sources or missing test
evidence fail qualification. Review any new executable behavior before updating a hash; move runtime behavior
into the runtime population. Do not expand this list to hide ordinary uncovered code.

Qualified declarations remain in aggregate totals and retain their actual measured hits and risk bands in the
report. Only the runtime critical/high file counts exclude them. Passing generator tests is compile-time evidence,
not invented runtime coverage. README coverage badges use the merged measurement; their test count remains the
core suite once on .NET 10.

*Mutation testing* makes small changes to production code, such as reversing a condition, and checks whether tests
fail. A surviving mutation can reveal an assertion gap even when line coverage is high. The permanent score floor is
75%.

`coverage-risk.mjs` applies the population and risk policy to the collector's merged report.
`mutation-report.mjs` checks the permanent-scope Stryker report. The exact pinned mutation command is in
the repository root `MUTATION_TESTING.md`.

Exact reviewed declarations with no mutation opportunities are reported as not applicable, not detected behavior.
They remain in the configured scope and must have a present report with identical source and no mutants.
`contracts/quality/mutation-non-mutable.json` pins their source hashes, Stryker version and reasons. Missing reports
and compiler-rejected mutations do not qualify; executable code retains the score and survivor requirements.

The layout parser has its own oracle: `ParserDifferentialTests` parses every fixture, contract, demo, and
documentation layout - and thousands of deterministic mutations of them - through both `LayoutParser` and the
frozen Pidgin reference grammar kept under `tests/CStructSharpTests/Reference/`, requiring identical accept/reject
decisions and identical syntax trees except for explicitly tested grammar corrections (comment stars and empty
alignment arguments). The Portable contract defines the intended behavior for these cases. Extend the language in `LayoutParser` and, for the differential test to
keep its meaning, in the reference grammar too.

Do not lower thresholds, add broad exclusions, or classify a real survivor away to make a run green.

## Property tests and fuzzing

A property test checks a rule across many generated values rather than one example. Round-trip properties distinguish
meaningful value equality from identical bytes: padding can be normalized, pointers are not relocated, and a
`UnionValue` explicitly retains raw storage.

The managed fuzz harness feeds bounded generated/corpus inputs to six targets. It records a stable seed and minimizes
failures so they can be replayed. Add a minimized failure as a named regression; a random failure that cannot be
reproduced is not enough. The `generated-differential` target reads every input with the `[CStructLayout]`-generated
readers of the harness's layouts and with the runtime, and writes both values back: the two paths must fail the same
way (type and message) or produce the same bytes, so the target has no documented failures - any disagreement fails
the run.

The dissect corpus sweep (`DissectCorpusSweepTests.Corpus_NeverRegresses`) compiles every definition extracted
from the dissect ecosystem and is in the `OptIn` test category, which `tests/CStructSharpTests/default.runsettings`
excludes from ordinary runs. To run it, extract a corpus with `node tools/quality/extract-dissect-corpus.mjs
<ecosystem-dir> corpus.json`, then:

```sh
CSTRUCTSHARP_DISSECT_CORPUS=corpus.json dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release -f net10.0 --settings tests/CStructSharpTests/opt-in.runsettings
```

## Compiler-differential fixtures

Small Clang and GCC fixtures record how selected C11 objects were laid out under specific recorded environments.
They help explain where Portable deliberately agrees or differs. They do not add a selectable compiler/ABI mode to
CStructSharp.

## Performance, packages, and release checks

BenchmarkDotNet scenarios compare timing and allocation for controlled before/after cases. Package checks inspect
metadata, framework assets, symbols, Source Link, installed consumer behavior, dependency audit results, and raw or
compressed sizes.

Performance work follows a recorded-baseline discipline. `benchmarks/fixtures/` is a seeded corpus shared by the
.NET, Node, and browser harnesses; `CStructSharp.FixtureTool fill` records the expected result of every fixture
from the managed library and `verify` re-checks it, so a performance change that alters any parsed value fails
before it is measured. `contracts/performance/non-web-rc1.json` is the enforced release gate (Gate job, medians
and allocations with generous multipliers); `non-web-rc2.json` and `web-benchmark-rc1.json` are the wider
baselines used by the soft drift report (`tools/quality/compare-benchmark-baseline.mjs`,
`benchmarks/js/bench/check.mjs`, and the non-failing `benchmark-drift` workflow). Re-record a baseline only for an
accepted change, with the `--merge` mode of `tools/quality/record-benchmark-baseline.mjs` or
`benchmarks/js/bench/record.mjs`, and record what moved in the contract's `updates` note. The complete procedure (jobs,
runtimes, profiling, browser harness, AOT variant) is in `benchmarks/README.md` and `benchmarks/js/README.md` in the repository.

The browser adapter's source can be compared with its recorded wire format without compiling Web/WASM. Run relevant
frontend and browser checks locally when changing that application. Release automation builds the production
WASM explorer and runs frontend unit tests, explorer end-to-end tests, and the extracted browser starter checks.
It also runs the starter and recipe programs against the candidate NuGet package.

The npm package CI and release workflow test the installed tarball in Node.js 22.14 and 24.0 on Windows,
Linux, and macOS, plus TypeScript and browser consumers. The browser checks cover Vite development and production,
nested deployment paths, server rendering, and static assets. See [npm package checks](release-process.md#build-and-test-npm-locally)
for the local commands.

## Documentation

```sh
node tools/documentation/validate-documentation.mjs
```

Run this from the repository root after changing public behavior or the site. It builds only the core net10 assembly,
executes documentation examples and language fixtures, generates API metadata, builds DocFX with warnings as errors,
and validates Markdown, spelling, links, search, browser behavior, accessibility, and artifact size.

When a check fails, keep its first meaningful error and use [Debugging contributor failures](debugging.md) rather
than rerunning the entire suite without narrowing the cause.
