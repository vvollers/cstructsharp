---
title: Arrays, character buffers, and strings
description: Calculate fixed/runtime array extents and choose fixed-capacity or terminated text storage.
---

# Arrays, character buffers, and strings

Portable supports one-dimensional arrays. Text has two distinct storage shapes:

- fixed character buffers own an exact number of code units; and
- terminated strings continue until a NUL or line-feed marker.

Array counts are element counts, not byte counts. The byte extent is count multiplied by the complete element stride.
In aligned mode, that stride includes any tail padding in a nested struct.

## Fixed arrays

`T field[expression];` declares an array whose expression must become a non-negative `Int32`.

```c
struct root {
    uint16 values[2];
    uint8 tail;
};
```

For packed little-endian bytes `34 12 78 56 9A`:

| Path | Offset | Value |
| --- | ---: | ---: |
| `root.values` / `root.values[0]` | 0 | `4660` |
| `root.values[1]` | 2 | `22136` |
| `root.tail` | 4 | `154` |

```text
offset   0    1    2    3    4
byte    34   12   78   56   9A
field   values[0] values[1] tail
```

Even counts zero and one produce collection-shaped results. A zero-length array consumes no element bytes; the next
field may begin at the same packed offset or at its own aligned offset.

`GetDynamicArrayLength` returns the evaluated count. Serialization needs exactly that many items, including zero or
one. The `fixed-arrays` fixture checks values `17`, `34`, and `126` at offsets `0`, `1`, and `2`.

`values[2][3]` is not supported. Model a row as a named struct or flatten the dimensions explicitly.

## Runtime expression arrays

The expression may depend on a `#define`, enum member, an earlier field exposed by an operation, or an integer
variable supplied by the caller:

```c
struct root {
    uint8 values[N];
    byte tail;
};
```

With `N = 2`, bytes `11 22 7E` place the two array items at offsets 0 and 1 and `tail` at offset 2.

Because `N` can change, `GetStructSizeInBytes("root")` cannot return one fixed size. Pass the same variable values to
parse, address, length, serialize, write, and update operations. Counts remain subject to
`ReadOptions.MaxArrayElements` and the corresponding write limits.

An unsized non-character declaration such as `uint16 values[]` is not a “read the remaining bytes” field. It fails
with `InvalidLayout` because no safe extent is defined.

## Fixed character buffers

`char[N]` stores exactly `N` one-byte code units. `wchar[N]`, `wchar<[N]`, and `wchar>[N]` store exactly `N` UTF-16
code units with neutral, little-, or big-endian byte order. Their direct result is a C# string, but their storage
still has fixed array behavior.

For `struct root { char value[2]; byte tail; };` and bytes `41 42 7E`:

| Offset | Bytes | Field/value |
| ---: | --- | --- |
| 0 | `41 42` | `root.value` = `"AB"` |
| 2 | `7E` | `root.tail` = `126` |

Writing `"A"` to `char[2]` produces `41 00`. Writing more than two code units fails instead of extending the field.
An embedded zero is ordinary fixed-buffer content.

A Unicode character outside the Basic Multilingual Plane consumes two UTF-16 code units. Updating one indexed
`wchar` changes one raw code unit; it does not repair a neighboring surrogate automatically.

## Terminated strings

An empty character dimension (`char[]`, `wchar[]`, `wchar<[]`, or `wchar>[]`) scans for a NUL code unit. Named
terminated primitives can use NUL or LF. Their byte size is found while reading, and their alignment is one.

| Family | Strict encoding | Terminator | Bytes for `"A"` |
| --- | --- | --- | --- |
| `char[]`, `cstring`, `ascii_string_zero` | ASCII | NUL | `41 00` |
| `ascii_string_newline` | ASCII | LF | `41 0A` |
| `utf8_string_zero` | UTF-8 | NUL | `41 00` |
| `utf8_string_newline` | UTF-8 | LF | `41 0A` |
| `wchar[]`, `string`, `unicode_string_zero` | UTF-16 in layout order | NUL code unit | LE: `41 00 00 00` |
| `string<`, `unicode_string_zero<` | UTF-16LE | NUL code unit | `41 00 00 00` |
| `string>`, `unicode_string_zero>` | UTF-16BE | NUL code unit | `00 41 00 00` |
| `unicode_string_newline*` | Matching UTF-16 variant | LF code unit | LE: `41 00 0A 00` |

```text
struct root { utf8_string_zero value; byte tail; }
bytes        41 00 7E
value        └─A─┘  └tail
offsets       0      2
```

The `terminated-strings` fixture checks this exact example. Decoding rejects non-ASCII input for ASCII handlers,
malformed UTF-8, odd-byte UTF-16, and unpaired surrogates. There is no replacement-character or byte-order-mark
detection mode.

`MaxStringBytes` includes the complete encoded terminator. A limit of 2 rejects `41 42 00` with
`ReadLimitExceeded`. `GetDynamicArrayLength` returns the decoded character/code-unit count without the terminator.

A selected update may replace a terminated value only inside the existing storage plan; it does not relocate later
fields. A value containing its own terminator is invalid.

## Choose the correct shape

| Shape | Fixed size | Length result | Indexed path | Main limit |
| --- | --- | --- | --- | --- |
| Fixed `T[N]` | Yes when `N` is fixed | Element count | Yes | Array elements |
| Runtime `T[N]` | No | Evaluated element count | Yes | Array elements and expression |
| Fixed `char[N]` / `wchar[N]` | Yes | Code-unit count | Yes | Array elements |
| Terminated string | No | Decoded count | No element indexing | Encoded string bytes |

Do not confuse zero-filled fixed text with a terminated scan, infer an array count from remaining stream bytes, omit
runtime variables on a later operation, or assume UTF-16 code units equal user-perceived characters.

See the [text guide](../guides/strings-and-encodings.md), [selected-read guide](../guides/reading-values.md), and
[update guide](../guides/updating-existing-data.md).
