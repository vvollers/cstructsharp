---
title: Inspect byte ranges and addresses
description: Connect decoded values to the exact positions they occupied in a stream.
---

# Inspect byte ranges and addresses

Normal parsing tells you what the data means. Diagnostic parsing also tells you which bytes produced each value.
This is useful for hex viewers, format inspectors, and error reports.

Use `ParseStreamWithDebug` when you need values and ranges together. Use `ResolveAddress` when you need only the
absolute stream position of one path.

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
before position 3. Half-open ranges make the byte count easy to calculate: `EndPos - CurPos`, or `3 - 1 = 2`.

Each `DebugData` record can include its starting and ending stream positions, type name, decoded value, and captured
bytes. Treat it as diagnostic output. It may expose exact input values, so filter it before sending it outside your
application's trusted logs or diagnostic tools.

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
[`DebugData`](xref:CStructSharp.DebugData) for the generated member reference.
