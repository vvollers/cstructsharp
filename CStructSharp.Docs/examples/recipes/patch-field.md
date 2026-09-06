---
title: Patch a nested field in a stream
description: Run the complete patch-field example and check its values and bytes.
---

# Patch a nested field in a stream

**Intermediate · C#**. The stream begins with a two-byte prefix. Set Position to the start of the selected structure before updating it.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- patch-field
```

The runner checks EE EE 34 12 A5; invalid replacement preserves bytes and position. Success includes `PASS patch-field`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=nested-update).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](patch-field.cs).

[!code-csharp[Complete patch-field program](patch-field.cs)]

## Try it and diagnose mistakes

Try replacing flags with 999.

Answer: It does not fit uint8. Validation fails before changing the stream. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/updating-existing-data.md) or [choose another recipe](../../guides/recipes/index.md).
