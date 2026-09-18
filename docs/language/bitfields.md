---
title: Portable bitfields
description: Pack named unsigned values into explicit integer storage with predictable bit positions.
---

# Portable bitfields

A bitfield stores several small integer values inside one primitive storage unit. Portable bitfields use explicit storage-unit rules,
with low-bit-first allocation by default and an optional high-bit-first setting.

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

Adjacent bitfields of the *same* declared size share one storage unit while the next field fits and no ordinary
field interrupts the run; an ordinary field begins after the storage the run used. What happens when the declared
size changes is where C compilers disagree, and `CStructCompilationOptions.BitfieldPacking` picks the rule:

| `BitfieldPacking` | Rule | `uint8 a:4; uint16 b:4;` | Matches |
| --- | --- | --- | --- |
| `SysV` (default) | Bits are allocated contiguously from the struct start. With aligned placement a field joins the run while it stays inside one type-aligned cell of its own declared size (it moves to the next cell otherwise); packed placement never moves it. The storage unit is the cell that holds the bits, trimmed to the bytes the run actually uses. | `AF 00` aligned, `AF` packed | GCC and Clang on every System V target (x86-64, ARM64, RISC-V, ...), the Itanium C++ ABI |
| `Msvc` | A new unit of the declared size starts whenever the declared size changes or the field no longer fits; whole units are kept. | `0F 00 0A 00` aligned, `0F 0A 00` packed | Microsoft Visual C++, and dissect.cstruct |

Both rules are part of the compiled-layout cache key. A field that would need a packed SysV window wider than eight
bytes (a 64-bit field starting mid-byte) is rejected at compile time with the remedies; aligned placement never
produces one.

An unnamed zero-width declarator, `uint16 : 0;`, is a separator: it stores nothing and appears in no result, and
the next bitfield starts on the next boundary of the separator's type (a whole new unit under `Msvc`). Under `SysV`
its type does not raise the struct's alignment, as on x86-64; under `Msvc` it does.

Bit numbering is independent of placement: `BitfieldAllocation` counts from the low or the high bit of the storage
unit. Real compilers pair little-endian units with low-bit-first numbering and big-endian units with high-bit-first;
in the other two pairings the bits fill a unit from its last byte, so SysV placement keeps whole declared cells
(a field that would cross its cell moves to the next one) and never trims a unit.

The `portable-bitfields` fixture checks `low=5`, `high=17`, `next=4660`, offsets, size 3, alignment 2, and bytes
`8D 34 12` on both frameworks. `BitfieldPackingTests` checks twenty-three mixed-size shapes against bytes recorded
from GCC in both placements, and the MSVC rule for the shapes where the two differ.

### Allocating from the high bit

Formats specified by big-endian ABIs or RFC bit diagrams number the first field from the most significant bit.
`CStructCompilationOptions { BitfieldAllocation = BitfieldAllocation.HighBitFirst }` selects that reading (it is
also what dissect.cstruct does under a big-endian layout): with `uint16 a : 4; uint16 b : 12;` the big-endian bytes
`12 34` give `a = 1`, `b = 0x234` instead of the low-bit-first `a = 4`, `b = 0x123`. Storage-unit grouping, unit
sizes, offsets, and byte order are unaffected; only each field's position inside its unit changes, so every
operation (read, write, update, typed read, address) agrees. The option is part of the compiled-layout cache key.
The `bitfield-allocation` fixture checks both readings of the same bytes.

## Storage types and byte order

Storage must resolve to a supported fixed integer codec in the [primitive table](primitive-types.md).
Built-in aliases, integer typedefs, enum/flag backing types, `char`, `wchar`, and explicit-endian variants are accepted.
Pointer, array, struct, union, floating-point, and terminated-string storage are rejected.

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

## Unnamed padding fields

A declarator may omit its name and keep only a nonzero bit width, reserving storage as pure padding:

```c
struct flags {
    uint8 low:3;
    uint8 :2;
    uint8 high:3;
};
```

The two middle bits are consumed from the shared storage unit exactly like any other bitfield slice, but never
become an addressable path, a POCO/`StructValue` member, or a JSON field - there is nothing to read, write, or
resolve a path to. New output (`Write`, `Serialize`) always writes zero for those bits, the same way a
struct's own tail padding is zero-filled; an update to a named sibling in the same storage unit changes only that
sibling's own bit range and leaves the padding untouched. Debug output still reports the padding's byte/bit range,
since it is still storage worth inspecting even though it has no name. `@align(N)` is accepted on an anonymous declarator the
same way it is on a named one; `@N` is rejected the same way it already is on every bitfield declarator, named or
not.

A whole field can be anonymous from its own declaration, not only a middle slice of a comma-separated list:

```c
struct reserved_byte {
    uint8 :3;
};
```

**The anonymous type must be a single word** (`uint8`, `uint16_t`, and so on). A multi-word type
(`unsigned int :3;`) is not rejected outright - it falls back to the existing "last word is the name" rule that
already applies to every multi-word type, naming the field after its last word (`unsigned int :3;` declares a field
named `int` of type `unsigned`, not an anonymous `unsigned int`). Only a single captured word before the bit width
is unambiguous enough to mean "this word is the whole type, and there is no name."

The `anonymous-bitfield-padding` fixture checks `flag=1`, `other=5`, size 1, and bytes `51` on both frameworks.

## Unsupported forms

| Form | Result |
| --- | --- |
| Zero-width separator (`uint8 reserved : 0`) | `InvalidLayout` |
| Array, pointer, floating-point, or composite storage | `InvalidLayout` |
| Width larger than the storage type | `InvalidLayout` |
| Native signed-field projection or compiler allocation rules | Not inferred |
| Native compiler attributes or ABI profiles | Unsupported |

Native C bitfield layout is implementation-defined. Portable may intentionally produce different bytes from GCC,
Clang, or MSVC. Compiler-comparison fixtures record selected differences but do not select a native allocation rule automatically.

When bytes are wrong, check storage type identity, bit width, group breaks, byte order, and whether the source header
relied on a native compiler convention. See [Differences from C](differences-from-c.md).
