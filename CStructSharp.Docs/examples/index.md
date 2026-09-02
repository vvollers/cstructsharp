---
title: Executable examples
description: Run the complete C# examples used throughout the CStructSharp documentation.
---

# Executable examples

The snippets in the guides come from the program shown at the end of this page. They are complete examples rather
than fragments copied into Markdown. The documentation build compiles and runs the program against the current
`CStructSharp` project, which catches stale method names and incorrect expected values.

Each named scenario demonstrates one task, such as decoding a header, following a pointer, preserving union bytes, or
patching one field. Assertions check the important values and byte sequences. The examples do not use the network or
machine-specific paths, so the result should be the same on every supported development machine.

## Run all examples

You need the repository's .NET 10 SDK. Open PowerShell in the repository root—the directory containing
`CStructSharp.Docs`—and run:

```powershell
dotnet run --project .\CStructSharp.Docs\examples\CStructSharp.Docs.Examples.csproj -c Release
```

`dotnet run` builds the small example project in Release mode and then starts it. A successful run prints one line
for each of the 11 scenarios and ends with:

```text
PASS all 11 scenarios
```

If a scenario's actual result differs from the documented result, the program throws an exception and exits with a
nonzero status. Read the last scenario name printed before the exception to locate the failing example. If the build
cannot find a suitable SDK, confirm that `dotnet --version` is using the SDK selected by the repository's
`global.json`.

The methods below are independent scenarios, not steps in one long application. Start with `DecodeHeader`, then open
the [recipe index](../guides/recipes/index.md) to find the scenario that matches your task.

[!code-csharp[All executable scenarios](Program.cs)]
