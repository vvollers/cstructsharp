---
title: Follow an absolute stored pointer
description: Run the complete follow-pointer example and check its values and bytes.
---

# Follow an absolute stored pointer

**Advanced · C#**. The one-byte pointer describes a position in the supplied bytes. It is not an address in the computer's process memory.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- follow-pointer
```

The runner checks stored address 1 points to value 42. Success includes `PASS follow-pointer`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=pointer).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](follow-pointer.cs).

[!code-csharp[Complete follow-pointer program](follow-pointer.cs)]

## Try it and diagnose mistakes

Replace address 1 with 0.

Answer: Zero is null and is not followed, even when a nonzero origin is configured. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/pointers.md) or [choose another recipe](../../guides/recipes/index.md).
