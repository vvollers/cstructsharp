---
title: How C structs occupy memory
description: Understand native C objects, padding, array stride, ABIs, and why a memory dump is not a portable file format.
---

# How C structs occupy memory

A CStructSharp layout resembles C because both describe a group of named fields. Their purposes differ:
a native C declaration describes an object used by a compiled program; a Portable layout describes bytes in a
file, message, or buffer. This chapter explains the connection. Start with [binary layout basics](binary-layout-basics.md)
if hexadecimal numbers or byte offsets are new to you.

## A declaration is a type, not an allocation

This is **native C**, including its standard header, rather than CStructSharp syntax:

```c
#include <stdint.h>

struct sample {
    uint8_t kind;
    uint32_t length;
    uint16_t flags;
};

struct sample first;
struct sample records[2];
```

The declaration ending at `};` defines the type. `first` is one object of that type; `records` contains two.
Field names are how the source program refers to storage. The object bytes do not include the words `kind`,
`length`, or `flags`, and do not carry a schema for a reader to discover.

In C, ordinary struct members retain declaration order; the implementation may insert padding between members
and at the end, but not before the first member. Arrays store their elements consecutively. `sizeof` includes
padding. These are language rules; exact offsets need the compiler's target rules too. The C11 committee draft
describes them in sections 6.2.5, 6.5.3.4, and 6.7.2.1. [C11 draft N1570](https://www.open-std.org/jtc1/sc22/wg14/www/docs/n1570.pdf)

Declaring fields does not initialize them automatically in every context. Give local objects explicit initial
values before using their fields. Also distinguish field values from padding: initializing fields is not a
portable promise that every padding byte has a useful, repeatable value.

A C byte is the unit measured by `sizeof(char)`; C does not universally require eight-bit bytes.
CStructSharp and these file examples use eight-bit bytes. The exact-width C types from `stdint.h` are available
when the implementation can provide the stated representation. Use this example on a target that provides them.

Assigning one native struct to another copies its member values, including any pointer values. It does not
recursively duplicate the objects those pointers refer to. This is why a struct containing an inline array and
one containing a pointer are not interchangeable descriptions of a saved record.

## Work out one concrete layout

For this example, assume eight-bit bytes, integer widths matching the names, natural alignments of 1, 4, and 2,
and a struct alignment of 4. These assumptions describe a common native layout and also this example under
CStructSharp's `aligned: true` rules. They are not a universal C guarantee.

| Part | Start offset | Bytes | Reason |
| --- | ---: | ---: | --- |
| `kind` | 0 | 1 | First field |
| Padding | 1 | 3 | Move the next field to a multiple of 4 |
| `length` | 4 | 4 | Four-byte field |
| `flags` | 8 | 2 | Offset 8 is already a multiple of 2 |
| Tail padding | 10 | 2 | Make the whole object size a multiple of 4 |

The size is **12**, not the sum of the field widths, **7**. With base address `0x1000`, `length` is at
`0x1004`. An address is a location; an offset is a distance from a chosen base.

For a current offset `p` and alignment `a`, the next aligned offset is:

```text
padding = (a - (p mod a)) mod a
next offset = p + padding
```

After `kind`, `p = 1` and `a = 4`, so padding is 3. After `length`, `p = 8` and `a = 2`, so padding is 0.
The second modulo prevents adding an entire alignment unit when the position is already aligned.

### Tail padding makes arrays work

The distance between array elements is called the **stride**. For `records`, the stride is 12:

```text
array offset    0         4       8   10  12        16      20  22  24
                | kind pad |length|flags pad|kind pad|length|flags pad|
element         |--------- records[0] ----|--------- records[1] ----|
```

The two `length` fields begin at array offsets 4 and 16, both multiples of 4. Without tail padding, the next
record would begin at 10 and its `length` at 14, breaking the alignment assumption. Tail padding therefore
belongs to every array element, including the last. The two-record array occupies 24 bytes.

A nested struct similarly occupies its complete size inside its parent. An array member contains its elements
inline; a pointer member contains a pointer, with its target elsewhere. Those two declarations do not have the
same storage cost.

### Why processors care about alignment

Hardware performs loads and stores using supported widths. An access at an unsuitable address may require extra
work, cross a hardware boundary, or be unsupported by an instruction. Some architectures handle unaligned
accesses directly; others trap or require software assistance. It is inaccurate to say that every unaligned read
crashes, or that alignment never matters on modern machines. The Linux kernel's
[unaligned-access guide](https://cdn.kernel.org/doc/html/latest/core-api/unaligned-memory-access.html)
explains these differences.

Packing saves storage by reducing gaps, but native code must still access packed fields correctly. A compiler
extension can change layout and access instructions together. Simply casting an arbitrary byte-buffer address to
`struct sample *` does not solve alignment, bounds, object lifetime, byte order, or C's rules for accessing objects
through different types. CStructSharp decodes the bytes according to the layout instead of treating the input as a
native struct in memory.

## Measure a native compiler instead of guessing

Use this complete C program to inspect the compiler and target you actually use:

```c
#include <stddef.h>
#include <stdint.h>
#include <stdio.h>

struct sample {
    uint8_t kind;
    uint32_t length;
    uint16_t flags;
};

int main(void) {
    printf("size=%zu alignment=%zu\n", sizeof(struct sample), _Alignof(struct sample));
    printf("kind=%zu length=%zu flags=%zu\n",
           offsetof(struct sample, kind),
           offsetof(struct sample, length),
           offsetof(struct sample, flags));
    return 0;
}
```

Save it as `layout.c`. With a C11-capable GCC or Clang installation, compile with
`gcc -std=c11 -Wall -Wextra layout.c -o layout`, or substitute `clang`. Run `./layout` on Unix-like systems or
`.\layout.exe` in PowerShell on Windows. With the assumptions above it prints:

```text
size=12 alignment=4
kind=0 length=4 flags=8
```

This is an observation of that build, not a file-format specification. Compiler options, target, or packing
directives can change it. Microsoft's [struct storage documentation](https://learn.microsoft.com/en-us/cpp/c-language/storage-and-alignment-of-structures)
gives another implementation-specific account of this distinction.

## Why Windows and Unix-like systems can disagree

An **application binary interface**, or ABI, specifies the details that separately compiled native code must agree
on, including type sizes, alignment, and function calling rules. The operating system alone is not the whole ABI:
the processor target, compiler conventions, and build options also matter.

In particular, “64-bit” does not mean every integer type occupies eight bytes:

| Common native data model | `int` | `long` | Pointer | Typical use |
| --- | ---: | ---: | ---: | --- |
| ILP32 | 32 bits | 32 bits | 32 bits | Many 32-bit environments |
| LP64 | 32 bits | 64 bits | 64 bits | Many 64-bit Unix-like environments |
| LLP64 | 32 bits | 32 bits | 64 bits | 64-bit Windows |

The [Solaris data-model guide](https://docs.oracle.com/cd/E19455-01/806-0477/6j9r2e2as/index.html) describes ILP32 and LP64.

Microsoft retained 32-bit `long` in the Windows LLP64 model partly to preserve existing data layouts while expanding
pointers. This illustrates why legacy compatibility matters: changing an integer width can change existing files,
network records, and shared-memory structures. [Windows data-model rationale](https://learn.microsoft.com/en-us/windows/win32/winprog64/abstract-data-models)

CStructSharp instead gives `long` a fixed eight-byte meaning. Its pointer width is an explicit constructor argument,
unrelated to the .NET process architecture. When translating a header, prefer names such as `uint32` and `uint64`
after checking the actual format. The [primitive reference](../language/primitive-types.md) defines the accepted aliases.

## Structs, unions, and bitfields solve different problems

A struct reserves successive storage for its fields. A union overlaps member storage: it can hold a representation
large enough for its largest member. Merely declaring a union does not add a discriminator telling a reader which
interpretation is meaningful. A surrounding format commonly supplies a `kind` field for that purpose.

CStructSharp's `UnionValue` keeps raw storage and decoded member views so an inspector can examine overlapping
interpretations. That does not mean a native C program has several independently stored union values. Follow the
[union guide](unions.md) when preserving bytes or selecting a member to write.

Bitfields divide a storage unit into bit slices. Native C leaves important allocation details to the implementation,
including bit allocation order. CStructSharp explicitly allocates compatible slices from the low bit upward.
Byte endianness and bitfield allocation order are separate questions. The
[Portable bitfield rules](../language/bitfields.md) include worked examples; do not infer those rules from a native
C declaration alone.

## Why writing a struct directly to disk is fragile

A native call such as `fwrite(&value, sizeof value, 1, file)` writes an object representation. It does not create
a portable schema. A later reader still needs to know:

- member widths and offsets, including padding;
- integer byte order and floating-point representation;
- character encoding and string capacity or termination;
- which union member is meaningful;
- whether a stored number is a value, an offset, or a process pointer; and
- which format version these choices belong to.

Two objects can have equal field values and different padding bytes, so byte comparison is not generally struct
value comparison. Padding can also expose bytes that the application never intended to publish. Treat reserved
file bytes as an explicit format rule rather than relying on unspecified native padding.

For the sample, a Portable declaration uses `uint8`, `uint32`, and `uint16` in place of the C header's types.
With default packed placement its size is 7; with `aligned: true` it is 12. Neither choice is automatically correct
for an existing file. Choose from its specification or verified producer behavior. Then test known input bytes.

The practical benefit is that the **same bytes and explicit options have the same meaning** on supported hosts.
This also makes old formats usable after their original hardware has disappeared. Continue with
[memory addresses and stored data](memory-and-stored-data.md), then consult
[differences from C](../language/differences-from-c.md) when translating declarations.

## Check your understanding

1. Under the stated alignment rules, reorder the fields to `length`, `flags`, `kind`. What is the new size?
2. Does changing little-endian to big-endian remove any padding?
3. Does a pointer member include the bytes of the pointed-to object?

Answers: **8 bytes** (one tail-padding byte); **no**, byte order only changes encoding within fields; **no**,
the pointer and its target are separate storage. Reordering may save native memory, but it changes a persisted
format, so it is not a safe optimization for an existing file schema.
