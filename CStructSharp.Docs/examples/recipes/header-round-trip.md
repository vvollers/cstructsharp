---
title: Write and update a header
description: Run the complete header-round-trip example and check its values and bytes.
---

# Write and update a header

**Beginner · C#**. Serialize creates bytes. UpdateStream changes the existing kind without shifting the length field.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- header-round-trip
```

The runner checks 03 00 06 00 00 00 after updating kind. Success includes `PASS header-round-trip`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=header-update).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](header-round-trip.cs).

[!code-csharp[Complete header-round-trip program](header-round-trip.cs)]

## Try it and diagnose mistakes

Change the replacement kind to 4.

Answer: The first byte becomes 04; the other five bytes stay the same. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/header-next-steps.md) or [choose another recipe](../../guides/recipes/index.md).
