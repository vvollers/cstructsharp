---
title: Write and serialize values
description: Create binary data as an owned byte array, an existing span, a buffer writer, or a stream.
---

# Write and serialize values

Reading turns bytes into values. Serialization does the reverse: it checks that a C# value matches a selected layout
and encodes the value as bytes.

Before writing, construct the same `CStruct` configuration you use for reading. Byte order, alignment, pointer size,
runtime variables, and the selected root must describe the destination format. A different choice can produce valid
bytes for the wrong format.

## Start with an owned byte array

For most application code, start with the overload that returns `byte[]`:

```csharp
byte[] output = layout.Serialize("sample", value);
```

The library creates an exact-sized result and owns any staging required during the operation. This is the simplest
choice when the result will be sent, saved, or passed to another API.

The executable round-trip example reads this layout and writes it in three ways:

```c
struct sample {
    uint16 id;
    uint8 flags;
};
```

[!code-csharp[Create owned and caller-provided output](../examples/Program.cs#api-reference-write-options)]

For `id = 0x1234` and `flags = 0xA5`, every output form produces:

```text
34 12 A5
└ id ┘ flags
```

The first two bytes are little-endian `0x1234`.

## Supply a value with the correct shape

A struct can be supplied as:

- the dynamic object returned by parsing;
- a dictionary or `ExpandoObject` with matching member names; or
- a POCO with readable public properties or fields.

All required fields must be present and convertible to the declared layout type. A fixed array must have exactly the
declared number of elements. A numeric value must fit its width. Null is valid only for a scalar pointer, where it
encodes address zero.

Enums and unions need extra care. Keep `EnumValueResult` when an unknown enum value must survive a round trip. Keep
an unmodified `UnionValue` to preserve all overlapping raw bytes, or explicitly select a member before writing a new
union value.

## Write into existing storage

Use `Serialize(Span<byte>, ...)` when you already have a writable memory region:

```csharp
Span<byte> destination = stackalloc byte[8];
int written = layout.Serialize(destination, "sample", value);
ReadOnlySpan<byte> result = destination[..written];
```

The return value is the number of bytes initialized at the beginning of the span. Capacity after that prefix remains
unchanged. If the span is too small, serialization fails.

Use `Serialize(IBufferWriter<byte>, ...)` for pipelines, pooled writers, or other APIs that expose
`IBufferWriter<byte>`. CStructSharp requests writable windows, fills them, advances the writer, and returns the
number of appended bytes.

These two forms use storage owned by the caller. If a later field fails after earlier output has been initialized or
advanced, CStructSharp cannot roll that prefix back. Stage through the `byte[]` overload when all-or-nothing output is
more important than avoiding an allocation.

## Write to a stream

`WriteStream` writes at a writable, seekable stream's current position:

```csharp
using var stream = new MemoryStream();
layout.WriteStream(stream, "sample", value);
```

The stream remains open and belongs to the caller. Direct stream writing is not transactional: a later conversion or
physical write failure can leave earlier bytes written. To change an existing field while keeping surrounding bytes
in place, use [Update existing data](updating-existing-data.md) instead.

## Verify output

For a new format integration:

1. Check the returned length against the expected layout size.
2. Compare the result with a known byte fixture, not only with another implementation using the same assumptions.
3. Parse the output with the same layout and compare meaningful values.
4. Remember that alignment padding may be normalized to zero. Untouched union raw storage is the explicit
   byte-preserving case.

Most write failures come from a missing member, the wrong collection length, a number outside its declared range, an
incorrect enum or union shape, insufficient output capacity, or a configured safety limit. Inspect
`CStructWriteException.Path` when available before changing the layout.

Use [Spans and buffer writers](spans-and-memory.md) for more ownership detail, or
[Enums](enums.md) and [Unions](unions.md) for their lossless write models.
