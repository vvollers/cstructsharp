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

## Engine golden outcomes

The library reads, writes, and updates most layouts with its compiled engine (see
[Architecture](architecture.md#what-happens-during-an-operation)). The tests in `tests/CStructSharpTests/Engine/`
check the engine by comparing each operation's *outcome* with a *golden outcome*: a reviewed copy of the outcome,
stored in the repository. An outcome is everything a caller can observe, written as text: the value or the failure
(exception type, message, path, and offset), the final stream position, the written bytes, the debug records, and
how many operations reached the engine instead of a faster path in front of it.

For example, `EngineDifferentialTests.Bitfields_ReadAndUpdateIdentically` parses this layout from the three bytes
`21 34 12`:

```c
struct rec {
    uint8 low : 4;   /* the low four bits of byte 0 */
    uint8 high : 4;  /* the high four bits of byte 0 */
    uint16 rest;     /* bytes 1-2, little-endian */
};
```

Byte `21` holds `low = 1` and `high = 2`, and bytes `34 12` hold `rest = 0x1234 = 4660`. The golden outcome of the
parse from a span is recorded in `Engine/Golden/EngineDifferentialTests.txt`:

```text
@test Bitfields_ReadAndUpdateIdentically
@case Parse rec (Span) (Fastest)
  result = StructValue {3}
  result.low = Int32 1
  result.high = Int32 2
  result.rest = UInt16 4660
  decisions = 1
```

`decisions = 1` means one operation reached the engine. If a change made `high` read as `3`, or moved the stream
position, or sent the parse down a different path, the test would fail with a line-by-line difference.

Each test class has one manifest file in `Engine/Golden/`, with one `@test` section per test. A small test stores its
outcomes as readable `@case` blocks. A sweep over many layouts or corpus inputs stores one SHA-256 hash per group of
outcomes (`@hash`), which keeps the files small but does not show which outcome changed. A test fails when an
outcome differs from its golden one, when it has no golden outcome, or when a golden outcome is no longer produced.

The same outcomes are checked on .NET 8 and .NET 10. Record the manifests again only for an intended, explained
behavior change; [CONTRIBUTING.md](https://github.com/vvollers/cstructsharp/blob/main/CONTRIBUTING.md#engine-golden-outcomes)
gives the `node tools/quality/engine-golden.mjs record` procedure and how to find what changed inside a hashed group.

## Generator snapshots and parity

The source generator has three test projects of its own:

```sh
dotnet test tests/CStructSharp.Generators.Tests/CStructSharp.Generators.Tests.csproj -c Release
dotnet test tests/CStructSharp.Generated.Parity/CStructSharp.Generated.Parity.csproj -c Release
dotnet test tests/CStructSharp.Generators.Modern.Tests/CStructSharp.Generators.Modern.Tests.csproj -c Release
```

The first runs the generator in memory over small sources: every language-contract fixture must generate without a
diagnostic and compile, the generated file for a hand-picked set of sources (the emitted types and one source per
feature) is compared byte for byte with the golden file in `Snapshots/*.g.cs`, and behavior tests compile the output
and compare it with the runtime (`ReaderParityTests`, `WriterParityTests`, `ConditionalParityTests`, the analyzer's
diagnostics). When a change to the emitter alters the generated text on purpose, rewrite the snapshots and review the
diff in the commit:

```sh
UPDATE_SNAPSHOTS=1 dotnet test tests/CStructSharp.Generators.Tests/CStructSharp.Generators.Tests.csproj -c Release
```

A snapshot is never rewritten to make a failing test pass; the diff is the review.

The first project hosts the generator under Roslyn 4.8 with C# 12 consumers. The third runs shared compiler
compatibility tests under Roslyn 5.9 with both C# 12 and C# 14 consumers. These tests compile generated readers
and writers, check signed-byte values, and verify exact round trips for fixed and counted arrays, including an
empty counted array. The generator itself still references Roslyn 4.8: a newer test host does not raise its
minimum compiler requirement. Both host checks run in CI and release verification. Compiler-host coverage is
separate from testing the runtime library on .NET 8 and .NET 10.

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

*Coverage* records which lines and branches the tests execute. CI requires a minimum aggregate line and branch
coverage (the `--minimum-line-percent` and `--minimum-branch-percent` arguments in `.github/workflows/ci.yml`). It
also rejects every critical or high-risk runtime file. A critical file has
less than 60% line coverage, or less than 50% branch coverage when it has at least ten branches. A high-risk
file has less than 75% line coverage, or less than 65% branch coverage with at least ten branches.

Collect the same whole-library measurement used by CI after building `CStructSharp.NonWeb.slnf` in Release:

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
`thresholds.break` in `stryker-config.json`.

`coverage-risk.mjs` applies the population and risk policy to the collector's merged report.
`mutation-report.mjs` checks the permanent-scope Stryker report. The exact pinned mutation command is in
[Mutation testing](mutation-testing.md).

Exact reviewed declarations with no mutation opportunities are reported as not applicable, not detected behavior.
They remain in the configured scope and must have a present report with identical source and no mutants.
`contracts/quality/mutation-non-mutable.json` pins their source hashes, Stryker version and reasons. Missing reports
and compiler-rejected mutations do not qualify; executable code retains the score and survivor requirements.
Individually proven equivalent survivors are recorded in `contracts/quality/mutation-equivalents.json`, pinned to
their exact source, tool version, operator, location and replacement. They remain survivors in the raw 75% score
calculation, not detected behavior. Every unexplained survivor, uncovered mutation and runtime error still fails.
See [Mutation testing](mutation-testing.md) for the review criteria and the distinction from ordinary missing assertions.
Mutation runners use source-project context so Stryker honors the configured core test project. Reports containing
tests from unintended projects fail validation. Generator/parity suites remain independently required in normal CI.

The layout parser has its own corpus checks: `ParserCorpusTests` parses every fixture, contract, demo, and
documentation layout - and thousands of deterministic mutations of them - and requires each to parse or fail with a
syntax diagnostic, never another exception. Hand-picked token-convention spellings keep their exact syntax trees,
recorded in `tests/CStructSharpTests/ParserTokenConventions.json`. The Portable contract defines the intended
language; a deliberate grammar change updates that file together with the contract.

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

Each target's report ends with a replay digest: a SHA-256 over every input and its outcome. The outcome is the
canonical text of the value a success produced, or the failure's type, message, path and offset.
`ManagedFuzzTests` pins the digests, so a change to a value or a diagnostic fails that test even when the success and
failure counts stay the same. After a reviewed, deliberate behavior change, run
`dotnet run --project tests/CStructSharp.Fuzz -c Release -f net10.0 -- --target all` and copy the printed digests
into the test.

The dissect corpus sweep (`DissectCorpusSweepTests.Corpus_NeverRegresses`) compiles every definition extracted
from the dissect ecosystem and is in the `OptIn` test category, which `tests/CStructSharpTests/default.runsettings`
excludes from ordinary runs. To run it, extract a corpus with `node tools/quality/extract-dissect-corpus.mjs
<ecosystem-dir> corpus.json`, then:

```sh
CSTRUCTSHARP_DISSECT_CORPUS=corpus.json dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release -f net10.0 --settings tests/CStructSharpTests/opt-in.runsettings
```

## Compiler comparison fixtures

A C compiler places struct members by the rules of its *ABI* (application binary interface): the size and alignment
of each type, where padding goes, and how bitfields share storage. Different compilers and targets follow different
ABIs. CStructSharp does not ask the compiler; its Portable rules, together with the constructor's placement options,
decide every offset. The compiler comparison fixtures record what real compilers do, so the documentation can say
exactly where the Portable rules agree with them and where they differ.

The evidence has three parts:

- `tools/compiler-fixtures/portable-host-facts.c` declares a set of C11 objects and prints their sizes, alignments,
  and byte images. The objects have static storage, so their padding bytes are zero and each image is repeatable.
- `contracts/quality/compiler-fixtures/baselines/` holds one observation file per compiler and target: GCC and Clang
  on Linux x64, GCC with `-m32` on Linux x86, Clang on macOS arm64, and MSVC and clang-cl on Windows x64. Each file
  records the compiler's identity, flags, host, and the SHA-256 hash of the fixture source it ran.
- `contracts/quality/compiler-fixtures/shapes.json` pairs each C shape with the equivalent Portable layout and its
  values, and *claims* whether the library reproduces the compiler byte for byte with SysV placement (`sysv`), SysV
  placement with four-byte pointers (`sysvX86`, compared with 32-bit x86), and MSVC placement (`msvc`).

For example, the shape `u64-after-u8` is `struct { uint8_t a; uint64_t b; }` with `a = 0x11` and
`b = 0x8877665544332211`:

| Baseline | Size | Byte image |
| --- | ---: | --- |
| Linux x64 GCC, Windows x64 MSVC | 16 | `11 00 00 00 00 00 00 00 11 22 33 44 55 66 77 88` |
| Linux x86 GCC (`-m32`) | 12 | `11 00 00 00 11 22 33 44 55 66 77 88` |

On x86-64 and arm64, `b` is aligned to eight bytes, so seven padding bytes follow `a`. The 32-bit x86 System V ABI
aligns an eight-byte integer inside a struct to only four bytes, so `b` starts at offset 4. Aligned Portable
placement always aligns `uint64` to eight bytes, so the shape claims `sysv` and `msvc` but not `sysvX86`.

`CompilerDifferentialFixtureTests` checks every claim against every baseline of the matching ABI family: in the named
mode, the library's size, alignment, and bytes must equal the compiler's, and the compiler's bytes must parse back to
the shape's values. The fixture's observations are evidence for the documentation, not a promise that CStructSharp
follows a host compiler's ABI. `node tools/quality/compiler-fixture.mjs table` renders the comparison table in
[Differences from C](../language/differences-from-c.md) from the same files, and CI checks that the table is current.

A baseline made from an older fixture source is stale, and `node tools/quality/compiler-fixture.mjs validate` rejects
it. When the C file or the shapes change, the baselines are recorded again by the hand-started `compiler-fixtures`
workflow; [CONTRIBUTING.md](https://github.com/vvollers/cstructsharp/blob/main/CONTRIBUTING.md#compiler-comparison-fixture)
lists the steps.

## Performance, packages, and release checks

BenchmarkDotNet scenarios compare timing and allocation for controlled before/after cases. Package checks inspect
metadata, framework assets, symbols, Source Link, installed consumer behavior, dependency audit results, and raw or
compressed sizes.

Performance work follows a recorded-baseline discipline. `benchmarks/fixtures/` is a seeded corpus shared by the
.NET, Node, and browser harnesses; `CStructSharp.FixtureTool fill` records the expected result of every fixture
from the managed library and `verify` re-checks it, so a performance change that alters any parsed value fails
before it is measured. `contracts/performance/release-gate.json` is the release gate: the maintainer runs the `Gate`
benchmark category with the Gate job before a release, and `tools/quality/non-web-release-budgets.mjs` compares the
medians and allocations with generous multipliers. No workflow runs it with real data, because shared CI runners are
too noisy for a timing budget; for the same reason no workflow compares timings with a recorded baseline. A
performance change is judged by a before/after comparison on one machine (`tools/quality/quick-perf-check.mjs`).
`web-size-budget.json` holds the size and startup limits of the browser build. The complete procedure (jobs,
runtimes, profiling, browser harness, AOT variant) is in `benchmarks/README.md` and `benchmarks/js/README.md` in the repository.

The browser adapter's source can be compared with its recorded wire format without compiling Web/WASM. Run relevant
frontend and browser checks locally when changing that application. The web workflow builds the production
WASM explorer and runs frontend unit tests, explorer end-to-end tests, and the npm package's browser tests
(`packages/cstructsharp/tests/browser`: the extracted starter pages and the bridge contract through the public API).
The managed workflow runs the starter and recipe programs against a freshly packed NuGet package. A release reruns
both workflows on its source commit, then checks the versioned artifacts themselves.

The web workflow tests the installed tarball at the edges of the supported Node range: Node.js 22.14 (the
package's minimum) on Windows, Linux, and macOS, and Node.js 26.5 on Linux, plus TypeScript and browser consumers. The browser checks cover Vite development and production,
nested deployment paths, server rendering, and static assets. See [npm package checks](release-process.md#build-and-test-npm-locally)
for the local commands.

## Continuous integration

Three workflows run on pull requests and on pushes to `main`, each only when a file in its area changes:

| Workflow | Area | Runs |
| --- | --- | --- |
| `ci.yml` | Library, generator, tests, benchmarks, tools, contracts | Build, format, both-framework tests, coverage, contracts, packages, Native AOT, Windows/macOS tests |
| `web.yml` | WASM bridge, npm package, explorer, inspector | Lint and unit tests without .NET; one WASM build shared by the package, explorer, inspector and onboarding browser tests; the npm package on the minimum Node on every platform and the newest Node on Linux |
| `docs.yml` | Documentation sources, the API reference's source, contracts | The complete documentation gate |

Each also runs weekly on a schedule and on demand, so an effect that a path filter did not anticipate still shows up.
The release workflow always runs all three. Every workflow reads its Node version from `.node-version`.

## Documentation

```sh
node tools/documentation/validate-documentation.mjs
```

Run this from the repository root after changing public behavior or the site. It builds only the core net10 assembly,
executes documentation examples and language fixtures, generates API metadata, builds DocFX with warnings as errors,
and validates Markdown, spelling, links, search, browser behavior, accessibility, and artifact size.

When a check fails, keep its first meaningful error and use [Debugging contributor failures](debugging.md) rather
than rerunning the entire suite without narrowing the cause.
