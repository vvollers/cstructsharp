---
title: Executable examples
description: Run individual CStructSharp examples or download complete programs with their required types and helpers.
---

# Executable examples

Choose a task in the [recipe catalog](../guides/recipes/index.md). Each of the 22 recipes has a complete downloadable
program, exact checked values, an exercise, and an answer. Start with the [minimal first program](../guides/install-and-first-parse.md)
if you have not used the library before.

## Run one or all examples

With the repository's .NET 10 SDK, run from the repository root:

```sh
dotnet run --project docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- decode-header
```

Use `--list` to list scenario names. Omit the final `-- decode-header` to run all scenarios. Success ends with
`PASS all 22 scenarios`; a failed assertion reports the expected and actual value or byte sequence.

## Adapt an independent program

Each recipe page contains all required imports, types, and comparison helpers. Copy its whole program into a new
.NET 10 console project with a matching CStructSharp package, or follow the repository run command above.
The first-use C# starter is also checked on .NET 8.

The complete programs are generated from the same methods that the runner executes, so copied code and tested code
stay together. The checked source is in `Program.cs` and `MoreExamples.cs`. After changing a scenario or its teaching
text in `tools/documentation/export-documentation-examples.mjs`, run that script or build the documentation to regenerate pages.
The original snippet region names remain available to API and language guides.

For larger tasks, follow [the binary file walkthrough](../guides/binary-file-walkthrough.md) or
[the browser inspector](../guides/browser/inspector.md).
