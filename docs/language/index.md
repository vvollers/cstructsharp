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

The same text is consumed in two places: at run time by `new CStruct(text)`, which compiles it when the program
runs, and at compile time by the `[CStructLayout]` source generator, which turns it into C# classes while the
program is built ([generated code](../guides/generated/index.md)). Both use the same parser and the same
placement rules, so a layout means the same bytes wherever it is read.

The rules are called *Portable* because they do not change with the operating system, CPU, installed C compiler, or
.NET process bitness. `uint32` is always four bytes. `long` is always eight bytes. Pointer width is an explicit
constructor choice.

## It looks like C, but it is not a C compiler

Portable accepts structs, unions, enums, aliases, arrays, strings, bitfields, pointers, integer expressions, and the
directives a header needs: `#define` constants, `#ifdef`/`#ifndef`/`#else`/`#endif` selection, `#pragma pack`
as a composite alignment clamp, and `#include` lines that are recorded rather than resolved. The vocabulary of
Windows SDK and Linux kernel headers (`DWORD`, `__u32`, `typedef struct _X { ... } X, *PX;`) is built in, and a
function-pointer declarator is stored as an opaque address. What it does not do:

- resolve `#include` paths, expand function-like macros, or run any other preprocessing beyond the directives
  above;
- give a primitive a platform-dependent width - `long` is `CLongWidth`'s explicit choice, `uint32` is always four
  bytes, and pointer width is a constructor argument;
- honor compiler attributes such as `__attribute__((packed))` or `alignas` - placement is chosen by the
  constructor's `aligned` flag, `#pragma pack`, `@align(N)`, and `BitfieldPacking` (`SysV` or `Msvc`);
- declare functions, or dereference a function pointer; or
- detect the host ABI - every rule is an explicit option, so a layout means the same thing on every machine.

If you are translating a C header, first find the actual on-disk or on-wire format. Then express those fixed widths
and positions in Portable syntax. [Differences from C](differences-from-c.md) lists every intentionally unsupported
family and compares Portable placement with real compilers.

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
