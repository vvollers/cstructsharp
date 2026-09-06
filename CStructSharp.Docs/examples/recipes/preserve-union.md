---
title: Preserve or select union storage
description: Run the complete preserve-union example and check its values and bytes.
---

# Preserve or select union storage

**Advanced · C#**. Raw storage preserves bytes whose interpretation may be unknown. Explicit member selection is useful when creating a new value.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- preserve-union
```

The runner checks raw 34 12 round trip; selected small writes A5 00. Success includes `PASS preserve-union`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=union).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](preserve-union.cs).

[!code-csharp[Complete preserve-union program](preserve-union.cs)]

## Try it and diagnose mistakes

Predict the result when selecting large with value 4660.

Answer: The little-endian bytes are 34 12. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/unions.md) or [choose another recipe](../../guides/recipes/index.md).
