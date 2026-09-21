# Contributing to CStructSharp

Thank you for helping with CStructSharp. This guide explains the usual workflow and points out the extra checks
needed for parts of the project that have a public compatibility promise.

If you are new to the repository, start with the build instructions in [README.md](README.md). You do not need to
understand every release check before fixing a small bug.

## Start with a working build

Run the normal build and test commands from the repository root:

```sh
dotnet restore CStructSharp.NonWeb.sln
dotnet build CStructSharp.NonWeb.sln -c Release --no-restore
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release --no-build
```

The test command runs on both `net8.0` and `net10.0`. If it fails before you make a change, save the first error and
your `dotnet --info` output. That makes it much easier to tell a setup problem from a code problem.

Use `CStructSharp.NonWeb.sln` for normal library work. It leaves out the WebAssembly project and keeps the build
smaller. Build the web projects only when your change affects the browser bridge or the web apps.

## A good workflow for a code change

1. Keep the change about one problem or feature.
2. For a bug, add a test that fails because of that bug.
3. Make the smallest change that fixes the shared code path.
4. Run the new test on .NET 8 and .NET 10.
5. Run the complete managed test suite.
6. Run the extra checks listed below for the area you changed.
7. Update comments and documentation if users will see different behavior.
8. Review `git diff` before you ask someone else to review the change.

Do not remove an existing test just because a new implementation makes it fail. First decide whether the old test
describes a public promise. If the promise has intentionally changed, replace the test and explain the new behavior.

## Choose checks for the part you changed

### Library code

Run the full managed test suite:

```sh
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj -c Release
```

When a change only affects one class, use a test filter while you work, then finish with the full suite. For example:

```sh
dotnet test tests/CStructSharpTests/CStructSharpTests.csproj \
  -c Release -f net10.0 \
  --filter "FullyQualifiedName~WriteBudgetTests"
```

Repeat a focused test with `-f net8.0` before you finish.

### Layout language, parser, expressions, or binary behavior

Changes in this area can affect files, protocols, and stored data. Update the parts that describe the behavior:

- the relevant pages in `docs/language/`;
- the Portable contract in `contracts/language/portable-v1.json`;
- the valid and invalid language fixtures;
- the feature-operation matrix; and
- tests on both supported .NET versions.

Then run:

```sh
node tools/documentation/validate-canonical-reference.mjs
node tools/quality/feature-operation-matrix.mjs
```

The feature-operation matrix records which types and operations are supported. Update it when you change accepted
syntax, a codec, a returned value shape, field placement, or the behavior of a public operation.

Keep the difference between a value and its bytes clear. Two byte sequences can sometimes represent the same value,
while a round-trip test may require the exact original bytes. The
[writing and updating guide](docs/language/writing-and-updating.md) explains this distinction.

### Public .NET API

The public .NET API is compared with the files in `contracts/api/managed-rc1/`. Run:

```sh
node tools/quality/managed-api-baseline.mjs compare
```

If the comparison fails, check whether a public type, member, parameter, return type, or exception has changed. Do
not replace the baseline simply to make the check green. For an intentional public change:

1. Decide whether the package version or release plan must change.
2. Refresh the baseline with `node tools/quality/managed-api-baseline.mjs update --kind additive|breaking|correction --rationale "..." --impact "..."`, which rewrites the canonical surface, its hashes, and the review history.
3. Add or update behavior tests and package-consumer tests.
4. Put migration notes in `CHANGELOG.md` and update guides and API documentation to describe the current behavior.

### Browser bridge and web apps

The browser API has its own compatibility files in `contracts/api/browser-rc1/`. Run the browser
contract check when exports, options, result envelopes, error categories, or number handling change:

```bash
node tools/quality/browser-contract.mjs
```

A browser change must also pass the relevant frontend checks locally. The release workflow builds the production
WASM explorer and inspector and runs their unit and browser tests, along with package-consumer checks.
Use the owning app's README for local formatting, audit, and compatibility checks.

### Fuzzing

The managed fuzz harness keeps a small set of known inputs and makes repeatable mutations from them. If fuzzing
finds a crash:

1. Save the exact input from the failure report.
2. Reduce it to the smallest input that still fails.
3. Add it as a named corpus seed or a focused regression test.
4. Confirm that the new test fails before changing the library.
5. Fix the shared library code and rerun the harness on .NET 8 and .NET 10.

Do not hide a failure by changing the stable seed, lowering the iteration count, increasing a safety limit without
review, or treating a new exception as expected. Check the corpus file with:

```sh
node tools/quality/fuzz-corpus.mjs
```

### Mutation testing

Mutation testing checks whether the tests notice small, deliberate changes to the library logic. Use it for changes
to the parser, layout calculations, readers, writers, and expression handling. The commands and the permanent scope
are explained in [MUTATION_TESTING.md](MUTATION_TESTING.md).

### Compiler comparison fixture

`tools/compiler-fixtures/portable-host-facts.c` records what real C compilers do with a set of layout shapes
(`contracts/quality/compiler-fixtures/shapes.json` describes each shape and the `BitfieldPacking` mode in which the
library reproduces it). The observations are evidence, not a promise that CStructSharp follows a host compiler's
ABI; `CompilerDifferentialFixtureTests` verifies every claim against every checked-in baseline.

The two-byte packing shape intentionally triggers MSVC's member-alignment warning C4121. The fixture suppresses
only that warning around that shape; all other diagnostics remain errors. Review fresh observations before adopting
a baseline, including when a source-only change requires a new fixture hash.

If you change the C file or the shapes, re-record the baselines you can (`node tools/quality/compiler-fixture.mjs
record --compiler gcc --output contracts/quality/compiler-fixtures/baselines/<platform>-<compiler>.json`), let the
`compiler-fixtures` workflow record the other platforms, run the managed tests on both .NET versions, and then run:

```sh
node tools/quality/compiler-fixture.mjs validate
node tools/quality/compiler-fixture.mjs table
```

The second command regenerates the comparison table in `docs/language/differences-from-c.md`; CI checks that it is
current. A record made from an older fixture source is stale and fails validation.

## Test-quality requirements

The normal CI checks these minimums:

- 78% line coverage;
- 80% branch coverage;
- no files classified as critical or high coverage risk; and
- a 75% mutation score for the reviewed mutation-testing scope.

These numbers are a backstop, not the goal of a test. A useful test should explain behavior and fail for a clear
reason. Do not exclude difficult files, lower a threshold, or count a mutation compile error as a detected behavior
just to improve a score. The
[testing guide](docs/project/testing.md) explains how the measurements are made.

## Documentation ownership and update triggers

Run the full documentation check whenever you change files in `docs/`:

```sh
node tools/documentation/validate-documentation.mjs
```

Update documentation alongside the code when you change:

- a public API, default, exception, ownership rule, or limit;
- layout syntax, primitive behavior, paths, field placement, or supported operations;
- build steps, dependencies, tests, workflows, packaging, or release steps; or
- documentation navigation, styling, search, templates, or deployment.

API changes usually need updated XML comments, API-reference checks, examples, and the managed API comparison.
Language changes usually need manual pages, contract data, fixtures, the feature matrix, and tests on both .NET
versions. Website or navigation changes also need the browser and accessibility tests and the Pages artifact check.

Do not commit generated `_site` output, generated API YAML, browser reports, logs, or local planning notes. Examples
and snippets that are published should be compiled or otherwise checked so they cannot quietly become outdated.

In a pull request, say which documentation you updated. If no documentation changed, a short explanation is enough.

## Safety and compatibility

CStructSharp follows pointers and reads binary data from streams supplied by its caller. Invalid layouts, unknown
names, bad paths, and unsafe pointer targets should produce clear errors. Avoid silent fallback behavior that can
turn bad input into a believable but wrong result.

Be especially careful with layout and ABI assumptions. Document the supported rule instead of guessing what a C
compiler would do on the current machine.

## Before requesting review

- Run the tests and checks that match your change.
- Run `git diff --check` to catch whitespace mistakes.
- Read the complete diff, including tests and documentation.
- Mention any check you could not run and why.
- Keep generated files and unrelated edits out of the change.

## Release checklist for maintainers

Most contributions do not need this section. Before preparing a release candidate, maintainers should:

- follow the [project documentation](docs/project/index.md);
- pass managed tests on both target frameworks;
- pass formatting, coverage, risk, mutation, dependency-audit, API, language, fuzz, and documentation checks;
- pass package, symbol-package, and package-consumer validation;
- pass the benchmark and package limits in
  `contracts/performance/non-web-rc1.json`;
- run `node tools/quality/non-web-release-budgets.mjs --self-test`;
- build the full WebAssembly and Vue application and pass its audit, browser, compatibility, reproducibility, and
  size checks when the browser is part of the release;
- update `CHANGELOG.md` and check the package version, license, repository URL, documentation URL, release notes, and
  supported frameworks; and
- complete the full-solution release rehearsal.

Triggering `.github/workflows/release.yml` is the release decision: the workflow first builds and verifies every
artifact (managed tests, packages, documentation, WebAssembly bundle, onboarding and browser checks) with no write
access to the repository, then — only if that verification succeeds — commits and tags the version bump, publishes
the NuGet and npm packages, deploys the documentation/explorer/inspector site, and creates the GitHub Release. There is no separate,
manually-approved publish step after triggering; see
[the release process guide](docs/project/release-process.md) for detail.
