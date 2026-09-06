---
title: Supply a runtime array count
description: Run the complete runtime-payload example and check its values and bytes.
---

# Supply a runtime array count

**Advanced · C#**. The application supplies COUNT through an integer dictionary. A prior field does not automatically become a variable. This dictionary is a C# API feature.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- runtime-payload
```

The runner checks three payload values; second is 32; length lookup preserves position. Success includes `PASS runtime-payload`.

This example uses the C# API. Browser capabilities and result shapes are described in the [browser guide](../../guides/browser/api.md).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](runtime-payload.cs).

[!code-csharp[Complete runtime-payload program](runtime-payload.cs)]

## Try it and diagnose mistakes

Set COUNT to 4 without adding input bytes.

Answer: Reading fails because the fourth payload byte is missing. Validate external counts before parsing. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/variables-options-and-limits.md) or [choose another recipe](../../guides/recipes/index.md).
