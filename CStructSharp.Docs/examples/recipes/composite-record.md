---
title: Combine text, an enum, and a union
description: Run the complete composite-record example and check its values and bytes.
---

# Combine text, an enum, and a union

**Intermediate · C#**. The enum describes a kind; it does not automatically choose a union member. The application decides which interpretation makes sense.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- composite-record
```

The runner checks Text, AB with a zero character, and exact six-byte round trip. Success includes `PASS composite-record`.

This example uses the C# API. Browser capabilities and result shapes are described in the [browser guide](../../guides/browser/api.md).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](composite-record.cs).

[!code-csharp[Complete composite-record program](composite-record.cs)]

## Try it and diagnose mistakes

Inspect both union member values for 34 12.

Answer: small sees 52; large sees 4660. They overlap the same storage. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/../language/tutorial/02-composites-and-layout.md) or [choose another recipe](../../guides/recipes/index.md).
