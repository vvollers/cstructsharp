---
title: Read an item in a nested array
description: Run the complete nested-array example and check its values and bytes.
---

# Read an item in a nested array

**Intermediate · C#**. Array indexes start at zero. Each item occupies two bytes, so the second starts at offset 2.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- nested-array
```

The runner checks packet.items[1].id is 2; exact four-byte round trip. Success includes `PASS nested-array`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=arrays).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](nested-array.cs).

[!code-csharp[Complete nested-array program](nested-array.cs)]

## Try it and diagnose mistakes

Select items[0].id instead.

Answer: The first id is 1. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/reading-values.md) or [choose another recipe](../../guides/recipes/index.md).
