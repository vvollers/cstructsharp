---
title: Write, update, and use a C# class
description: Continue the first header example by creating bytes, changing a field, and handling a short input.
---

# Write, update, and use a C# class

Start with the application from [your first parse](install-and-first-parse.md). Replace its `Program.cs` with the
complete program below. Run `dotnet run` after each change you try.

[!code-csharp[Complete header continuation](../examples/starter/Next.cs)]

Expected output:

```text
Created: 020006000000
Updated: 030006000000
Kind = 3; Length = 6
Truncated read succeeds = False
```

## Create bytes

`Serialize` allocates a new byte array and writes the fields according to the layout. `Header` is a C# class
defined at the bottom of this program that implements `ICStructMapped<Header>`: its `WriteTo` stores `Kind` under
the layout name `kind` and `Length` under `length`, and its `ReadFrom` does the reverse with `Get<T>`, whose
conversions are range-checked (`ushort` for the two-byte `uint16`, `uint` for `uint32`). The `[ModuleInitializer]`
method registers the class before any other code runs; the `[CStructMapped]` source generator writes all of this
for a `partial` class, so the hand-written version is here to show what it does.

## Change existing bytes

`Update` selects `header.kind` and writes the replacement in its existing two-byte space. It leaves the four
length bytes alone. A stream is an object that supports reading or writing bytes; `MemoryStream` keeps those bytes
in memory and lets the library move to the selected position. An update cannot insert space or move later fields.

| Byte range | Before | After | Meaning |
| --- | --- | --- | --- |
| `[0,2)` | `02 00` | `03 00` | `kind` changes from 2 to 3. |
| `[2,6)` | `06 00 00 00` | `06 00 00 00` | `length` stays 6. |

`[0,2)` includes offsets 0 and 1, not 2. These are byte offsets from the start of this input. The `MemoryStream`
wraps the supplied array, so this update changes those array bytes too. `Serialize` instead creates a new array.
Do not generalize this small in-memory success into a promise that disk writes are atomic: an I/O failure can
leave a destination partly changed. Keep the original when you need recovery; see
[update existing data](updating-existing-data.md) for validation and write-failure behavior.

## Read into a class and handle missing bytes

`ReadValue<Header>` gives application code typed properties through `Header`'s own `ReadFrom`. The byte array is
read in place; a `ReadOnlySpan<byte>` or `ReadOnlyMemory<byte>` slice of a larger buffer works the same way.

The last read has only one byte. `TryReadValue` returns `false` for this expected library failure. Invalid method
arguments can still throw; it is not a way to suppress every programming error.

Do not use the discarded output after a failed `TryReadValue`. Obtain a complete input, retry, and use the result
only after success. For diagnostics rather than a Boolean, see [errors and recovery](errors-and-recovery.md).
The mapping methods in `Header` connect layout names to C# properties; they do not change field offsets.
You can continue using `StructValue.Get<T>` from the first program without adopting mapped or generated classes.

## Try a change

Change the replacement kind to `65535`. Answer: the first two updated bytes are `FF FF`. Try `65536` next.
Answer: the replacement does not fit `uint16`, so the update fails. Restore a valid value rather than changing the
field width unless the actual format calls for a different width.

Continue with [nested arrays](../examples/recipes/nested-array.md),
[fixed text](../examples/recipes/fixed-text.md), or [choosing an API](choosing-an-api.md). The starter's third
file, `Generated.cs`, does the same four steps with a `[CStructLayout]` class the compiler generates:
[your first generated layout](generated/first-generated-layout.md).
