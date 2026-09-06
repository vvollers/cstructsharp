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

`Serialize` allocates a new byte array and writes the fields according to the layout. `Header` is an ordinary C#
class defined at the bottom of this program. Its `ushort` property matches the two-byte unsigned `uint16` field;
its `uint` property matches `uint32`. These property names have an unambiguous case-insensitive match to the layout.

## Change existing bytes

`UpdateStream` selects `header.kind` and writes the replacement in its existing two-byte space. It leaves the four
length bytes alone. A stream is an object that supports reading or writing bytes; `MemoryStream` keeps those bytes
in memory and lets the library move to the selected position. An update cannot insert space or move later fields.

## Read into a class and handle missing bytes

`ReadValue<Header>` gives application code typed properties. `AsSpan()` chooses a view over the byte array without
copying it. It also selects the same overload on .NET 8 and .NET 10.

The last read has only one byte. `TryReadValue` returns `false` for this expected library failure. Invalid method
arguments can still throw; it is not a way to suppress every programming error.

## Try a change

Change the replacement kind to `65535`. Answer: the first two updated bytes are `FF FF`. Try `65536` next.
Answer: the replacement does not fit `uint16`, so the update fails. Restore a valid value rather than changing the
field width unless the actual format calls for a different width.

Continue with [nested arrays](../examples/recipes/nested-array.md),
[fixed text](../examples/recipes/fixed-text.md), or [choosing an API](choosing-an-api.md).
