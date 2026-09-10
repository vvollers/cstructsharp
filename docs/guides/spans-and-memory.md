---
title: Use spans, memory, and buffer writers
description: Read from in-memory bytes and write into storage supplied by your application.
---

# Use spans, memory, and buffer writers

The simplest CStructSharp workflow takes a `byte[]` as input and returns a new `byte[]` as output. The span, memory,
and buffer-writer overloads are useful when your application already owns the storage and wants to avoid another
complete array.

The API reference calls this *caller-owned output*: your code supplies the destination, and CStructSharp initializes
or appends to it. The term describes ownership, not a different binary format.

If these .NET types are unfamiliar, start with the normal array overloads. Change the code only after a measurement
shows that the extra allocation or stream adapter matters.

## Read from in-memory data

`ReadOnlySpan<byte>` is a temporary view over a continuous block of bytes. `ReadOnlyMemory<byte>` is a memory value
that can be stored and passed around. CStructSharp accepts both, performs the read synchronously, and does not keep a
reference to the input after the method returns.

```csharp
ReadOnlySpan<byte> bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00];
dynamic header = layout.Parse(bytes, "header");
```

The first-parse example compiles and executes this call. Because no stream object is created by your code, this is a
good fit for bytes received from a network API, a memory-mapped region already exposed as memory, or an array that is
already in your process.

Coordinates start at zero within the region you supply. If you pass `largeBuffer.AsSpan(100, 20)`, offset 0 means
index 100 in the original array, and pointers cannot follow bytes outside those 20 supplied bytes.

## Write to a span

A writable `Span<byte>` gives CStructSharp a fixed-capacity destination:

[!code-csharp[Write to a span and a buffer writer](../examples/Program.cs#api-reference-write-options)]

In the example, the value needs three bytes. The call returns `3`, initializes only `destination[..3]`, and leaves
the next byte at its original `0xCC` value.

Always use the returned count:

```csharp
int written = layout.Serialize(destination, "sample", value);
ReadOnlySpan<byte> initialized = destination[..written];
```

The destination must be large enough. CStructSharp does not resize a span. If you cannot calculate a safe capacity,
use the `byte[]` overload or an `IBufferWriter<byte>`.

## Append to an IBufferWriter

`IBufferWriter<byte>` is a .NET interface used by pipelines and pooled buffers. A producer requests a writable
window, fills it, and tells the writer how many bytes were used. CStructSharp performs those steps and returns the
total number of appended bytes.

`ArrayBufferWriter<byte>` is a convenient resizable implementation, as shown in the executable example. In a
pipeline, you can supply the writer provided by that pipeline instead.

Pointer coordinates are relative to the start of the newly appended CStructSharp region, not to bytes that were
already present in the writer.

## Ownership and partial output

The application owns the span, memory, array, or writer. Do not modify input while a synchronous call is reading it,
and do not let concurrent calls write to the same destination without synchronization.

Span and buffer-writer output cannot be rolled back after a prefix has been initialized or a writer window has been
advanced. A failure in a later field may therefore leave partial output. When a destination needs all-or-nothing
behavior, first call the overload that returns a new `byte[]`, then copy or append the completed result.

Common mistakes are ignoring the returned byte count, assuming unused span capacity was cleared, passing a slice that
omits a pointer target, or treating `ReadOnlyMemory<byte>` as an asynchronous retained input. All current memory
operations finish before returning.

See [Write and serialize values](writing-and-serialization.md) for the simpler array and stream choices, or
[Use CStructSharp efficiently](performance.md) before changing code solely for performance.
