---
title: Portable bitfields
description: Pack named unsigned values into explicit integer storage with predictable bit positions.
---

# Portable bitfields

A bitfield stores several small integer values inside one primitive storage unit. Portable bitfields use a fixed
low-bit-first rule so their result does not depend on a native C compiler.

```c
struct flags {
    uint8 low : 3;
    uint8 high : 5;
    uint16 next;
};
```

The number after `:` is the field's width in bits. It must be a checked compile-time expression between 1 and the
storage type's bit capacity.

## How fields share storage

Allocation begins at the least significant bit:

```text
storage byte 0 = 0x8D = binary 10001 101
bit index                    7           3 2       0
field                        high=17       low=5

offset   0                  1    2
bytes   8D                 34   12
fields  low/high─────────  next────────
```

`low` takes bits 0 through 2 and reads as 5. `high` takes bits 3 through 7 and reads as 17. Both fields have byte
offset 0 and the same one-byte debug range. In packed little-endian placement, `next` starts at offset 1 and reads
`34 12` as 4660.

Adjacent bitfields share a storage unit only while:

- their direct declared storage codec is the same;
- the next field fits in the remaining bits; and
- no ordinary field interrupts the group.

A type change, full unit, overflow into another unit, or ordinary field starts new storage. In aligned mode, a new
unit begins at the alignment required by its codec. An ordinary field begins after the complete unit, including any
unused high bits.

The `portable-bitfields` fixture checks `low=5`, `high=17`, `next=4660`, offsets, size 3, alignment 2, and bytes
`8D 34 12` on both frameworks.

## Storage types and byte order

Storage must be one of the 31 direct fixed primitive spellings in the [primitive table](primitive-types.md), including
the built-in signed/unsigned aliases, `char`, `wchar`, and explicit-endian variants. A user typedef, enum, pointer,
array, struct, union, floating-point name, or terminated string is rejected even if it would eventually have an
integer width.

The storage unit is sliced as an unsigned number. A signed primitive does not make an individual bitfield
signed. Results below 32 bits are non-negative `Int32`; widths from 32 through 64 use an unsigned value that keeps
every bit.

For a two-byte storage number:

```text
uint16 value 0xA5D5
little-endian bytes: D5 A5       big-endian bytes: A5 D5
first:3  = bits 0..2  = 5        same field value
center:5 = bits 3..7  = 26       same field value
last:8   = bits 8..15 = 165      same field value
```

Byte order changes how the complete storage number maps to bytes. It does not reverse the low-bit-first allocation.

## Read, write, and update

Parsing and selected reads return each declared slice as an unsigned value. Debug and address operations identify the
whole shared storage unit.

Serialization merges all declared slices into that unit. A written value must be a non-null integer from zero
through `(2^width - 1)`. Negative and overflowing values produce `WriteFailed`.

An update first reads the complete existing unit, changes only the selected mask, and preserves neighboring and
unused bits. The same rule applies to a bitfield viewed through a union.

## Unsupported forms

| Form | Result |
| --- | --- |
| Unnamed field (`uint8 : 3`) | `InvalidLayout` |
| Zero-width separator (`uint8 reserved : 0`) | `InvalidLayout` |
| Array, pointer, typedef, enum, or composite storage | `InvalidLayout` |
| Width larger than the storage type | `InvalidLayout` |
| Native signed-field projection or compiler allocation rules | Not inferred |
| `#pragma pack`, attributes, or compiler profiles | Unsupported |

Native C bitfield layout is implementation-defined. Portable may intentionally produce different bytes from GCC,
Clang, or MSVC. Compiler-comparison fixtures record selected differences but do not change the low-bit-first rule.

When bytes are wrong, check storage type identity, bit width, group breaks, byte order, and whether the source header
relied on a native compiler convention. See [Differences from C](differences-from-c.md).
