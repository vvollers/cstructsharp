---
title: Tutorial 1 — your first fixed layout
description: Map a packed little-endian six-byte header and read it from C#.
---

# Tutorial 1 — your first fixed layout

Suppose a binary message begins with a two-byte kind and a four-byte length:

```text
02 00 06 00 00 00
```

The layout names those two values:

```c
struct header {
    uint16 kind;
    uint32 length;
};
```

`uint16` is always two bytes, and `uint32` is always four. The fields have no initial values in the layout; the
declarations describe how to interpret bytes supplied later.

## Step 1: choose placement and byte order

CStructSharp's defaults are:

- packed placement (`aligned: false`);
- little-endian neutral numeric fields (`isLittleEndian: true`); and
- eight-byte pointer storage (`pointerSize: 8`).

This layout has no pointer, so pointer width does not affect its size.

Packed placement puts `length` immediately after `kind`:

| Offset | Bytes | Field | Decoded value |
| ---: | --- | --- | ---: |
| 0 | `02 00` | `header.kind` | `2` |
| 2 | `06 00 00 00` | `header.length` | `6` |

The complete size is six bytes. In aligned placement, the four-byte `length` would start at offset 4, leaving two
padding bytes after `kind`, and the struct would occupy eight bytes.

## Step 2: construct and reuse the layout

The documentation runner compiles and executes this complete scenario:

[!code-csharp[Read the header dynamically and as a C# class](../../examples/Program.cs#api-reference-cstruct)]

`new CStruct(...)` reads and prepares the layout once. `Parse(bytes, "header")` returns a dynamic object whose field
names come from the layout. `TryReadValue<Header>` maps the same bytes to a C# class and returns `false` for the
deliberately truncated one-byte input.

Expected results:

```text
kind   = 2
length = 6
typed read succeeds = true
truncated read succeeds = false
```

## Step 3: verify a byte-order change

Change only the field declaration to:

```c
uint32> length;
```

The `>` suffix forces big-endian storage for that field. The length bytes must then be `00 00 00 06`. Its width and
offset do not change; only the order of its four bytes changes.

If the original bytes produce a very large length, check whether the format is actually little-endian. If reading
fails, confirm that the source contains all six packed bytes and that the root name is exactly `header`.

The [primitive table](../primitive-types.md) lists all fixed widths and suffixes. Continue with
[Tutorial 2 — composites and overlapping storage](02-composites-and-layout.md).
