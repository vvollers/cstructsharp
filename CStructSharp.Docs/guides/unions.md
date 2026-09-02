---
title: Read and write unions
description: Inspect overlapping union views, preserve raw bytes, and select a member deliberately when writing.
---

# Read and write unions

A union gives several interpretations to the same bytes. Unlike a struct, its members do not follow one another.
They all begin at the same offset.

```c
union choice {
    uint8 small;
    uint16 large;
};
```

For little-endian bytes `34 12`, `small` reads the first byte as `52`, while `large` reads both bytes as `4660`.
The stored data does not say which interpretation is “active.” That meaning must come from another field or from the
application's format rules.

## Preserve what was read

Parsing a union returns `UnionValue`. It contains:

- a copy of the complete raw union storage;
- every decoded member view; and
- an optional member selection used for writing.

The executable example reads and writes the two-byte union:

[!code-csharp[Preserve raw storage or select one member](../examples/Program.cs#api-reference-union)]

Passing the untouched parsed `UnionValue` to `Serialize` returns the original bytes `34 12`. This is the safe choice
when you do not know which member produced the data or when unused bytes must survive exactly.

## Select a member for new output

For a new union value, make the choice explicit:

```csharp
UnionValue selected = UnionValue.FromMember("choice", "small", (byte)0xA5);
byte[] output = layout.Serialize("choice", selected);
```

The writer clears the full two-byte union storage and then writes `small`, producing `A5 00`. Clearing prevents an
older larger member from leaving unrelated high bytes behind.

`WithSelectedMember` makes a new selection from an existing `UnionValue`. `WithoutSelection` returns to raw
pass-through behavior when raw storage exists.

## Update one member or the whole union

A path such as `root.choice.small` updates only that member's byte range and preserves bytes outside the member. A
path selecting the whole union applies the whole-union policy, including clearing storage for a selected member by
default.

Do not replace a complete union with a dictionary or POCO and expect CStructSharp to infer an active member. Use
`UnionValue` so the intent and raw-storage behavior are explicit.

Common mistakes are assuming the first member is active, keeping only one decoded view after a read, forgetting that
a smaller member leaves other union bytes unexplained, or expecting a tagged union when the format contains no tag.

Continue with [Structs, unions, enums, and typedefs](../language/structs-unions-enums-typedefs.md#unions) for size,
alignment, arrays of unions, and nested struct members.
