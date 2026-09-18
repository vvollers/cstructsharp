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

<svg class="byte-grid" role="img" viewBox="0 0 514 162" width="514" height="162" xmlns="http://www.w3.org/2000/svg" font-size="12">
  <title>Both members of union choice read the same two bytes</title>
  <text x="116" y="18" text-anchor="middle" fill="currentColor" opacity="0.7" font-size="11">0</text>
  <text x="150" y="18" text-anchor="middle" fill="currentColor" opacity="0.7" font-size="11">1</text>
  <text x="6" y="43" fill="currentColor">bytes</text>
  <rect x="99" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="133" y="24" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <text x="116" y="43" text-anchor="middle" fill="currentColor">34</text>
  <text x="150" y="43" text-anchor="middle" fill="currentColor">12</text>
  <text x="6" y="81" fill="currentColor">uint8 small</text>
  <rect x="133" y="62" width="34" height="30" fill="none" stroke="currentColor" stroke-opacity="0.35"/>
  <rect x="100" y="63" width="32" height="28" fill="var(--cstruct-accent-soft, #dbeafe)" stroke="var(--cstruct-accent, #2563eb)" stroke-width="1.5"/>
  <text x="116" y="81" text-anchor="middle" fill="currentColor">52</text>
  <text x="6" y="119" fill="currentColor">uint16 large</text>
  <rect x="100" y="101" width="66" height="28" fill="var(--cstruct-accent-soft, #dbeafe)" stroke="var(--cstruct-accent, #2563eb)" stroke-width="1.5"/>
  <text x="133" y="119" text-anchor="middle" fill="currentColor">4660</text>
  <text x="6" y="150" fill="currentColor" opacity="0.7" font-size="11">every member starts at offset 0; the union is as large as its widest member</text>
</svg>

## Preserve what was read

Parsing a union returns `UnionValue` (from `CStructSharp.Values`). It contains:

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
