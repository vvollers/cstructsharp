---
title: Work with stored pointers
description: Decode pointer coordinates, distinguish storage from targets, and follow data within explicit limits.
---

# Work with stored pointers

A pointer in a binary file or message is a number that refers to another byte position. It is not a C# reference and
must never be treated as a process memory address.

Three positions are easy to confuse:

1. The *pointer field position* is where the encoded pointer bytes are stored.
2. The *stored address* is the unsigned number decoded from those bytes.
3. The *effective target position* is where CStructSharp reads the pointed-to value. In relative mode, this is the
   configured origin plus the stored address.

The layout and read options tell CStructSharp how to connect them.

## Follow a simple pointer

This layout stores a one-byte pointer to a one-byte value:

```c
struct root {
    uint8 *target;
};
```

The executable example sets `pointerSize: 1` and reads bytes `01 2A`:

[!code-csharp[Follow a bounded one-byte pointer](../examples/Program.cs#api-reference-pointer-read-options)]

```text
offset       0            1
bytes       01           2A
field       target       pointed-to uint8
stored      address 1 ───────► value 0x2A
```

The returned `Pointer` reports `Address = 1`, `IsDereferenced = true`, and `Value = 0x2A`. The stream contains no
object allocation or relocation information; it contains only the coordinate `1`.

Pointer width is part of the data format. Set it to 1, 2, 4, or 8 when constructing `CStruct`; do not copy the
bitness of the .NET process.

## Null and unresolved pointers

A stored zero is always null. A nonzero pointer can be left unresolved by setting
`ReadOptions.DereferencePointers = false`.

| State | `IsNull` | `IsDereferenced` | `Value` |
| --- | --- | --- | --- |
| Stored zero | `true` | `false` | `null` |
| Nonzero, following disabled | `false` | `false` | `null` |
| Nonzero, followed successfully | `false` | `true` | Decoded target |

This distinction lets inspection tools display an address without reading untrusted target data.

## Absolute and relative coordinates

Absolute mode treats the stored number as the target's stream position. Relative mode adds
`ReadOptions.Origin`. Use relative mode only when the format specification says offsets are measured from a known
base, such as the beginning of a record.

Zero remains null in both modes; the origin is not added to it. Effective targets must be non-negative, fit the
stream coordinate range, and stay within the supplied memory region or readable stream.

## Paths and multiple levels

After a pointer field:

- `.address` selects the pointer field's stored coordinate for reading or writing;
- `.value` follows one declared pointer level.

For `uint8 **target`, `root.target.value` reaches the second pointer and
`root.target.value.value` reaches the final byte. Each level consumes traversal budget.

Serialization writes pointer coordinates. It does not move target objects, allocate storage for them, or fix up
addresses automatically. The application must already know the correct coordinate.

## Limits and troubleshooting

Use `ReadOptions` to limit pointer depth, bytes per fixed target, total physical bytes read, arrays, strings, and
nested structures. These limits protect against cycles and addresses designed to make the reader traverse excessive
data.

When following fails, inspect:

1. pointer width and byte order;
2. absolute versus relative mode and the origin;
3. the stored address and effective target position;
4. whether the stream or supplied memory contains the target;
5. the number of `.value` levels; and
6. pointer depth, target-size, and total-read limits.

Read [Pointers and addressing](../language/pointers-and-addressing.md) for multi-level, union, array, write, and
failure rules.
