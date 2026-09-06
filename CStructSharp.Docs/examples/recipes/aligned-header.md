---
title: Explain padding in an aligned header
description: Run the complete aligned-header example and check its values and bytes.
---

# Explain padding in an aligned header

**Intermediate · C#**. Two padding bytes follow kind. A four-byte length begins at an offset divisible by four.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- aligned-header
```

The runner checks length 6 at offset 4; eight-byte round trip. Success includes `PASS aligned-header`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=alignment).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](aligned-header.cs).

[!code-csharp[Complete aligned-header program](aligned-header.cs)]

## Try it and diagnose mistakes

Predict the position with aligned set to false.

Answer: Packed length starts at 2, so the packed input must omit the two padding bytes. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/binary-layout-basics.md) or [choose another recipe](../../guides/recipes/index.md).
