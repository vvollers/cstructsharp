---
title: The Portable layout language
description: Learn the C-like language CStructSharp uses to map named values to exact bytes.
---

# The Portable layout language

CStructSharp needs a description of the binary format before it can read or write values. That description is a
small domain-specific language: a language built for one job. Here, the job is mapping fields to bytes.

The syntax is intentionally familiar to C developers:

```c
struct header {
    uint16 kind;
    uint32 length;
};
```

This says that `header` contains a two-byte unsigned integer followed by a four-byte unsigned integer. The
constructor chooses packed or aligned placement and the byte order used by neutral multi-byte fields.

The rules are called *Portable* because they do not change with the operating system, CPU, installed C compiler, or
.NET process bitness. `uint32` is always four bytes. `long` is always eight bytes. Pointer width is an explicit
constructor choice.

## It looks like C, but it is not a C compiler

Portable accepts structs, unions, enums, aliases, arrays, strings, bitfields, pointers, integer expressions, and a
small `#define` form. It does not import arbitrary headers or implement:

- `#include`, `#pragma`, or general preprocessing;
- platform-dependent primitive widths;
- compiler-specific packing, attributes, or bitfield allocation;
- functions or function pointers; or
- automatic host ABI detection.

If you are translating a C header, first find the actual on-disk or on-wire format. Then express those fixed widths
and positions in Portable syntax. [Differences from C](differences-from-c.md) lists every intentionally unsupported
family.

## Learn in this order

The [three-part tutorial](tutorial/index.md) starts with a six-byte header and then adds:

1. widths, byte order, packed placement, and a first parse;
2. enums, fixed text, unions, and overlapping storage; and
3. runtime array counts, selected paths, stored pointers, and safety limits.

After the tutorial, choose a reference by task:

- [Primitive types](primitive-types.md) for widths, ranges, byte order, and C# result types.
- [Structs, unions, enums, and typedefs](structs-unions-enums-typedefs.md) for declarations.
- [Arrays, character buffers, and strings](arrays-and-strings.md) for fixed and runtime-sized data.
- [Layout, alignment, and padding](layout-alignment-and-padding.md) for exact offsets.
- [Paths and selection](paths-and-selection.md) for reading or updating nested data.
- [Limits and diagnostics](limits-and-diagnostics.md) for safe failure handling.
- [Complete grammar](grammar.md) for the full Portable binary layout grammar.
- [Cookbook](cookbook/index.md) for short format patterns.

The [Portable v1 rules](portable-v1-reference.md) explain the versioned behavior and how its examples are checked.
The [feature table](operation-matrix.md) shows which layout features work with parsing, debugging, addresses, lengths,
serialization, writes, updates, and selected reads.

The language pages explain byte-level behavior. Use the [library guides](../guides/index.md) to choose a C# method and
understand streams, buffers, ownership, and application errors.
