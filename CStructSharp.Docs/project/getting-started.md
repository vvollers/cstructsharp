---
title: Contributor setup
description: Install the required tools, clone the repository, and establish a passing non-Web development baseline.
---

# Contributor setup

This page prepares a machine for core, tests, benchmarks, packages, and documentation work. Run every command in
PowerShell from the repository root unless a step says otherwise.

## Prerequisites

Install:

- Git;
- a stable .NET 10 SDK; and
- PowerShell 7 for the repository scripts.

Node.js is not needed for ordinary core, test, benchmark, or package work. Install Node 24 or 26 only when you will
build or validate the documentation. The optional Web projects use their own separately maintained Node toolchain.

The repository's `global.json` requests SDK `10.0.100` or a newer installed .NET 10 feature band and rejects
prerelease SDKs. From the directory containing `global.json`, verify the tools:

```powershell
dotnet --version
git --version
pwsh --version
```

The .NET command should print a stable `10.0.x` version. If it prints an older major version or reports an SDK
resolver error, install a stable .NET 10 SDK before changing project targets.

## Clone the repository

Choose a parent directory where you keep source projects:

```powershell
git clone https://github.com/vvollers/CStructSharp.git
cd CStructSharp
```

`git clone` downloads the repository and creates the `CStructSharp` directory. `cd` makes it the working directory
for the commands that follow. Confirm that `CStructSharp.NonWeb.sln` and `global.json` are present before continuing.

## Restore pinned tools and packages

```powershell
dotnet tool restore
dotnet restore .\CStructSharp.NonWeb.sln
```

The first command installs the exact local versions recorded in `.config/dotnet-tools.json`, including DocFX,
mutation, Source Link, and public-API tools. They are local to this repository; you do not need global copies.

The second command resolves packages for the core library, managed tests, fuzz harness, and benchmarks. It does not
restore the optional Web/WASM projects. A successful restore ends without errors and creates only ignored local
package/build state.

## Build and test the baseline

```powershell
dotnet build .\CStructSharp.NonWeb.sln -c Release --no-restore
dotnet test .\CStructSharpTests\CStructSharpTests.csproj -c Release --no-build
```

`-c Release` uses the configuration measured by repository quality checks. `--no-restore` is safe because the
previous step restored packages; it prevents a build from quietly changing that step. `--no-build` makes the tests
run the assemblies produced by the immediately preceding build.

The build should finish with zero errors. The test command runs the project for both `net8.0` and `net10.0`; both
target summaries must pass.

If the baseline fails before you edit anything, stop and record the command, target framework, first error, SDK
version, and working-tree state. The [debugging guide](debugging.md) helps separate a local setup problem from an
existing source failure.

## Make a first focused change

Use this loop:

1. Find the smallest test class that describes the behavior.
2. Add a test that fails for the reason you intend to fix.
3. Run that test on both target frameworks.
4. Change the shared core path rather than adding different fixes to several public overloads.
5. Run the focused tests again, then the full managed suite and affected repository checks.
6. Update XML comments, guides, language files, examples, or compatibility data if the public behavior changed.
7. Review every changed file.

For example, this real filter runs the Portable manual fixtures on one framework:

```powershell
dotnet test .\CStructSharpTests\CStructSharpTests.csproj -c Release -f net10.0 --no-build --filter "FullyQualifiedName~ManualLanguageFixtureTests"
```

Run the equivalent command with `-f net8.0` as well. Replace the filter with the narrow test class for your change.
A successful focused run reports only the selected tests and exits with code 0.

Before asking for review:

```powershell
git status --short
git diff --check
```

The first command lists modified and untracked files. The second catches whitespace errors. Neither command changes
the working tree.

Continue with the [repository map](repository-map.md) and [contributing workflow](contributing.md).
