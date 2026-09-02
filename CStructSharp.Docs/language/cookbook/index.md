---
title: Portable layout cookbook
description: Adapt tested layout patterns for common headers, mixed byte order, text, enums, unions, bitfields, pointers, and updates.
---

# Portable layout cookbook

Each recipe is backed by either the compiled documentation runner or a named pair in
[`manual-fixtures-v1.json`](../../contracts/language/manual-fixtures-v1.json), executed on .NET 8 and .NET 10.

Before copying one, replace widths, byte order, placement, pointer coordinates, and limits with facts from your
format. Similar-looking bytes do not prove that two formats share the same rules.

## Decode a packed fixed header

Use explicit-width primitives in declaration order:

```c
struct header {
    uint16 kind;
    uint32 length;
};
```

Little-endian bytes `02 00 06 00 00 00` decode as kind 2 and length 6. Packed size is 6. In aligned placement,
`length` would move to offset 4 and total size would become 8.

[!code-csharp[Decode a fixed header](../../examples/Program.cs#api-reference-cstruct)]

Verify the result by checking field widths, the root starting position, and the format's byte order.

## Mix byte orders without moving fields

Use `<` or `>` only on fields whose encoding overrides the layout order:

```c
struct root {
    uint16> network;
    uint16< device;
};
```

Bytes `12 34 78 56` produce `network=4660` and `device=22136`. The fields remain at offsets 0 and 2 and total size is
4. The `alignment-and-endian-overrides` fixture checks this behavior.

## Read a payload whose count comes from elsewhere

```c
struct packet {
    uint8 kind;
    uint8 payload[COUNT];
};
```

[!code-csharp[Supply COUNT and read the payload](../../examples/Program.cs#language-tutorial-runtime-payload)]

Pass the same `COUNT` to every later read, address, length, write, or update. Do not infer the count from remaining
stream bytes.

## Store fixed-capacity text

Use `char[N]` or `wchar[N]` when the format reserves exactly `N` code units:

```c
struct label {
    char text[4];
};
```

[!code-csharp[Read and zero-fill fixed text](../../examples/Program.cs#language-tutorial-fixed-text)]

Writing `"XY"` produces `58 59 00 00`. This is not a terminated scan; a longer value fails.

## Put terminated text before another field

Use a strict named handler:

```c
struct root {
    utf8_string_zero name;
    uint8 flags;
};
```

Bytes `41 00 7E` produce `name="A"` and `flags=126` at offset 2. Set `MaxStringBytes` so a missing terminator cannot
scan beyond the format's expected maximum. The `terminated-strings` and `bounded-failures` pairs check success and a
limit failure.

## Preserve an enum number with no name

```c
enum state : uint32 {
    Known = 1
};

struct root {
    state value;
};
```

[!code-csharp[Keep an unknown enum value](../../examples/Program.cs#api-reference-enum)]

Check `EnumValueResult.Name`; when it is null, retain `Value` and `RawBits` instead of narrowing through `int`.

## Preserve or choose a union member

```c
union choice {
    uint8 small;
    uint16 large;
};
```

[!code-csharp[Round-trip raw union bytes and choose one member](../../examples/Program.cs#api-reference-union)]

Untouched raw storage `34 12` survives exactly. Selecting `small=0xA5` starts from cleared two-byte storage and
produces `A5 00`.

## Decode compact flags

```c
struct flags {
    uint8 low : 3;
    uint8 high : 5;
    uint16 next;
};
```

Bytes `8D 34 12` produce low 5, high 17, and next 4660. Both slices have byte offset 0. Confirm native C bitfield ABI
before translating an existing header; Portable always allocates from the low bit.

## Follow a relative pointer

Choose pointer width from the format and configure address mode:

[!code-csharp[Follow a one-byte absolute pointer](../../examples/Program.cs#api-reference-pointer-read-options)]

For a relative format, use `AddressingMode = Relative` and the correct `Origin`. Zero stays null. Nonzero targets must
fit the stream/memory region and configured traversal limits. Serialization writes a coordinate and does not
relocate target data.

## Patch one field in existing data

Use a path and `UpdateStream` when surrounding bytes and positions must stay fixed:

[!code-csharp[Patch a field after staged validation](../../examples/Program.cs#api-reference-update-options)]

Path, range, shape, and limit failures detected by CStructSharp happen before destination writes. A physical stream
failure during final commit may still leave an accepted prefix.

For new output, use owned or caller-provided serialization:

[!code-csharp[Serialize to an array, span, and buffer writer](../../examples/Program.cs#api-reference-write-options)]

Continue with [Choose an API](../../guides/choosing-an-api.md) for ownership and failure tradeoffs.
