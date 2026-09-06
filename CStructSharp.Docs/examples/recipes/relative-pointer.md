---
title: Follow a pointer relative to an origin
description: Run the complete relative-pointer example and check its values and bytes.
---

# Follow a pointer relative to an origin

**Advanced · C#**. Pointer width, field position, stored address, and effective target are different concepts. The options bound this example to one pointer level and one target byte.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- relative-pointer
```

The runner checks stored address 1 plus origin 1 reaches offset 2 and value 42. Success includes `PASS relative-pointer`.

This example uses the C# API. Browser capabilities and result shapes are described in the [browser guide](../../guides/browser/api.md).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](relative-pointer.cs).

[!code-csharp[Complete relative-pointer program](relative-pointer.cs)]

## Try it and diagnose mistakes

Set origin to 0 and predict the target.

Answer: The target becomes offset 1. It is a valid position but does not contain the intended value. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/pointers.md) or [choose another recipe](../../guides/recipes/index.md).
