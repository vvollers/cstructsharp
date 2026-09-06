---
title: Bound the work of a read
description: Run the complete bounded-read example and check its values and bytes.
---

# Bound the work of a read

**Advanced · C#**. Limits count work done by an operation. A complete input can fail because its allowed read budget is too small.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- bounded-read
```

The runner checks three-byte budget fails; six-byte budget reads length 6. Success includes `PASS bounded-read`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=limits).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](bounded-read.cs).

[!code-csharp[Complete bounded-read program](bounded-read.cs)]

## Try it and diagnose mistakes

Use a limit of 5.

Answer: The six-byte read still fails. Set limits from the format, not by repeatedly raising them until arbitrary data succeeds. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/variables-options-and-limits.md) or [choose another recipe](../../guides/recipes/index.md).
