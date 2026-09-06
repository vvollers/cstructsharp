---
title: Preserve an unknown enum number
description: Run the complete preserve-enum example and check its values and bytes.
---

# Preserve an unknown enum number

**Advanced · C#**. An unknown member is still a valid stored integer. Keep its width and signedness instead of forcing it into a named application enum.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- preserve-enum
```

The runner checks 4294967295 with no known name, unsigned 32-bit backing. Success includes `PASS preserve-enum`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=enum).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](preserve-enum.cs).

[!code-csharp[Complete preserve-enum program](preserve-enum.cs)]

## Try it and diagnose mistakes

Change the input to 01 00 00 00.

Answer: The enum name is Known and its value is 1. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/enums.md) or [choose another recipe](../../guides/recipes/index.md).
