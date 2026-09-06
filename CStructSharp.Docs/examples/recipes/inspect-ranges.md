---
title: Connect a field to its bytes
description: Run the complete inspect-ranges example and check its values and bytes.
---

# Connect a field to its bytes

**Intermediate · C#**. Debug ranges use an exclusive end position: [1,3) means offsets 1 and 2. The debug result includes the root wrapper.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- inspect-ranges
```

The runner checks uint16 occupies offsets 1 and 2; ResolveAddress returns 1. Success includes `PASS inspect-ranges`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=header).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](inspect-ranges.cs).

[!code-csharp[Complete inspect-ranges program](inspect-ranges.cs)]

## Try it and diagnose mistakes

Change the tag byte only.

Answer: The value range remains [1,3). Changing a value does not change these fixed field positions. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/debug-data-and-addresses.md) or [choose another recipe](../../guides/recipes/index.md).
