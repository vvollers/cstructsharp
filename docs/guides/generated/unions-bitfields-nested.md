---
title: Unions, bitfields, and nested structs
description: How generated code reads a union, where bitfield offsets and shifts come from, and how inline and promoted composites become classes.
---

# Unions, bitfields, and nested structs

Composite shapes are where a layout stops being a flat list of numbers. The [unions](../unions.md) and
[bitfields](../../language/bitfields.md) pages explain the rules with diagrams; this lesson shows what the generated
code makes of them.

[!code-csharp[A layout with a bitfield struct, a union, and an inline struct](../../examples/GeneratedExamples.cs#generated-unions-bitfields-nested-class)]

[!code-csharp[Reading and writing it](../../examples/GeneratedExamples.cs#generated-unions-bitfields-nested)]

## Bitfields

`uint8 version : 4; uint8 priority : 3; uint8 urgent : 1;` share one byte. The compiler decides, at build time,
which storage unit each field lives in, at which bit offset, and whether bits are counted from the low or the high
end (`BitfieldPacking` and `BitfieldAllocation` on the attribute). The generated reader takes the unit, extracts the
field's bits with the shared `Codec.ExtractBits`, and stores them in the declared type - a `byte` here. In the
view, each bitfield accessor is a single masked read; in `Offsets`, a bitfield reports its storage unit's first
byte.

Byte `0b1_101_0011` gives `version = 3`, `priority = 5`, `urgent = 1` with the default low-bit-first allocation.

## Unions

A union is a class with every member plus two extras: `RawStorage`, the union's bytes as read, and
`SelectedMember`, which is `null` after a read. Reading fills every member from the same bytes - `Word` and
`Octets` overlap - because the data does not say which one is meant.

Writing has to choose. With `SelectedMember` set to a member's layout name, that member is written and the rest of
the union's extent is zero; with `SelectedMember` left `null`, `RawStorage` is written back unchanged (so a parsed
union round-trips byte for byte). A union whose members have different sizes is written into its full size, the
widest member's, exactly as the runtime writes a `UnionValue`.

## Inline and promoted structs

`struct { uint16 x; uint16 y; } position;` declares a struct in place and names the member. The generator has no
tag to name the class after, so it uses the parent and the member: `Packets.PacketPosition`, reached through
`packet.Position`. Its offsets are known, so `Offsets.Position.X` is a constant too.

A struct declared in place *without* a member name (`struct { uint16 ea_size; uint16 reserved; };`) is
**promoted**: its members belong to the parent, as in C. The generated class splices them in - `packet.EaSize`, not
`packet.Something.EaSize` - and the reader reads them at their promoted offsets, the same way the runtime's
`StructValue` shows them.

## Check yourself

1. After `Packets.Parse`, what is `packet.Body.SelectedMember`?
2. Which member is written when `SelectedMember` is `null`?
3. Why is the inline struct's class called `PacketPosition`?

<details>
<summary>Answers</summary>

1. `null`: the bytes do not say which member is meant; every member and `RawStorage` are filled.
2. None of the members: `RawStorage` is written, which must have the union's size.
3. The struct has no tag, so the generator names it after its parent (`packet`) and the member (`position`).

</details>

## Exercise

Set `packet.Body.SelectedMember = "octets"` and `packet.Body.Octets = [9, 8, 7, 6]`, serialize, and read the
result back. What is `Word`?

<details>
<summary>Solution</summary>

The union's four bytes are `09 08 07 06`; reading them back as the little-endian `uint32 word` gives `0x06070809`.
Both members always describe the same storage.

</details>
