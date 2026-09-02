---
title: Binary layout basics
description: Learn how bytes, offsets, widths, byte order, and padding relate to a CStructSharp layout.
---

# Binary layout basics

Binary data is stored as bytes rather than as C# objects or text. A format specification tells you what each byte
means. For example, a device may send six bytes where the first two identify the message type and the next four give
the message length:

```text
offset       0     1     2     3     4     5
bytes       02    00    06    00    00    00
meaning     kind────    length────────────────
```

An *offset* is a byte's position measured from the start of the data. Offset 0 is the first byte. A field's *width*
is the number of bytes it occupies. Here, `kind` starts at offset 0 and is two bytes wide; `length` starts at offset 2
and is four bytes wide.

CStructSharp uses a layout to give these byte ranges names and types:

```c
struct header {
    uint16 kind;
    uint32 length;
};
```

This looks like a C struct, but it describes stored data rather than live process memory. `uint16` always means an
unsigned two-byte integer, and `uint32` always means an unsigned four-byte integer.

## Byte order

When an integer occupies more than one byte, the format must say which byte comes first. This is called *byte order*
or *endianness*.

The value `0x1234` needs two bytes:

```text
little-endian: 34 12
big-endian:    12 34
```

Little-endian stores the least significant byte first. Big-endian stores the most significant byte first.
CStructSharp's default layout byte order is little-endian. You can choose big-endian for the whole layout or use `<`
and `>` on individual numeric types when a format mixes byte orders.

Byte order changes how a value is encoded, but it does not change the field's width or position.

## Packed and aligned placement

The next question is where each field begins.

In *packed* placement, fields follow one another with no unused bytes. Packed placement is the CStructSharp default.
The six-byte `header` therefore places `length` immediately after `kind`, at offset 2.

Some formats use *aligned* placement. Alignment moves a field to an offset that is a multiple of that field's
alignment. For the same header, `uint32 length` has alignment 4, so aligned placement inserts two padding bytes:

```text
packed, 6 bytes                 aligned, 8 bytes
02 00 06 00 00 00              02 00 00 00 06 00 00 00
kind  length                    kind  pad   length
```

*Padding* is unused space added to place a later field at the required offset. You must get this choice from the
binary format you are reading. Do not copy the layout of a native C compiler and assume it will be identical.

## What “compile the layout” means

When you construct a [`CStruct`](xref:CStructSharp.CStruct), the library reads the layout text, checks its names and
types, calculates known sizes and alignments, and prepares the rules needed by later operations. The documentation
calls this *compiling the layout*. It does not create machine code or require a C compiler.

Create the `CStruct` once and reuse it for data that follows the same format:

```csharp
var layout = new CStruct(
    "struct header { uint16 kind; uint32 length; };",
    pointerSize: 8,
    aligned: false,
    isLittleEndian: true);
```

The arguments describe the file or message format:

- `pointerSize` is the number of bytes used by pointer fields. It has no effect on this pointer-free header.
- `aligned: false` selects packed placement.
- `isLittleEndian: true` selects little-endian order for numeric types without a `<` or `>` suffix.

The defaults have those same values, but passing them explicitly is useful when the layout describes a persisted
format. A future reader can then see the format choices without looking up constructor defaults.

## Check the example by hand

For bytes `02 00 06 00 00 00`:

1. Read `kind` from offsets 0 and 1: little-endian `02 00` is `2`.
2. Read `length` from offsets 2 through 5: little-endian `06 00 00 00` is `6`.
3. Confirm the packed field widths add up to six bytes.

If CStructSharp produces different values, first check the byte order, placement mode, field widths, and starting
offset. Those four mistakes explain many first parsing failures.

## What to learn next

Continue with [Install and make a first parse](install-and-first-parse.md) to run this layout from C#. The
[layout-language tutorial](../language/tutorial/index.md) then adds nested structures, arrays, strings, unions, and
runtime-sized data.
