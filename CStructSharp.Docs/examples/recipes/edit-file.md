---
title: Inspect and edit a complete binary file
description: Run the complete edit-file example and check its values and bytes.
---

# Inspect and edit a complete binary file

**Advanced · C#**. Validate signature, version, count, and file length in application code. Then supply COUNT and update one fixed field. The example creates and removes its own temporary fixture file.

## Run this example

Prerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- edit-file
```

The runner checks 435301020100100200A5; truncated and excessive-count fixtures rejected. Success includes `PASS edit-file`.

This example uses the C# API. Browser capabilities and result shapes are described in the [browser guide](../../guides/browser/api.md).

## Complete program

The layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the
repository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.
These examples follow the source version; use a matching package when testing a release.

[Download the complete C# source](edit-file.cs).

[!code-csharp[Complete edit-file program](edit-file.cs)]

## Try it and diagnose mistakes

Change the record count to 255 without changing file length.

Answer: Application validation rejects it before traversal. Patching cannot insert more records or move following data. The program contains assertions for its original inputs. When changing an input intentionally,
update the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.

Continue with [the related guide](../../guides/binary-file-walkthrough.md) or [choose another recipe](../../guides/recipes/index.md).
