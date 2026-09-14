---
title: Memory addresses and stored data
description: Connect process memory, pointers, files, byte order, text encodings, and bounded parsing.
---

# Memory addresses and stored data

The same byte pattern can represent an integer, text, flags, or an address. The bytes alone cannot tell you which.
This chapter connects [native C structs](native-c-memory.md) to persistent binary formats and CStructSharp's choices.

## An address needs a coordinate system

On ordinary desktop operating systems, a process uses **virtual addresses**. The OS and processor translate these
to physical memory and enforce access permissions. A continuous virtual range need not occupy consecutive physical
RAM. Different processes can use the same numeric virtual address for unrelated data.
[Windows virtual-address explanation](https://learn.microsoft.com/en-us/windows/win32/memory/virtual-address-space)

For a typical C program, automatic local objects often live on a stack, allocated objects in a heap, and objects
with static storage in regions that last for the program's lifetime. These are useful implementation concepts,
not three kinds of struct syntax. A `struct sample` has its type's layout regardless of where an instance is stored.
Its address and lifetime are separate properties.

If a struct contains `char *name`, writing the struct's own bytes does not write the characters at `name`.
The address may be meaningless in another process or after restarting the program. It also says nothing about
how many bytes the target owns. A useful file instead stores inline text, or a defined offset and length.

Memory-mapping a file makes its bytes accessible in a virtual address range. It does not turn every stored offset
into a valid C pointer or change the file's encoding. The mapping's address and the file's offset remain different
coordinates.

## File offsets survive relocation

Suppose a record starts at file position 100 and contains a stored offset of 12:

| Interpretation | Target position |
| --- | ---: |
| Absolute file offset | 12 |
| Offset relative to the record start | 112 |

CStructSharp calls these `AddressingMode.Absolute` and `AddressingMode.Relative`. In relative mode,
`ReadOptions.Origin` supplies the base. It is not implicitly the pointer field's position. For a supplied span or
memory slice, positions start at zero within that region; a whole-file offset may need translation before it is
meaningful there.

The Portable pointer width can be 1, 2, 4, or 8 bytes. A stored zero means null even in relative mode. This is a
deliberate format rule: ISO C's null-pointer concept does not require an all-zero object representation on every
implementation. [C11 draft, sections 6.3.2.3 and 6.2.6.1](https://www.open-std.org/jtc1/sc22/wg14/www/docs/n1570.pdf)

Following a pointer reads a target within the supplied data; it does not access arbitrary process memory.
Serialization writes coordinates but does not allocate targets or relocate them. See the [pointer guide](pointers.md)
for an executable example and traversal limits.

## Byte order is a format choice

For the value `0x12345678`, the four stored bytes are:

```text
increasing offset    0   1   2   3
little-endian       78  56  34  12
big-endian          12  34  56  78
```

Here is how to reconstruct the little-endian value by hand:

```text
value = 0x78 + 0x56 * 256 + 0x34 * 256^2 + 0x12 * 256^3
```

Each byte is a base-256 digit. Endianness orders those digits; it does not reverse bits within a byte, move fields,
or reverse an entire record. A string of ASCII bytes remains in character order.

Machines and protocols developed different conventions. External formats therefore standardize byte order rather
than asking every receiver to use its own processor's order. For example, XDR specifies a most-significant-byte-first
representation. This is an example of a portable interchange contract, not a claim that every network protocol is
big-endian. [XDR specification, RFC 4506](https://www.rfc-editor.org/rfc/rfc4506.html)

CStructSharp defaults to little-endian, with a constructor setting for the whole layout and `<`/`>` suffixes for
supported individual primitives. A format can mix orders; always check its field definitions.

## Signed integers and floating-point values

Width and byte order still do not tell you whether an integer is signed. For 16-bit storage, an unsigned value
ranges from 0 to 65,535. CStructSharp's signed integer codecs use two's complement: if the high bit is set,
subtract 65,536 from the unsigned interpretation. Thus little-endian `FE FF` is either 65,534 as `uint16`, or
**-2** as `int16`. No bytes changed; only the interpretation did.

For `n` bits, the unsigned range is 0 through `2^n - 1`, and the two's-complement signed range is
`-2^(n-1)` through `2^(n-1) - 1`. Storage width and arithmetic width are separate: a 24-bit field takes three
bytes but is returned in a 32-bit C# integer. Intermediate calculations must still fit the language's rules;
Portable array-count expressions use checked signed 32-bit arithmetic.

Floating-point storage uses a different representation, with sign, exponent, and fraction information.
Little-endian bytes `00 00 80 3F` represent **1.0** as `float32`, but **1,065,353,216** as `uint32`.
An integer-to-float conversion computes a numeric value; interpreting the same bits as a float is a different
operation. Choose the layout type from the format specification.

A fixed-point field instead stores an integer multiplied by a documented scale. For unsigned 8.8 storage,
raw integer 128 means `128 / 256 = 0.5`. Not every decimal value lies exactly on that grid; CStructSharp rejects
values that do not, rather than choosing an application-specific rounding policy. See
[binary metadata types](binary-metadata-types.md) for the supported fixed-point formats.

## Bytes, characters, and legacy encodings

A character is not necessarily one byte. Unicode assigns code points; an encoding maps those values to code units
and bytes. UTF-8 uses one to four bytes for a Unicode scalar value. UTF-16 uses one or two 16-bit code units.
A displayed character can consist of several code points, such as a letter and a combining accent.
[Unicode encoding FAQ](https://www.unicode.org/faq/utf_bom.html)

For example, `é` is `C3 A9` in UTF-8 and `E9 00` in UTF-16LE. The text is the same, but a length field must say
whether it counts encoded bytes, code units, or something else. An emoji such as `😀` occupies four UTF-8 bytes
and two UTF-16 code units. A two-byte UTF-8 field cannot hold it.

Historical formats may use one-byte encodings such as Latin-1 or CP437. A byte above ASCII's range has no universal
character meaning. CStructSharp exposes these encodings explicitly so decoding does not depend on the current
machine's locale. Its Latin-1 means ISO-8859-1, not Windows-1252; its CP437 follows the Unicode mapping, including
control characters rather than old display-font pictures.

Choose the field by its storage rule:

| Portable field | What the count means | How reading ends |
| --- | --- | --- |
| `char name[8]` | Eight raw one-byte code units | Exactly eight bytes, preserving zero characters |
| `wchar name[8]` | Eight UTF-16 code units | Exactly sixteen bytes |
| `utf8 name[8]` | Eight encoded UTF-8 bytes | Exactly eight bytes, strict decoding |
| `utf16le name[8]` | Eight encoded UTF-16LE bytes | Exactly eight bytes, strict decoding |
| `utf8_string_zero name` | No declared count | NUL terminator, within the read budget |

C's familiar NUL-terminated strings and fixed arrays must also be distinguished. A C `char name[8]` is storage
capacity; it is not inherently a promise of a terminator. CStructSharp's `char name[]` is its own terminated-text
syntax, not the general flexible-array-member feature of C. See [strings and encodings](strings-and-encodings.md).

## A large source does not imply a small result

A stream is an interface for reading or writing bytes. **Seeking** changes the current byte position without
decoding all earlier fields. Files and memory streams commonly support it; a live network stream commonly does not.
CStructSharp's managed read operations require seeking for address resolution and pointer or union traversal.

The JavaScript API accepts one-pass streams by staging them in temporary storage first. Browser `File`/`Blob`
inputs can instead be read by range. Worker execution keeps longer parsing work off the caller's thread, but
does not make the result lazy: decoding millions of fields still allocates millions of values. A header in a very
large file can be cheap if the layout only asks for the header. See [large binary inputs](browser/large-data.md).

Limits belong to the parsing operation, not just the file length. An array count can demand excessive allocation,
a missing terminator can trigger a long scan, and pointers can form cycles. Bound array elements, string bytes,
nesting, pointer traversal, and total bytes read. The [options guide](variables-options-and-limits.md) explains
which limit controls each kind of work.

Finally, changing bytes is different from safely storing them. `UpdateStream` validates a replacement before
committing it, but a physical write can fail partway through. It is not a filesystem transaction or a guarantee
that data has reached durable storage. If an application needs durable whole-file replacement, it must implement
that policy around serialization using the guarantees of its filesystem and operating system.

## Check your understanding

1. If a record moves from file offset 100 to 200, what happens to a relative offset of 12?
2. Is UTF-16 field capacity 4 the same for `wchar[4]` and `utf16le[4]`?
3. Can paging a 10 GiB file make decoding every array element free of memory cost?

Answers: its target becomes **212** when the origin follows the record; **no**, the fields occupy eight and four
bytes respectively; **no**, paging bounds input buffering, not the size of the decoded result.
