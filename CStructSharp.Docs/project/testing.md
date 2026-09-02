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

```powershell
dotnet test .\CStructSharpTests\CStructSharpTests.csproj -c Release -f net8.0 --no-build --filter "FullyQualifiedName~ManualLanguageFixtureTests"
dotnet test .\CStructSharpTests\CStructSharpTests.csproj -c Release -f net10.0 --no-build --filter "FullyQualifiedName~ManualLanguageFixtureTests"
```

Replace the sample filter with the test that covers your change. Running both targets catches differences hidden by
one runtime. A successful result reports no failed tests and exit code 0 for each command.

Then run the full managed suite:

```powershell
dotnet test .\CStructSharpTests\CStructSharpTests.csproj -c Release
```

This builds as needed and runs unit, integration, regression, property, stream-adapter, concurrency, limit, and
compatibility tests on both frameworks.

## Check repository reference data

Some behavior is also recorded in JSON/text files so tests, docs, and release automation agree. Run the checks
related to your change:

```powershell
.\tools\Validate-RegressionInventory.ps1
.\tools\Validate-FeatureOperationMatrix.ps1
.\tools\Validate-CanonicalReference.ps1
.\tools\Validate-CompilerFixture.ps1
.\tools\Validate-FuzzCorpus.ps1
.\tools\Compare-ManagedApiBaseline.ps1
```

These commands check, respectively, named regressions, language operations, the Portable data tables, compiler
observations, replayable fuzz inputs, and public API signatures. Each prints a concise pass summary or exits nonzero
with the mismatched file/entry.

A language change normally updates parser tests, operation tests, manual fixtures, the feature matrix, and prose
together. A public API change needs an explicit compatibility decision; do not regenerate the baseline merely to
make the comparison pass.

## Coverage and mutation testing

*Coverage* records which lines and branches the tests execute. CI requires at least 78% aggregate line coverage and
80% aggregate branch coverage. It also rejects any critical or high-risk file that remains below its file-level
gate.

*Mutation testing* makes small changes to production code, such as reversing a condition, and checks whether tests
fail. A surviving mutation can reveal an assertion gap even when line coverage is high. The permanent score floor is
75%.

`Measure-CoverageRisk.ps1` combines coverage reports with the criticality policy.
`Validate-MutationReport.ps1` checks the focused Stryker report. The exact pinned mutation command and the known
parser-instrumentation limitation are in the repository root `MUTATION_TESTING.md`.

Do not lower thresholds, add broad exclusions, or classify a real survivor away to make a run green.

## Property tests and fuzzing

A property test checks a rule across many generated values rather than one example. Round-trip properties distinguish
meaningful value equality from identical bytes: padding can be normalized, pointers are not relocated, and a
`UnionValue` explicitly retains raw storage.

The managed fuzz harness feeds bounded generated/corpus inputs to five targets. It records a stable seed and minimizes
failures so they can be replayed. Add a minimized failure as a named regression; a random failure that cannot be
reproduced is not enough.

## Compiler-differential fixtures

Small Clang and GCC fixtures record how selected C11 objects were laid out under specific recorded environments.
They help explain where Portable deliberately agrees or differs. They do not add a selectable compiler/ABI mode to
CStructSharp.

## Performance, packages, and release checks

BenchmarkDotNet scenarios compare timing and allocation for controlled before/after cases. Package checks inspect
metadata, framework assets, symbols, Source Link, installed consumer behavior, dependency audit results, and raw or
compressed sizes.

The browser adapter's source can be compared with its recorded wire format without compiling Web/WASM. Run relevant
frontend and browser checks locally when changing that application. Release automation builds the production
WASM explorer but does not repeat those checks.

## Documentation

```powershell
.\tools\Validate-Documentation.ps1
```

Run this from the repository root after changing public behavior or the site. It builds only the core net10 assembly,
executes documentation examples and language fixtures, generates API metadata, builds DocFX with warnings as errors,
and validates Markdown, spelling, links, search, browser behavior, accessibility, and artifact size.

When a check fails, keep its first meaningful error and use [Debugging contributor failures](debugging.md) rather
than rerunning the entire suite without narrowing the cause.
