---
title: Read into a C# class
description: Run the complete map-poco example and check its values and bytes.
---

# Read into a C# class

**Beginner · C#**. The complete program includes the Point class. Compatible properties can use C# capitalization when matching is unambiguous.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- map-poco
```

The runner checks Point with X -2 and Y 5. Success includes `PASS map-poco`.

This example uses the C# API. Browser capabilities and result shapes are described in the [browser guide](../../guides/browser/api.md).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](map-poco.cs).

[!code-csharp[Complete map-poco program](map-poco.cs)]

## Try it and diagnose mistakes

Change FE FF to FF FF.

Answer: X becomes -1, not 65535, because int16 is signed. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/typed-values.md) or [choose another recipe](../../guides/recipes/index.md).
