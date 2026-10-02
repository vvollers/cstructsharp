---
title: Inspect byte ranges and addresses
description: Connect decoded values to the exact positions they occupied in a stream.
---

# Inspect byte ranges and addresses

Normal parsing tells you what the data means. Diagnostic parsing also tells you which bytes produced each value.
This is useful for hex viewers, format inspectors, and error reports.

Use `ParseWithDebug` when you need a struct's values and ranges together; it returns a `ParseResult` whose `Value`
is the same `StructValue` that `Parse` returns and whose `Debug` list holds one `DebugData` record per value read.
`ParseWithDebug`, like `Parse`, accepts only a path that selects one struct (a root, a nested struct, or one element
of a struct array). To read a union, an array or a scalar with its ranges, use `ReadValueWithDebug`: it does the
same for any selection (a struct, a union, an array, one element, a scalar, a bitfield, a pointer, an enum or a
string) and returns a `ReadResult` whose `Value` is what `ReadValue` returns. Use
`ResolveAddress` when you need only the absolute stream position of one path. `DebugData` lives in the
`CStructSharp.Diagnostics` namespace.

## Capture value ranges

The executable example uses a three-byte packed structure:

```c
struct sample {
    uint8 tag;
    uint16 value;
};
```

[!code-csharp[Inspect ranges and resolve one field address](../examples/Program.cs#api-reference-debug-data)]

With input `A1 34 12`, `sample.value` occupies the half-open range `[1, 3)`: it starts at position 1 and ends just
before position 3. Half-open ranges make the byte count easy to calculate: `End - Start`, or `3 - 1 = 2`; the
record's `Length` property does this for you.

A typedef root keeps the name you selected. For `typedef sample packet;`, reading `packet` reports
`packet.value`, not `sample.value` or `packet.packet.value`. Inline and chained aliases follow the same rule.

Each `DebugData` record is an immutable value with the item's `Path` (`sample.value`), `Start` and `End`
positions, `TypeName`, and decoded `Value`. Select the bytes from your own input with `Start..End`; only a union
captured as raw storage carries its bytes in `Bytes`. Treat records as diagnostic output. They expose exact input
values, so filter them before sending them outside your application's trusted logs or diagnostic tools.

## Ranges of one selection

`ReadValueWithDebug` records only what the selected value needs, and it names each record exactly as a debug parse
of the whole root would. This packed layout uses one-byte pointers:

```c
struct pair { uint8 a; uint8 b; };
struct root {
    uint8 head;       /* byte 0 */
    uint16 value;     /* bytes 1-2 */
    pair items[2];    /* bytes 3-6 */
    uint8 codes[2];   /* bytes 7-8 */
    uint8 low:3;      /* byte 9, bits 0-2 */
    uint8 high:5;     /* byte 9, bits 3-7 */
    pair *link;       /* byte 10: the address of the target */
};
```

With input `00 34 12 01 02 03 04 07 08 AB 0B 11 22`, the target of `link` is the `pair` at bytes 11-12:

| Path | Value | Records |
| --- | --- | --- |
| `root.value` | `4660` | `root.value [1, 3)` |
| `root.items[1]` | a `StructValue` | `root.items[1].a [5, 6)`, `root.items[1].b [6, 7)` |
| `root.items` | a list of two structs | one record per member of each element, under `root.items[0]` and `root.items[1]` |
| `root.codes` | an array of the bytes 7 and 8 | `root.codes [7, 8)`, `root.codes [8, 9)`: one record per element |
| `root.codes[1]` | `8` | `root.codes [8, 9)` |
| `root.high` | `21` | `root.high [9, 10)`: a bitfield covers its whole storage unit |
| `root.link` | a `Pointer` | `root.link.value.a [11, 12)`, `root.link.value.b [12, 13)`, then `root.link [10, 11)` |
| `root.link.address` | `11` | `root.link [10, 11)`: the stored address |
| `root.link.value` | a `StructValue` | `root.link.value.a [11, 12)`, `root.link.value.b [12, 13)` |

The elements of a struct array keep their index in the path because each element has members of its own. The
elements of a scalar array share their member's path, as they do in a whole-root parse. A null pointer's `.value`
returns `null` with no records.

## Records of a pointer and its target

A pointer occupies two places in the input: the bytes that store the address, and the bytes at that address (the
*target*). Its debug records keep the two apart, using the same names as the result. In the result, a pointer is a
`Pointer` object whose `Address` is the stored number and whose `Value` is the target. In a path, `.address`
selects the stored number and `.value` follows the pointer. So:

- The record of the stored address carries the pointer's own path: `root.link [10, 11)`.
- Every level a parse follows adds a `value` segment. The members of the `pair` above are `root.link.value.a` and
  `root.link.value.b`.

A scalar target works the same way. For `struct box { uint8 *flag; };` and input `01 2A`, the parse records
`box.flag [0, 1)` for the stored address 1 and `box.flag.value [1, 2)` for the byte 42 at that address. A pointer
to a pointer (`uint8 **deep`) adds one `value` per level: `root.deep` is the first stored address,
`root.deep.value` the second one, and `root.deep.value.value` the final byte. A union target's own record is
`root.choice.value`, after the records of its views (`root.choice.value.small`, ...).

Each element of a pointer array keeps its index, whatever its target, because each element has a target of its own.
For `uint8 *bytes[2];` the stored addresses are `root.bytes[0]` and `root.bytes[1]`, and their targets are
`root.bytes[0].value` and `root.bytes[1].value`.

Because of these rules, no target record shares a path with the record of a pointer's address. Apart from the
elements of a scalar array, which share their member's path, a record's path is also the path of its value in the
result. Apart from one more case, it is a path you can pass to `ReadValue`, `ResolveAddress` or
`ReadValueWithDebug`. The exception is a counted target (`pair *nodes @count(n);`): its element `3` is recorded as
`root.nodes.value[3].a`, but a path cannot select a counted target, because the count may name a member that a path
never reads.

A struct follows its pointers after its last member, so the records of a pointer's target come after the records of
the struct that holds the pointer. A pointer's own record comes after its target's records when the pointer is
followed in place, as a selection of `root.link` shows.

## Resolve a position without returning the value

```csharp
long address = layout.ResolveAddress(stream, "sample.value");
```

The example receives address `1`. `ResolveAddress` restores the caller's original stream position after the lookup,
so it can be used to annotate a stream without consuming the selected field.

Pointer paths need careful wording: `root.pointer.address` resolves the byte position where the pointer itself is
stored, while `root.pointer.value` follows one level and resolves the target position.

## Cost and common mistakes

Debug capture does more work and allocates diagnostic records, so use ordinary reads in hot paths that do not need
byte ranges. Both debug parsing and address resolution require a readable, seekable stream because traversal may
revisit positions.

If a range looks wrong, verify the stream's position at operation entry, packed versus aligned placement, the root
path, array indices, and whether a pointer accessor followed a target. All returned stream positions are absolute;
they include any nonzero root starting position.

See [Paths and selection](../language/paths-and-selection.md) for coordinate rules and
[`DebugData`](xref:CStructSharp.Diagnostics.DebugData) for the generated member reference.
