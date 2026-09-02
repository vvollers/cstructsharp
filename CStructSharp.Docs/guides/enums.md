---
title: Preserve exact enum values
description: Handle known and unknown enum numbers without losing width, signedness, or raw bits.
---

# Preserve exact enum values

An enum gives names to integer values, but data written by another version of a program may contain a number your
current layout does not name. That number is still valid binary data.

CStructSharp returns `EnumValueResult` rather than narrowing every enum to a C# `int`. The result retains:

- the enum declaration and optional member name;
- the exact mathematical value;
- the raw stored bits;
- the backing width and signedness; and
- the backing storage type.

This matters for unsigned 32-bit and 64-bit values that do not fit in a signed 32-bit integer.

## Read an unknown value

The executable example declares one known value:

```c
enum state : uint32 {
    Known = 1
};

struct root {
    state value;
};
```

It then reads `FF FF FF FF`:

[!code-csharp[Keep an unknown 32-bit enum value](../examples/Program.cs#api-reference-enum)]

The result has:

```text
Value    = 4294967295
RawBits  = 0xFFFFFFFF
BitWidth = 32
IsSigned = false
Name     = null
```

Check `Name` before branching on a declared member. A null name means “this number is not declared here,” not “the
input is corrupt.”

## Write enums without narrowing

For a faithful read-modify-write cycle, keep and pass the original `EnumValueResult`. You can also write:

- a declared member name;
- a CLR integral value that fits the backing type;
- a `BigInteger`;
- an invariant decimal integer string; or
- a compatible object carrying consistent enum metadata.

Contradictory metadata, a number outside the backing range, a boolean, or a fractional value produces
`CStructWriteException`. The writer never truncates high bits to make the value fit.

An enum without an explicit backing type uses unsigned one-byte storage. This differs from C compilers, which may
choose an integer representation according to ABI rules. State the backing type when the binary format depends on a
particular width.

## Map to a CLR enum

`ReadValue<MyEnum>` can map the exact numeric payload to a C# enum. CLR enums can hold values that have no named
member, so the conversion does not make an unknown payload invalid. If your application must preserve width,
signedness, or raw-bit details, read `EnumValueResult` instead of discarding that information.

Common mistakes are casting through `int`, treating `Name == null` as a read error, assuming the default backing is a
C `int`, or serializing only the display name after receiving an unknown value.

See [Enums in the layout language](../language/structs-unions-enums-typedefs.md#enums) for expressions and supported
backing types.
