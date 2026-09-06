---
title: Read flags stored in one byte
description: Run the complete bit-flags example and check its values and bytes.
---

# Read flags stored in one byte

**Intermediate · C#**. Portable allocates these bitfields from the low bits. The one-bit enabled field comes before the three-bit mode.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- bit-flags
```

The runner checks 0B stores enabled 1, mode 5, reserved 0. Success includes `PASS bit-flags`.

[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=bitfields).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](bit-flags.cs).

[!code-csharp[Complete bit-flags program](bit-flags.cs)]

## Try it and diagnose mistakes

Change 0B to 0A.

Answer: enabled becomes 0; mode stays 5. Update the assertion before running the modified example. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/../language/bitfields.md) or [choose another recipe](../../guides/recipes/index.md).
