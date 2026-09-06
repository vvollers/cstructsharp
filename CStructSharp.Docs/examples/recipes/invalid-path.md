---
title: Repair an invalid path
description: Run the complete invalid-path example and check its values and bytes.
---

# Repair an invalid path

**Beginner · C#**. Paths are case-sensitive. Fix the selected name instead of changing valid bytes.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- invalid-path
```

The runner checks Header.kind fails; header.kind reads 2. Success includes `PASS invalid-path`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=invalid-path).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](invalid-path.cs).

[!code-csharp[Complete invalid-path program](invalid-path.cs)]

## Try it and diagnose mistakes

Misspell kind as kinds.

Answer: The selected read raises CStructPathException. Restore kind to fix it. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/reading-values.md) or [choose another recipe](../../guides/recipes/index.md).
