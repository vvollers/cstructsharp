---
title: Read inside a larger stream
description: Run the complete positioned-stream example and check its values and bytes.
---

# Read inside a larger stream

**Advanced · C#**. The two-byte prefix belongs to surrounding data. Selected addresses are stream coordinates, so length starts at 2 + 2.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- positioned-stream
```

The runner checks length address 4; inspection preserves Position 2; length reads 6. Success includes `PASS positioned-stream`.

This example uses the C# API. Browser capabilities and result shapes are described in the [browser guide](../../guides/browser/api.md).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](positioned-stream.cs).

[!code-csharp[Complete positioned-stream program](positioned-stream.cs)]

## Try it and diagnose mistakes

Start the stream at Position 0.

Answer: The prefix becomes part of the header input and values change. Restore Position 2. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/reading-values.md) or [choose another recipe](../../guides/recipes/index.md).
