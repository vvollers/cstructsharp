---
title: Primitive types
description: Choose an exact-width integer, character code unit, or terminated-string encoding.
---

# Primitive types

A primitive type is a value CStructSharp can convert directly between bytes and one CLR value. Use the type whose
width, signedness, and byte order match the binary format.

For multi-byte types, *layout order* means the `isLittleEndian` value passed to the `CStruct` constructor. A `<`
suffix always means little-endian, and `>` always means big-endian.

## Fixed primitives

| Accepted spelling | Internal codec | Bytes / alignment | Values accepted by the writer | Byte order | Direct CLR result |
| --- | --- | ---: | --- | --- | --- |
| `byte`, `uint8` | `uint8` | 1 / 1 | Unsigned, 0..255 | Not applicable | `Byte` |
| `int8` | `int8` | 1 / 1 | Signed, -128..127 | Not applicable | `SByte` |
| `char` | `char` | 1 / 1 | Raw code unit, U+0000..U+00FF | Not applicable | `Char` |
| `wchar` | `wchar` | 2 / 2 | UTF-16 code unit, U+0000..U+FFFF | Layout | `Char` |
| `wchar<` | `wchar<` | 2 / 2 | UTF-16 code unit, U+0000..U+FFFF | Little | `Char` |
| `wchar>` | `wchar>` | 2 / 2 | UTF-16 code unit, U+0000..U+FFFF | Big | `Char` |
| `int16`, `int16<`, `int16>` | Matching `int16` codec | 2 / 2 | Signed, -32768..32767 | Layout / little / big | `Int16` |
| `uint16`, `uint16<`, `uint16>` | Matching `uint16` codec | 2 / 2 | Unsigned, 0..65535 | Layout / little / big | `UInt16` |
| `int32`, `int32<`, `int32>` | Matching `int32` codec | 4 / 4 | Signed, -2147483648..2147483647 | Layout / little / big | `Int32` |
| `uint32`, `uint32<`, `uint32>` | Matching `uint32` codec | 4 / 4 | Unsigned, 0..4294967295 | Layout / little / big | `UInt32` |
| `int64`, `int64<`, `int64>` | Matching `int64` codec | 8 / 8 | Signed, -9223372036854775808..9223372036854775807 | Layout / little / big | `Int64` |
| `uint64`, `uint64<`, `uint64>` | Matching `uint64` codec | 8 / 8 | Unsigned, 0..18446744073709551615 | Layout / little / big | `UInt64` |
| `short` | `int16` | 2 / 2 | Signed, -32768..32767 | Layout | `Int16` |
| `ushort` | `uint16` | 2 / 2 | Unsigned, 0..65535 | Layout | `UInt16` |
| `int` | `int32` | 4 / 4 | Signed, -2147483648..2147483647 | Layout | `Int32` |
| `uint` | `uint32` | 4 / 4 | Unsigned, 0..4294967295 | Layout | `UInt32` |
| `long` | `int64` | 8 / 8 | Signed, -9223372036854775808..9223372036854775807 | Layout | `Int64` |
| `ulong` | `uint64` | 8 / 8 | Unsigned, 0..18446744073709551615 | Layout | `UInt64` |

The table groups 31 accepted spellings. The complete rows are also stored in
[`portable-v1.json`](../contracts/language/portable-v1.json) and checked against the runtime on .NET 8 and .NET 10.

Alignment equals byte width for every fixed primitive. Packed placement ignores alignment when choosing the next
field position; aligned placement uses it. The alignment still appears in size/alignment queries in packed mode.

## Endian byte diagrams

The value `0x1234` is decimal 4660. Its two bytes are `0x12` and `0x34`; byte order decides which one is stored first:

| Declaration | Constructor order | Offset 0 | Offset 1 | Result |
| --- | --- | ---: | ---: | ---: |
| `uint16 value;` | little | `34` | `12` | `4660` |
| `uint16 value;` | big | `12` | `34` | `4660` |
| `uint16< value;` | either | `34` | `12` | `4660` |
| `uint16> value;` | either | `12` | `34` | `4660` |

```text
uint16< 0x1234  →  [34] [12]    least-significant byte first
uint16> 0x1234  →  [12] [34]    most-significant byte first
```

Byte order never changes the mathematical value, field width, alignment, field order, or array stride. One-byte
types have no byte-order choice.

## Terminated primitives

Terminated types scan until a NUL or line-feed marker. Their encoded size is known only while reading/writing, their
alignment is one, and the direct CLR result is `String`.

| Accepted spelling | Strict encoding | Terminator | Byte order |
| --- | --- | --- | --- |
| `ascii_string_zero`, `cstring` | ASCII | NUL | Not applicable |
| `ascii_string_newline` | ASCII | LF | Not applicable |
| `utf8_string_zero` | UTF-8 | NUL | Not applicable |
| `utf8_string_newline` | UTF-8 | LF | Not applicable |
| `unicode_string_zero`, `string` | UTF-16 | NUL | Layout |
| `unicode_string_zero<`, `string<` | UTF-16 | NUL | Little |
| `unicode_string_zero>`, `string>` | UTF-16 | NUL | Big |
| `unicode_string_newline` | UTF-16 | LF | Layout |
| `unicode_string_newline<` | UTF-16 | LF | Little |
| `unicode_string_newline>` | UTF-16 | LF | Big |

These are the 14 `terminatedPrimitives` spellings in the published JSON data. Decoding is strict. Malformed
ASCII/UTF-8/UTF-16 or a missing terminator produces `ReadFailed`; exceeding `ReadOptions.MaxStringBytes` produces
`ReadLimitExceeded`.

Fixed `char[N]` and `wchar[N]` buffers are different: they always consume their declared capacity and do not scan for
an early terminator. See [Arrays, character buffers, and strings](arrays-and-strings.md).

## Differences from native C types

- Portable `long` and `ulong` are always 64-bit. Native C `long` may be 32 or 64 bits.
- Portable `char` is one unsigned raw code unit exposed as CLR `Char`; native plain-`char` signedness can vary.
- Portable `wchar` is one 16-bit UTF-16 code unit. Native `wchar_t` is commonly 16 bits on Windows and 32 bits on
  Unix-like systems.
- Portable `short` and `int` are fixed 16- and 32-bit aliases.
- Stored pointers use the explicit constructor width, not the .NET process width.
- Floating-point names, C integer suffixes, and multi-word names such as `unsigned long` are not Portable primitives.

An enum with no `: storage` uses unsigned one-byte backing. An explicit backing can use the supported integral
families or aliases, but not character, explicit-endian, pointer, composite, string, or another enum type. See
[Enums](structs-unions-enums-typedefs.md#enums).

## Common mistakes

- Use the file format's actual width instead of assuming native C `long`.
- Treat `char` as one raw code unit, not locale-aware text.
- Put `<` or `>` on the supported primitive itself; an arbitrary alias does not automatically gain a suffix.
- Remember that padding comes from the containing struct's placement, not from a primitive alone.
- Supply values inside the declared range; writers do not perform unchecked narrowing.

Fixed primitives work with parse, debug, address, serialize, stream write, update, and selected reads. A scalar has no
dynamic length. Terminated types also support `GetDynamicArrayLength`.
