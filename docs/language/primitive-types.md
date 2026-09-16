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
| `bool` | `bool` | 1 / 1 | Boolean, `true`/`false` (any nonzero byte reads as true) | Not applicable | `Boolean` |
| `char` | `char` | 1 / 1 | Raw code unit, U+0000..U+00FF | Not applicable | `Char` |
| `utf8` | `utf8` | 1 / 1 | Raw UTF-8 byte; counted arrays decode to strings | Not applicable | `Byte` |
| `latin1` | `latin1` | 1 / 1 | Raw byte; counted arrays decode strict text | Explicit in name / independent | `Byte` |
| `cp437` | `cp437` | 1 / 1 | Raw byte; counted arrays decode strict text | Explicit in name / independent | `Byte` |
| `utf16le` | `utf16le` | 1 / 1 | Raw byte; counted arrays decode strict text | Explicit in name / independent | `Byte` |
| `utf16be` | `utf16be` | 1 / 1 | Raw byte; counted arrays decode strict text | Explicit in name / independent | `Byte` |
| `wchar` | `wchar` | 2 / 2 | UTF-16 code unit, U+0000..U+FFFF | Layout | `Char` |
| `wchar<` | `wchar<` | 2 / 2 | UTF-16 code unit, U+0000..U+FFFF | Little | `Char` |
| `wchar>` | `wchar>` | 2 / 2 | UTF-16 code unit, U+0000..U+FFFF | Big | `Char` |
| `int16`, `int16<`, `int16>` | Matching `int16` codec | 2 / 2 | Signed, -32768..32767 | Layout / little / big | `Int16` |
| `uint16`, `uint16<`, `uint16>` | Matching `uint16` codec | 2 / 2 | Unsigned, 0..65535 | Layout / little / big | `UInt16` |
| `int24`, `int24<`, `int24>` | Matching `int24` codec | 3 / 1 | Signed, -8388608..8388607 | Layout / little / big | `Int32` |
| `uint24`, `uint24<`, `uint24>` | Matching `uint24` codec | 3 / 1 | Unsigned, 0..16777215 | Layout / little / big | `UInt32` |
| `int32`, `int32<`, `int32>` | Matching `int32` codec | 4 / 4 | Signed, -2147483648..2147483647 | Layout / little / big | `Int32` |
| `uint32`, `uint32<`, `uint32>` | Matching `uint32` codec | 4 / 4 | Unsigned, 0..4294967295 | Layout / little / big | `UInt32` |
| `int64`, `int64<`, `int64>` | Matching `int64` codec | 8 / 8 | Signed, -9223372036854775808..9223372036854775807 | Layout / little / big | `Int64` |
| `uint64`, `uint64<`, `uint64>` | Matching `uint64` codec | 8 / 8 | Unsigned, 0..18446744073709551615 | Layout / little / big | `UInt64` |
| `int48`, `int48<`, `int48>` | Matching `int48` codec | 6 / 1 | Signed, -140737488355328..140737488355327 | Layout / little / big | `Int64` |
| `uint48`, `uint48<`, `uint48>` | Matching `uint48` codec | 6 / 1 | Unsigned, 0..281474976710655 | Layout / little / big | `UInt64` |
| `int128`, `int128<`, `int128>` | Matching `int128` codec | 16 / 16 | Signed 128-bit | Layout / little / big | `Int128` |
| `uint128`, `uint128<`, `uint128>` | Matching `uint128` codec | 16 / 16 | Unsigned 128-bit | Layout / little / big | `UInt128` |
| `float16`, `float16<`, `float16>` | Matching `float16` codec | 2 / 2 | Any IEEE-754 binary16 bit pattern | Layout / little / big | `Half` |
| `float32`, `float32<`, `float32>` | Matching `float32` codec | 4 / 4 | Any IEEE-754 binary32 bit pattern (NaN, ±Infinity, subnormals, ±0.0 included) | Layout / little / big | `Single` |
| `float64`, `float64<`, `float64>` | Matching `float64` codec | 8 / 8 | Any IEEE-754 binary64 bit pattern (NaN, ±Infinity, subnormals, ±0.0 included) | Layout / little / big | `Double` |

The table lists the canonical codec spellings. Every C, C99, Windows SDK, Linux kernel, IDA, and dissect alias
spelling below resolves to one of them at construction time exactly like a `typedef` would - a compiled field
never sees the alias - so a header can be pasted with its own vocabulary. None of these infer a native compiler's
data model: the numeric spellings alias the numeric `int8`/`uint8` codecs, not the raw `char` code unit, and every
width is fixed by the table. A layout may redeclare an alias spelling (`typedef uint16 DWORD;`, `enum BYTE : ...`)
and its own declaration then wins; the canonical spellings stay reserved.

## Alias spellings

<!-- sync-primitive-spellings:start -->

| Canonical codec | Accepted alias spellings |
| --- | --- |
| `ascii_string_zero` | `cstring` |
| `bool` | `_Bool` |
| `char` | `CHAR` |
| `float32` | `float`, `FLOAT` |
| `float64` | `double`, `DOUBLE` |
| `int128` | `__int128`, `__s128`, `INT128`, `int128_t`, `s128` |
| `int16` | `__int16`, `__s16`, `INT16`, `int16_t`, `s16`, `short`, `SHORT`, `short int`, `signed short`, `signed short int` |
| `int32` | `__int32`, `__s32`, `int`, `INT`, `INT32`, `int32_t`, `LONG`, `LONG32`, `s32`, `signed`, `signed int` |
| `int64` | `__int64`, `__s64`, `INT64`, `int64_t`, `long long`, `long long int`, `LONG64`, `LONGLONG`, `s64`, `signed long long`, `signed long long int` |
| `int8` | `__int8`, `__s8`, `INT8`, `int8_t`, `s8`, `signed char` |
| `sleb128_64` | `ileb128`, `sleb128` |
| `uint128` | `__u128`, `_OWORD`, `OWORD`, `u128`, `UINT128`, `uint128_t`, `unsigned __int128` |
| `uint16` | `__u16`, `_WORD`, `u_int16_t`, `u_short`, `u16`, `UINT16`, `uint16_t`, `unsigned __int16`, `unsigned short`, `unsigned short int`, `ushort`, `USHORT`, `WORD` |
| `uint32` | `__u32`, `_DWORD`, `DWORD`, `DWORD32`, `u_int`, `u_int32_t`, `u32`, `uint`, `UINT`, `UINT32`, `uint32_t`, `ULONG`, `ULONG32`, `unsigned`, `unsigned __int32`, `unsigned int` |
| `uint64` | `__u64`, `_QWORD`, `DWORD64`, `DWORDLONG`, `QWORD`, `u_int64_t`, `u64`, `UINT64`, `uint64_t`, `ULONG64`, `ULONGLONG`, `unsigned __int64`, `unsigned long long`, `unsigned long long int` |
| `uint8` | `__u8`, `_BYTE`, `BYTE`, `u_char`, `u_int8_t`, `u8`, `uchar`, `UCHAR`, `UINT8`, `uint8_t`, `unsigned __int8`, `unsigned char` |
| `uleb128_64` | `uleb128` |
| `unicode_string_zero` | `string` |
| `unicode_string_zero<` | `string<` |
| `unicode_string_zero>` | `string>` |
| `void` | `VOID` |
| `wchar` | `WCHAR`, `wchar_t` |
| `int64` / `uint64` (`CLongWidth` 64, the default) or `int32` / `uint32` (`CLongWidth` 32) | `long`, `long int`, `signed long`, `signed long int`, `time_t`, `off_t`, `ulong`, `unsigned long`, `unsigned long int` |
| `uintN` / `intN` where N is the layout's pointer width in bits | `size_t`, `uintptr_t`, `SIZE_T`, `ULONG_PTR`, `UINT_PTR`, `DWORD_PTR`, `ssize_t`, `intptr_t`, `ptrdiff_t`, `SSIZE_T`, `LONG_PTR`, `INT_PTR` |
| `void*` (an opaque address of pointer width) | `PVOID`, `LPVOID`, `LPCVOID`, `HANDLE` |
| `char*` (a pointer to a byte string) | `PSTR`, `LPSTR`, `PCSTR`, `LPCSTR` |
| `wchar*` (a pointer to a UTF-16 string) | `PWSTR`, `LPWSTR`, `PCWSTR`, `LPCWSTR` |

<!-- sync-primitive-spellings:end -->

The `long` family is the one place C leaves the width to the target. Portable reads it as 64 bits (LP64, the
reading of every Linux kernel header on a 64-bit target); `CStructCompilationOptions.CLongWidth = 32` selects the
ILP32/LLP64 reading, which is also what dissect.cstruct assumes. Windows `LONG`/`ULONG` are always 32 bits and are
not part of the family. The pointer-sized spellings (`size_t`, `ssize_t`, `intptr_t`, `uintptr_t`, `ptrdiff_t`,
`SIZE_T`, `SSIZE_T`, `ULONG_PTR`, `LONG_PTR`, `UINT_PTR`, `INT_PTR`, `DWORD_PTR`) are as wide as the layout's
configured pointer size. The complete alias rows are also stored in
[`portable-v1.json`](../../contracts/language/portable-v1.json) (`aliasSpellings`) and checked against the runtime
on .NET 8 and .NET 10; `tools/documentation/sync-primitive-spellings.mjs` regenerates every view from the source
table.

Alignment equals byte width for every fixed primitive except the three- and six-byte integers, which have no natural
alignment in any ABI and align to one, and `uuid`/`guid`. Packed placement ignores alignment when choosing the next
field position; aligned placement uses it. The alignment still appears in size/alignment queries in packed mode.
`int48`/`uint48` sign-extend or zero-extend into 64-bit results; `int128`/`uint128` read into `System.Int128` and
`System.UInt128` (a JavaScript consumer receives them as safe integers or decimal strings, like `uint64`);
`float16` is bit-exact like the other floats. None of the three is bitfield storage or an enum backing type, and
none takes the span fast paths - they are rare in real formats and read through their codec delegates.

`void` has no storage of its own: only a pointer to it (`void *`, or the `PVOID`/`LPVOID`/`HANDLE` spellings) is a
field, an opaque address of the layout's pointer width that is never dereferenced. A function pointer declarator
(`uint8 (*callback)(uint8)`) is accepted and stored the same way; its signature is discarded.

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

`long double` is not supported because its representation depends on the native compiler and target.
Choose `float32` or `float64` only when the format specifies the corresponding IEEE 754 representation.
The storage width includes sign and exponent bits: binary32 has 24 bits of precision and binary64 has 53,
not 32 and 64 bits of integer precision. Neither represents every decimal fraction exactly.
For a format that specifies a binary-scaled integer, use the documented [fixed-point types](../guides/binary-metadata-types.md)
instead of assuming an ordinary float has the same bytes.

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

For a known byte length, use `utf8 text[length];` instead of a terminated primitive.
See [byte-bounded UTF-8 buffers](arrays-and-strings.md#byte-bounded-utf-8-buffers).

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

Three-byte integers use alignment 1 even in aligned layouts, and arrays have a three-byte stride.
They support ordinary numeric fields and scalar typedefs, but cannot back enums or bitfields.
Out-of-range writes fail before the value is committed.

## Variable-length integers

`uleb128_32` and `uleb128_64` decode unsigned LEB128 into `UInt32` and `UInt64`.
`sleb128_32` and `sleb128_64` decode signed LEB128 into `Int32` and `Int64`.
They have alignment 1, no endian suffix, and a maximum of five or ten encoded
bytes respectively. Legal padded encodings are accepted; unused terminal bits
must agree with the declared width and sign. Writers emit the shortest encoding.

Arrays may contain differently sized encodings. Addresses of following fields and
array elements require the original stream; static byte sizing is unavailable.
Numeric values may supply checked array counts. These types cannot back enums or
bitfields and are not terminated strings. A scalar has no string/array length.

In-place updates require the replacement to have the same encoded byte length.
A canonical rewrite of a padded value can therefore fail even when the numeric
value is unchanged. Serialize into a new buffer when changing encoded widths.
LEB128 does not describe EBML VINT, BER lengths, SQLite varints, or MIDI VLQ.

```c
struct section {
    uint8 id;
    uleb128_32 length;
    uint8 payload[length];
};
```

## Fixed-point values

Decimal inputs are checked before conversion to `Double`, so a decimal fraction
outside the storage grid cannot disappear during conversion and silently become an accepted value.

| Type | Integer storage | Fractional bits | Bytes / alignment | CLR |
| --- | --- | --- | --- | --- |
| `fixed16_16`, `fixed16_16<`, `fixed16_16>` | Signed 32-bit | 16 | 4 / 4 | Double |
| `ufixed16_16`, `ufixed16_16<`, `ufixed16_16>` | Unsigned 32-bit | 16 | 4 / 4 | Double |
| `fixed2_30`, `fixed2_30<`, `fixed2_30>` | Signed 32-bit | 30 | 4 / 4 | Double |
| `ufixed8_8`, `ufixed8_8<`, `ufixed8_8>` | Unsigned 16-bit | 8 | 2 / 2 | Double |

The stored integer is divided by 2 to the power of the fractional-bit count.
All supported raw values are exactly representable as Double. Neutral spellings
use layout byte order; `<` and `>` select explicit byte order. Writers reject
NaN, infinity, out-of-range values and values outside the storage grid instead
of rounding. For example, signed 16.16 encodes -1.5 as the integer -98304.
Fixed-point fields cannot supply integer layout expressions or back enums or bitfields.

## UUID and GUID identifiers

`uuid` and `guid` each occupy 16 bytes with alignment 1 and return `System.Guid`.
The enclosing layout byte order does not affect them; endian suffixes are not
accepted. Writers accept Guid values or canonical hyphenated D-format strings.
All bit patterns are allowed, including nil and nonstandard version bits.
WASM JSON represents these values as canonical lowercase identifier strings.

For `00112233-4455-6677-8899-aabbccddeeff`, the storage is:

- `uuid`: `00 11 22 33 44 55 66 77 88 99 aa bb cc dd ee ff`
- `guid`: `33 22 11 00 55 44 77 66 88 99 aa bb cc dd ee ff`

Use `guid` for Windows GUID fields and `uuid` for network-order UUID fields.
Hashes and arbitrary sixteen-byte payloads should remain byte arrays.
