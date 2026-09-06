---
title: Reuse a layout with different output storage
description: Run the complete round-trip example and check its values and bytes.
---

# Reuse a layout with different output storage

**Advanced · C#**. An owned array is simplest. A span or buffer writer lets the caller provide storage but has different partial-write behavior.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- round-trip
```

The runner checks 34 12 A5 from array, span, and buffer writer; unused capacity preserved. Success includes `PASS round-trip`.

This example uses the C# API. Browser capabilities and result shapes are described in the [browser guide](../../guides/browser/api.md).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](round-trip.cs).

[!code-csharp[Complete round-trip program](round-trip.cs)]

## Try it and diagnose mistakes

Predict how many bytes are initialized in the eight-byte span.

Answer: Only three. Use the returned count rather than treating all capacity as output. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/spans-and-memory.md) or [choose another recipe](../../guides/recipes/index.md).
