---
title: Building the repository
description: Build only the part of CStructSharp you are changing and understand the output each command produces.
---

# Building the repository

Run these commands from the repository root. Start with the smallest build that covers your change; this keeps the
feedback loop short and avoids rebuilding the optional browser workbench.

## Build only the core library

```powershell
dotnet restore .\CStructSharp\CStructSharp.csproj
dotnet build .\CStructSharp\CStructSharp.csproj -c Release -f net10.0 --no-restore
```

The restore resolves core NuGet dependencies. The build then compiles only the `net10.0` target in Release mode and
uses that restored graph. Success ends with zero errors and places the DLL, XML documentation, and PDB in:

```text
CStructSharp/bin/Release/net10.0/
```

DocFX uses those three files to generate the API reference. Use `-f net8.0` when you specifically need the other
target.

## Build the routine development solution

```powershell
dotnet restore .\CStructSharp.NonWeb.sln
dotnet build .\CStructSharp.NonWeb.sln -c Release --no-restore
```

This compiles core, tests, fuzzing support, and benchmarks. It deliberately excludes the WebAssembly adapter.
`--no-restore` makes a missing restore visible instead of hiding it inside the build.

Do not substitute `CStructSharp.sln` for routine work. The full solution adds the WASM project and is reserved for
the final integration and release rehearsal.

## Build and preview the documentation

The complete documentation check is:

```powershell
.\tools\Validate-Documentation.ps1
```

It restores pinned tools, builds only the core `Release/net10.0` assembly, runs examples and language fixtures,
generates API pages, builds DocFX with warnings treated as errors, and runs content/browser checks.

After one complete validation, use the faster authoring command:

```powershell
.\tools\Build-Documentation.ps1 -NoBuild -Serve
```

`-NoBuild` reuses the current core assembly but refuses to run when that assembly is missing or older than relevant
source. `-Serve` starts the generated site at `http://localhost:8080`; press `Ctrl+C` to stop it. The generated
`CStructSharp.Docs/api/*.yml` files and `_site/` directory are ignored build output.

## Package candidate

```powershell
dotnet pack .\CStructSharp\CStructSharp.csproj -c Release -o .\artifacts\package
.\tools\Test-PackageConsumer.ps1 -PackageDirectory .\artifacts\package
```

`dotnet pack` creates a `.nupkg` library package and `.snupkg` symbol package under `artifacts/package`. It does not
publish either file. The consumer script creates isolated test applications, installs the package as a real NuGet
dependency, and checks both supported target frameworks.

If packing fails, first confirm that the Release build and package metadata are valid. If the consumer cannot find
the package, verify the output directory and version rather than adding a public feed that could hide the local
candidate.

See [Testing](testing.md) for the checks to run after a build and [Release process](release-process.md) for the full
candidate sequence.
