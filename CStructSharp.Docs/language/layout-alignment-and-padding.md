---
title: Layout, alignment, and padding
description: Calculate field offsets, struct sizes, array strides, union storage, and byte order.
---

# Layout, alignment, and padding

Layout answers three related questions:

- Where does each field begin?
- How many bytes does the complete value occupy?
- In what order are multi-byte values stored?

Packed/aligned placement controls positions. Byte order controls encoding inside a multi-byte field. They are
independent choices.

## Alignment and endian

With `aligned: false` (the default), each ordinary field begins where the previous field ended.

With `aligned: true`, a field begins at the next offset divisible by its Portable alignment. The final struct size is
rounded to the largest field alignment. A nested struct occupies its complete padded size, and an array repeats that
complete size as its stride.

For:

```c
struct sample {
    uint8 a;
    uint32 b;
    uint16 c;
};
```

```text
packed (size 7)                 aligned (size 12, alignment 4)
offset 0 1 2 3 4 5 6           offset 0 1 2 3 4 5 6 7 8 9 10 11
field  a b────── c──           field  a pad── b────── c── pad──
bytes 11 55 44 33 22 77 66     bytes 11 00 00 00 55 44 33 22 77 66 00 00
```

In aligned mode, `b` moves from offset 1 to 4. `c` begins at 8. After `c`, two tail-padding bytes make the complete
size divisible by the struct alignment of 4.

Neutral multi-byte fields follow `isLittleEndian`. `<` forces little-endian for one supported primitive and `>`
forces big-endian:

```text
uint16< value = 0x1234  → 34 12
uint16> value = 0x1234  → 12 34
```

Changing byte order does not change width, alignment, field order, offset, or array stride.

## Placement algorithm

To calculate a layout:

1. Find each field alignment: primitive table value, configured pointer width, maximum child alignment for a
   composite, or one for a terminated value.
2. In packed mode, use the current byte. In aligned mode, round it up to the field alignment.
3. Add the field's complete storage. For an array, repeat the complete element stride.
4. A struct's alignment is its maximum field alignment. Packed size ends after the last field; aligned size is rounded
   to that struct alignment.
5. Every union member begins at offset zero. Union size is the largest complete member storage, rounded to the union
   alignment only in aligned mode.
6. Adjacent compatible bitfields share one storage unit until the next slice no longer fits. Ordinary placement
   resumes after the full unit.

An unsized array is allowed only for supported character types, where it means terminated text. A general unsized
array or variable-size union member does not have a fixed amount of storage, so it is rejected.

## Checked layout examples

The rows below come from [`portable-v1.json`](../contracts/language/portable-v1.json) and execute on both frameworks.
Padding in newly serialized output is zero. Offsets are relative to the root.

| Fixture | Definition / values | Options | Expected offsets | Size / alignment | Expected bytes |
| --- | --- | --- | --- | ---: | --- |
| `packed-mixed` | `struct sample { uint8 a; uint32 b; uint16 c; };` with `a=0x11`, `b=0x22334455`, `c=0x6677` | little, packed | `a=0`, `b=1`, `c=5` | 7 / 4 | `11 55 44 33 22 77 66` |
| `aligned-mixed` | Same definition and values | little, aligned | `a=0`, `b=4`, `c=8` | 12 / 4 | `11 00 00 00 55 44 33 22 77 66 00 00` |
| `aligned-nested-array` | `inner { uint8 tag; uint32 value; }`; `root { uint16 prefix; inner items[2]; uint8 tail; }` | little, aligned | `prefix=0`; tags `4,12`; values `8,16`; `tail=20` | 24 / 4 | `34 12 00 00 A1 00 00 00 44 33 22 11 A2 00 00 00 88 77 66 55 EE 00 00 00` |
| `aligned-union` | `union choice { uint8 small; uint32 large; };`, selected `small=0xA5` | little, aligned | both members `0` | 4 / 4 | `A5 00 00 00` |
| `explicit-endian` | `struct endian_sample { uint16> be; uint16< le; wchar> ch; };` with `0x1234`, `0x5678`, `A` | global little, packed | `be=0`, `le=2`, `ch=4` | 6 / 2 | `12 34 78 56 00 41` |
| `portable-bitfields` | `uint8 low:3=5; uint8 high:5=17; uint16 next=0x1234;` | little, packed | `low=0`, `high=0`, `next=1` | 3 / 2 | `8D 34 12` |
| `packed-null-pointer` | `struct pointer_sample { uint8 marker; uint16 *target; };`, null pointer | 2-byte pointer, little, packed | marker `0`, pointer `1` | 3 / 2 | `AA 00 00` |
| `aligned-null-pointer` | Same definition and values | 2-byte pointer, little, aligned | marker `0`, pointer `2` | 4 / 2 | `AA 00 00 00` |

`GetStructAlignmentInBytes` returns the alignment column even in packed mode. `GetStructSizeInBytes` works only when
the selected struct/union has a fixed extent. Runtime arrays and terminated fields need operation variables or actual
input/output to determine size.

Padding read from input is not a named value and is not guaranteed to survive a read/write round trip. New output
uses zero padding. `UnionValue` is the exception because it explicitly stores the complete raw union region.

## Layout preparation limits

`CStructCompilationOptions` defaults to:

- 128 KiB maximum source length;
- 256 levels of layout nesting;
- 256 levels of expression/dependency depth; and
- 100,000 expression nodes/evaluation steps.

`MaxExpressionTokens` keeps its public name for compatibility but counts expression work rather than lexer tokens.
Every limit must be positive. The constructor copies the values; changing an options variable later does not alter an
existing layout.

Increase a limit only for a known trusted layout source. For details, continue with
[arrays and strings](arrays-and-strings.md), [bitfields](bitfields.md),
[declarations](structs-unions-enums-typedefs.md), or [pointers](pointers-and-addressing.md).
