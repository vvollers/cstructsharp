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
| `bool`, `_Bool` | `bool` | 1 / 1 | Boolean, `true`/`false` (any nonzero byte reads as true) | Not applicable | `Boolean` |
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
| `float32`, `float32<`, `float32>` | Matching `float32` codec | 4 / 4 | Any IEEE-754 binary32 bit pattern (NaN, ±Infinity, subnormals, ±0.0 included) | Layout / little / big | `Single` |
| `float64`, `float64<`, `float64>` | Matching `float64` codec | 8 / 8 | Any IEEE-754 binary64 bit pattern (NaN, ±Infinity, subnormals, ±0.0 included) | Layout / little / big | `Double` |
| `float` | `float32` | 4 / 4 | Same as `float32` | Layout | `Single` |
| `double` | `float64` | 8 / 8 | Same as `float64` | Layout | `Double` |
| `short` | `int16` | 2 / 2 | Signed, -32768..32767 | Layout | `Int16` |
| `ushort` | `uint16` | 2 / 2 | Unsigned, 0..65535 | Layout | `UInt16` |
| `int` | `int32` | 4 / 4 | Signed, -2147483648..2147483647 | Layout | `Int32` |
| `uint` | `uint32` | 4 / 4 | Unsigned, 0..4294967295 | Layout | `UInt32` |
| `long` | `int64` | 8 / 8 | Signed, -9223372036854775808..9223372036854775807 | Layout | `Int64` |
| `ulong` | `uint64` | 8 / 8 | Unsigned, 0..18446744073709551615 | Layout | `UInt64` |
| `signed`, `signed int` | `int32` | 4 / 4 | Signed, -2147483648..2147483647 | Layout | `Int32` |
| `unsigned`, `unsigned int` | `uint32` | 4 / 4 | Unsigned, 0..4294967295 | Layout | `UInt32` |
| `signed short` | `int16` | 2 / 2 | Signed, -32768..32767 | Layout | `Int16` |
| `unsigned short` | `uint16` | 2 / 2 | Unsigned, 0..65535 | Layout | `UInt16` |
| `signed long` | `int64` | 8 / 8 | Signed, -9223372036854775808..9223372036854775807 | Layout | `Int64` |
| `unsigned long` | `uint64` | 8 / 8 | Unsigned, 0..18446744073709551615 | Layout | `UInt64` |
| `long long`, `signed long long` | `int64` | 8 / 8 | Signed, -9223372036854775808..9223372036854775807 | Layout | `Int64` |
| `unsigned long long` | `uint64` | 8 / 8 | Unsigned, 0..18446744073709551615 | Layout | `UInt64` |
| `signed char`, `int8_t` | `int8` | 1 / 1 | Signed, -128..127 | Not applicable | `SByte` |
| `unsigned char`, `uint8_t` | `uint8` | 1 / 1 | Unsigned, 0..255 | Not applicable | `Byte` |
| `int16_t` | `int16` | 2 / 2 | Signed, -32768..32767 | Layout | `Int16` |
| `uint16_t` | `uint16` | 2 / 2 | Unsigned, 0..65535 | Layout | `UInt16` |
| `int32_t` | `int32` | 4 / 4 | Signed, -2147483648..2147483647 | Layout | `Int32` |
| `uint32_t` | `uint32` | 4 / 4 | Unsigned, 0..4294967295 | Layout | `UInt32` |
| `int64_t` | `int64` | 8 / 8 | Signed, -9223372036854775808..9223372036854775807 | Layout | `Int64` |
| `uint64_t` | `uint64` | 8 / 8 | Unsigned, 0..18446744073709551615 | Layout | `UInt64` |

The table groups 62 accepted spellings, including the wider C integer spellings (`unsigned long long`, `uint32_t`,
and similar) accepted as aliases of an existing fixed-width codec — none of these infer a native compiler's data
model; `signed char`/`unsigned char`/`*_t` forms alias the numeric `int8`/`uint8` codecs, not the raw `char` code
unit. The complete rows are also stored in
[`portable-v1.json`](../../contracts/language/portable-v1.json) and checked against the runtime on .NET 8 and .NET 10.

Alignment equals byte width for every fixed primitive. Packed placement ignores alignment when choosing the next
field position; aligned placement uses it. The alignment still appears in size/alignment queries in packed mode.

## Floating-point primitives

`float32`/`float64` (with `float`/`double` as familiar aliases) codecs bit-reinterpret rather than numerically or
textually convert: a read is exactly `System.Single`/`System.Double`'s IEEE-754 bits taken directly from the
stream, and a write is exactly the caller's value's own bits, byte-order-adjusted the same way any other multi-byte
primitive's bytes are. Because `Single`/`Double`'s CLR bit layout already *is* the complete IEEE-754 binary32/64
encoding space, this guarantees exact round-tripping with no special-case code, for every representable bit
pattern:

```c
struct sample {
    float32 a;
    float64 b;
};
```

- **Every NaN payload the caller's `float`/`double` value actually carries round-trips exactly**, including a
  negative sign bit - the codec never collapses a specific NaN bit pattern into a single canonical "the" NaN the
  way `bool` canonicalizes a non-canonical byte (see
  [Differences from native C types](#differences-from-native-c-types)). This is a guarantee about the codec, not
  about the .NET runtime: a **signaling** NaN is inherently fragile in managed code and may already be quieted by
  an ordinary floating-point operation (a JIT-optimized load, an arithmetic step, even a plain variable assignment
  in a release build) before the codec ever receives it - by the time a `float`/`double` value reaches the codec,
  whatever bit pattern it actually holds round trips exactly, but the codec cannot restore a signaling bit the
  runtime already cleared beforehand.
- **Subnormal values round-trip exactly** - they are just another bit pattern in the encoding space, not a
  numerically special case the codec treats differently.
- **Negative zero round-trips as a distinct bit pattern from positive zero**, even though `+0.0 == -0.0` under CLR
  `==` - a round-trip test that only checks `==` cannot verify this guarantee; compare the raw bits instead
  (`BitConverter.SingleToInt32Bits`/`DoubleToInt64Bits`).
- Supplying a differently-sized CLR floating type (e.g. a `double` value into a `float32` field) performs a real,
  potentially lossy numeric narrowing conversion (`Convert.ToSingle`) - the bit-exact guarantee applies only when
  the caller already supplies the matching width.

`long double` is not supported: unlike `long`/`ulong` (a single, globally reasonable fixed 64-bit choice for every
target), `long double` has no single portable width to standardize on - 80-bit extended, 128-bit quad, or 64-bit
depending on compiler and target - so no fixed-width codec could represent it losslessly.

The `floating-point-primitives` fixture checks `a=1.5` (`float32`), `b=2.5` (`float64`), size 12, alignment 8, and
bytes `0000C03F0000000000000440` on both frameworks.

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
- C integer literal suffixes (`123u`, `123L`, and similar) are not Portable syntax - a `#define` value is a plain
  expression.
- `float`/`double` are Portable primitives (`float32`/`float64`); `long double` is not - see
  [Floating-point primitives](#floating-point-primitives).
- `bool`/`_Bool` is always 1 byte with canonical `0x00`/`0x01` write output; native `_Bool`/C++ `bool` storage width
  and representation can vary by compiler and ABI.

An enum with no `: storage` uses unsigned one-byte backing. An explicit backing can use the supported integral
families or aliases, but not character, boolean, explicit-endian, pointer, composite, string, or another enum type.
See [Enums](structs-unions-enums-typedefs.md#enums).

## Common mistakes

- Use the file format's actual width instead of assuming native C `long`.
- Treat `char` as one raw code unit, not locale-aware text.
- Put `<` or `>` on the supported primitive itself; an arbitrary alias does not automatically gain a suffix.
- Remember that padding comes from the containing struct's placement, not from a primitive alone.
- Supply values inside the declared range; writers do not perform unchecked narrowing.

Fixed primitives work with parse, debug, address, serialize, stream write, update, and selected reads. A scalar has no
dynamic length. Terminated types also support `GetDynamicArrayLength`.
