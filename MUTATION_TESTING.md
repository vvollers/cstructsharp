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

## Run the complete check

Run these commands from the repository root:

```powershell
dotnet tool restore
dotnet stryker `
  --config-file .\stryker-config.json `
  --solution .\CStructSharp.NonWeb.sln `
  --target-framework net10.0 `
  --configuration Release `
  --output .\artifacts\mutation\permanent `
  --skip-version-check

.\tools\Validate-MutationReport.ps1 `
  -ReportPath .\artifacts\mutation\permanent\reports\mutation-report.json
```

The first command installs the version of Stryker listed in `.config/dotnet-tools.json`. The second command creates
and tests the mutations. The last command checks that the report was made with the repository's approved settings.

This can take much longer than an ordinary test run. Progress is shown in the terminal. The JSON and HTML reports
are written below `artifacts/mutation/permanent/`. The `artifacts/` directory is ignored by Git.

## What the configuration does

`stryker-config.json`:

- mutates the main `CStructSharp` library;
- uses `CStructSharpTests` to test each mutation;
- limits mutation to 34 files that contain the main parsing and binary-data logic;
- runs the complete test project instead of selecting tests from coverage data;
- writes progress, JSON, and HTML reports; and
- requires a mutation score of at least 75%.

The report validator also checks that there are no surviving mutations, uncovered mutations, or mutations that fail
at runtime. This is stricter than checking the percentage alone.

## A known parser limitation

`CStructDefinitionParser.cs` is intentionally in the mutation list. At the moment, Stryker's safe rewriting of its
Pidgin parser expressions creates candidates that do not compile. The report validator records this known tool
limitation and checks that it has not silently changed.

A compile error is not treated as proof that a test found a bug. If a future Stryker or source change produces valid
mutations for this file, the result must be reviewed and the tests should run against those mutations.

## Use a focused run while developing

When you change a risky file, start by mutating only that file. For example:

```powershell
dotnet stryker `
  --config-file .\stryker-config.json `
  --solution .\CStructSharp.NonWeb.sln `
  --target-framework net10.0 `
  --configuration Release `
  --mutate CStructWriter.cs `
  --output .\artifacts\mutation\writer `
  --skip-version-check
```

Do not pass a focused report to `Validate-MutationReport.ps1`; that validator expects the complete permanent scope.
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
