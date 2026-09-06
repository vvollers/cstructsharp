---
title: Inspect and edit a small binary file
description: Validate a teaching file format, read counted records, and patch a field without changing other bytes.
---

# Inspect and edit a small binary file

This advanced walkthrough combines the [header lesson](install-and-first-parse.md), arrays, runtime variables,
and updates. You need basic file I/O and the .NET 10 SDK to run the repository example.

## Define the format before writing code

The teaching file uses packed little-endian fields. It contains a signature, version, record count, and records.
Each record contains a two-byte id and a one-byte flags value. This example accepts 2 through 32 records.

| Offset | Bytes in the fixture | Meaning |
| --- | --- | --- |
| 0–1 | `43 53` | ASCII signature CS |
| 2 | `01` | Version 1 |
| 3 | `02` | Two records |
| 4–6 | `01 00 10` | Record 0: id 1, flags 16 |
| 7–9 | `02 00 20` | Record 1: id 2, flags 32 |

The application validates the signature and version. CStructSharp does not know that those values identify this
format. The application also checks the count and exact length before supplying `COUNT` as a runtime variable.
A field named `count` does not automatically bind an array expression.

## Run and inspect the complete program

```sh
dotnet run --project CStructSharp.Docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- edit-file
```

The example creates a temporary fixture file, reads it, and removes it when finished. It patches record 1's flags
to `A5` in a copy, verifies that the other nine bytes are unchanged, and rejects both a truncated file and an
excessive count. Its printed output includes `435301020100100200A5` and `PASS edit-file`.

[!code-csharp[Complete file inspector](../examples/recipes/edit-file.cs)]

## Why the stream position matters

The records begin at offset 4. Before selecting `data.records[1].flags`, the program sets `Position` to 4.
The selected field is then at `4 + 3 + 2 = 9`. The count dictionary describes this operation; it does not modify
the reusable compiled layout.

The program validates all required records before patching. It checks untouched bytes after patching rather than
assuming a successful return proves preservation. For a real file, decide how to save the resulting copy and how
to recover from a physical storage failure. Library validation and operating-system write failures have different
[recovery behavior](errors-and-recovery.md).

## Try a change

Change count to `255` but keep the fixture length. The application rejects it before traversal. Remove the final
byte instead: the exact-length check rejects the truncated record. To add a record, construct a new complete file
with an updated count; a fixed-field update cannot move following data.

The browser does not expose the runtime-variable dictionary. The [browser inspector walkthrough](browser/inspector.md)
uses a fixed two-record variant and explicitly validates that count. See [runtime variables](variables-options-and-limits.md)
for formats whose record count varies.
