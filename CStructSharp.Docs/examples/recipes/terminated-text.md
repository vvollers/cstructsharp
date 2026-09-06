---
title: Read zero-terminated text
description: Run the complete terminated-text example and check its values and bytes.
---

# Read zero-terminated text

**Intermediate · C#**. The empty brackets on char mean a terminated string, not a general array that consumes all remaining bytes.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- terminated-text
```

The runner checks 41 42 00 reads AB and writes back unchanged. Success includes `PASS terminated-text`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=terminated-text).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](terminated-text.cs).

[!code-csharp[Complete terminated-text program](terminated-text.cs)]

## Try it and diagnose mistakes

Remove the final zero byte.

Answer: Reading fails because the terminator is missing. Restore it; do not assume the end of input is a terminator. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/strings-and-encodings.md) or [choose another recipe](../../guides/recipes/index.md).
