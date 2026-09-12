---
title: Decode common binary metadata
description: Use three-byte integers, bounded text, LEB128, fixed-point values, identifiers, and conditional records.
---

# Decode common binary metadata

Choose a type from the format's storage definition. Equal byte widths do not
imply equal meanings: a checksum, fixed-point number, and integer can each occupy
four bytes. The inspector's schemas use these types for outer headers and metadata;
compressed or encrypted content still requires a separate decoder.

| Storage | CStruct spelling | Executable example |
| --- | --- | --- |
| Three-byte integer | `uint24`, `int24`, with optional `<` or `>` | [24-bit fields](../examples/recipes/integers-24.md) |
| Encoded text of a known byte length | `utf8`, `latin1`, `cp437`, `utf16le`, `utf16be` arrays | [Bounded encodings](../examples/recipes/bounded-encodings.md) |
| Signed/unsigned LEB128 | `sleb128_32`, `sleb128_64`, `uleb128_32`, `uleb128_64` | [Variable integers](../examples/recipes/variable-integers.md) |
| Binary-scaled integer | `fixed16_16`, `ufixed16_16`, `fixed2_30`, `ufixed8_8` | [Fixed point](../examples/recipes/fixed-point.md) |
| 16-byte identifier | `uuid` for network order; `guid` for Windows field order | [Identifier order](../examples/recipes/identifier-order.md) |
| Discriminator-dependent fields | `if`/`else` and `switch` | [Conditional records](../examples/recipes/conditional-records.md) |

## Preserve storage boundaries

Three-byte integers and identifiers have alignment 1. LEB128 also has alignment 1,
but its byte width is determined by the encoded value. Use a stream-backed address
query for a field following a varint. LEB128 writers emit canonical encodings;
an in-place update rejects a replacement whose encoded width differs.

Bounded text array counts are bytes, including for UTF-16. For example,
`utf16le name[4]` can hold one supplementary character represented by two UTF-16
code units. Existing `wchar[4]` reserves four code units, or eight bytes. Decoders
reject incomplete sequences at the field boundary. Writers reject unmappable or
oversized text and zero-pad shorter text. See [text encodings](strings-and-encodings.md).

Fixed-point values use `Double` and must lie exactly on their storage grid.
For example, unsigned 8.8 represents `0.5` exactly as raw integer 128; `0.1`
requires rounding and is rejected. These values cannot serve as integer counts,
enum backing types, or bitfield storage. UUID/GUID values use `System.Guid` in
managed code and canonical strings in browser JSON. Neither is an integer count.

## Let each record select its own fields

The [conditional-record example](../examples/recipes/conditional-records.md)
uses one schema for text and numeric records. Only the active fields consume
bytes or appear in parsed results and debug ranges. Selecting an inactive field
raises a path error. Names must remain unique even in mutually exclusive arms;
named inline structs make larger alternatives easier to navigate.

Case labels are compiled constants. Predicates can use earlier fields and caller
variables; unavailable active expressions fail. Each group preserves its entry
environment, and each array element selects independently. See the
[grammar and scope rules](../language/grammar.md#conditional-field-groups).

Serialization chooses branches from the supplied values and rejects supplied
inactive members. An update cannot change the active storage plan, even if two
branches have equal byte widths. Serialize a new buffer for that change.

See the [primitive reference](../language/primitive-types.md) for ranges,
endianness, representation, and supported combinations.
