---
title: Read a fixed header
description: Run the complete decode-header example and check its values and bytes.
---

# Read a fixed header

**Beginner · C#**. A two-byte kind and four-byte length occupy six packed bytes. The typed read maps them to an ordinary C# class.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- decode-header
```

The runner checks kind 2, length 6; typed read succeeds and truncated read fails. Success includes `PASS decode-header`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=header).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](decode-header.cs).

[!code-csharp[Complete decode-header program](decode-header.cs)]

## Try it and diagnose mistakes

Change the first byte to 03 and update the expected kind.

Answer: kind is 3; length stays 6. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/install-and-first-parse.md) or [choose another recipe](../../guides/recipes/index.md).
