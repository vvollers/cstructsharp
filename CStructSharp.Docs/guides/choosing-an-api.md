---
title: Choose an API
description: Choose a CStructSharp read or write method based on your input, desired result, and ownership needs.
---

# Choose an API

CStructSharp offers several entry points because binary data arrives in different forms and applications need
different results. Make the choice in three parts:

1. Is the input in memory or in a stream?
2. Do you want a whole dynamic object, one selected value, or a C# type?
3. Are you creating new bytes, writing to a destination, or changing bytes that already exist?

Start with the simplest method that matches the job. A `byte[]` result is often easier to use than a custom buffer
until measurements show that allocation matters.

## Choose an input

| Your data is in | Use | What happens |
| --- | --- | --- |
| `byte[]`, `ReadOnlySpan<byte>`, or `ReadOnlyMemory<byte>` | `Parse` or `ReadValue` | The call is synchronous and does not retain the input. |
| A readable, seekable `Stream` | `ParseStream` or `ReadValue` | Reading begins at the stream's current position. |

A *span* is a short-lived view over a section of memory. `ReadOnlyMemory<byte>` is a storable memory object, but the
CStructSharp operation still finishes synchronously and does not keep it. Use a stream for files or data sources that
already expose seeking. Use memory APIs when the bytes are already available as an array or memory region.

Pointer coordinates in memory APIs start at zero within the region you pass. If you pass a slice, a pointer cannot
refer to bytes before that slice.

## Choose a read result

| Result you need | Method | Use it when |
| --- | --- | --- |
| A whole struct or union with runtime field names | `Parse` / `ParseStream` | Exploring a format, building tools, or handling layouts that vary at runtime |
| One field or nested value | `ReadValue` | You don't need the rest of the object |
| A known C# type | `ReadValue<T>` | Application code benefits from typed properties and checked conversion |
| A known C# type with an expected failure path | `TryReadValue<T>` | Truncated or malformed input is an ordinary outcome |
| Values plus byte ranges | `ParseStreamWithDebug` | A hex viewer or diagnostic tool must show where values came from |
| Only a field's stream position | `ResolveAddress` | You need a coordinate without materializing the value |
| An array or terminated string length | `GetDynamicArrayLength` | The count depends on variables or scanned input |

`Parse` is for composite values: structs and unions. `ReadValue` also handles scalars, array elements, enum values,
pointer parts, and other selected fields.

The untyped `ReadValue` result uses the library's direct C# representation. For example, `uint16` becomes `ushort`,
a struct becomes an `ExpandoObject`, an enum becomes `EnumValueResult`, and a union becomes `UnionValue`.
`ReadValue<T>` performs an additional checked mapping to your requested type.

## Choose a write operation

| What you want to do | Method | Ownership and failure behavior |
| --- | --- | --- |
| Create a new `byte[]` | `Serialize` returning `byte[]` | The library allocates and returns an exact-sized array. |
| Fill an existing `Span<byte>` | `Serialize(Span<byte>, ...)` | Returns the number of initialized bytes; unused capacity is unchanged. |
| Append to a pipeline or pooled writer | `Serialize(IBufferWriter<byte>, ...)` | Appends directly and returns the byte count. |
| Write at a stream's current position | `WriteStream` | Writes directly to a writable, seekable stream. |
| Replace a value already present in a stream | `UpdateStream` | Locates the path and validates the replacement before committing it. |

The `byte[]` overload is the easiest choice for most new code. Span and buffer-writer output avoid the final owned
array, but they cannot undo a prefix that was already initialized or advanced if a later write fails. `WriteStream`
can likewise leave earlier fields written after a later error.

`UpdateStream` is different: it is for fixed coordinates in existing data. It will not extend the stream or move
following fields. Errors CStructSharp can detect are found before it writes to the destination, although a physical
stream failure during the final commit may still leave a written prefix.

## A practical decision path

For a small file already loaded into a `byte[]`:

1. Start with `Parse(bytes, "root")` while learning the format.
2. Change to `ReadValue<MyType>(bytes, "root")` when the C# shape is stable.
3. Use `ReadValue(bytes, "root.header.flags")` when only one field is needed.
4. Start writes with `Serialize("root", value)`.
5. Consider spans or `IBufferWriter<byte>` only after measuring allocation in the real workload.

For a large file:

1. Open a readable, seekable stream.
2. Set `Position` to the start of the structure.
3. Use a selected `ReadValue` when later fields are irrelevant.
4. Use `UpdateStream` only when the existing field's storage plan must stay in place.

## Common mistakes

- Calling `Parse` for a scalar path and expecting every operation to return the same dynamic wrapper. Use
  `ReadValue` for one scalar.
- Omitting the root name in a layout with helper declarations. Pass the case-sensitive root explicitly.
- Sharing one stream between concurrent calls. The compiled layout is reusable; the stream is mutable and needs
  exclusive use for the complete operation.
- Choosing span output only because it sounds faster. It requires capacity planning and has weaker rollback behavior
  than staging a `byte[]`.
- Using `WriteStream` to patch existing data. It writes new output from the current position; `UpdateStream` first
  finds existing storage by path.

Continue with [Read values and paths](reading-values.md), [Map values to C# types](typed-values.md), or
[Write and serialize values](writing-and-serialization.md).
