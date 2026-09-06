---
title: Diagnose the wrong byte order
description: Run the complete byte-order example and check its values and bytes.
---

# Diagnose the wrong byte order

**Beginner · C#**. Changing byte order changes the value, not the field width. A read may succeed even when the chosen byte order is wrong.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- byte-order
```

The runner checks little-endian kind 2; big-endian kind 512 and length 100663296. Success includes `PASS byte-order`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=byte-order).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](byte-order.cs).

[!code-csharp[Complete byte-order program](byte-order.cs)]

## Try it and diagnose mistakes

Swap the two kind bytes and read with big-endian order.

Answer: 00 02 gives kind 2 in big-endian order. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/binary-layout-basics.md) or [choose another recipe](../../guides/recipes/index.md).
