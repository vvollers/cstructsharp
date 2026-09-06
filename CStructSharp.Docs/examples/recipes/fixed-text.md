---
title: Read and write fixed text
description: Run the complete fixed-text example and check its values and bytes.
---

# Read and write fixed text

**Intermediate · C#**. Fixed-capacity text preserves the complete field when read. Writing shorter text fills the remaining space with zeros.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- fixed-text
```

The runner checks ABC followed by a zero character; XY writes 58 59 00 00. Success includes `PASS fixed-text`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=text).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](fixed-text.cs).

[!code-csharp[Complete fixed-text program](fixed-text.cs)]

## Try it and diagnose mistakes

Try writing ABCDE into four bytes.

Answer: The write fails because the text exceeds the field capacity. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/strings-and-encodings.md) or [choose another recipe](../../guides/recipes/index.md).
