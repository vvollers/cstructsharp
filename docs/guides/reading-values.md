---
title: Read values and paths
description: Read a complete layout or select one nested field, array element, union member, or pointer level.
---

# Read values and paths

Use `Parse` when you want a complete struct or union. Use `ReadValue` when you want one value, including a scalar
field deep inside a larger layout.

Before starting, you should have:

- a constructed `CStruct`;
- a byte array, span, memory region, or readable seekable stream;
- the case-sensitive root declaration name; and
- any integer variables needed by runtime-sized arrays.

The result types this guide returns (`StructValue`, `UnionValue`, `EnumValueResult`, `Pointer`, `PrimitiveArray<T>`)
live in the `CStructSharp.Values` namespace; add `using CStructSharp.Values;` next to `using CStructSharp;`.

The examples below build on [the first header parse](install-and-first-parse.md).

## Read a complete struct

This call reads all fields in `header` into a `StructValue`:

```csharp
StructValue header = layout.Parse(bytes, "header");
ushort kind = header.Get<ushort>("kind");
uint length = header.Get<uint>("length");
```

`Get<T>` converts one member with the same checked rules as `ReadValue<T>`: widening is fine, a value that does not
fit throws `CStructReadException`, and a name that does not exist throws `CStructPathException` listing the members
that do. The path form reaches nested values - `packet.Get<byte>("items[2].tag")`, `node.Get<uint>("next.value.id")`
through a dereferenced pointer - and `TryGet<T>` returns `false` instead of throwing.

Two more forms avoid the exception without losing the reason. `TryGet<T>(path, out value, out failure)` hands back
the exception `Get<T>` would have thrown - a `CStructPathException` when the member is not there (a conditional
arm that was not selected, a misspelled name), a `CStructReadException` when it is there but does not convert to
`T` - so code can tell the two apart without a `catch`. `GetOrDefault<T>(path, fallback)` returns the fallback in
both cases and the member otherwise:

[!code-csharp[TryGet with the failure, and GetOrDefault](../examples/Program.cs#api-guide-try-get)]

The same object is also an `IDictionary<string, object?>` (`header["kind"]`, `header.ContainsKey("kind")`,
enumeration in declaration order) and supports `dynamic` member access - see [Dynamic access](#dynamic-access)
below for what that trades away.

A stream works the same way. Reading starts at the stream's current position and a successful read advances past
the selected data:

```csharp
using var stream = new MemoryStream(bytes);
StructValue header = layout.Parse(stream, "header");
```

The stream must be readable and seekable. Keep ownership of the stream; CStructSharp does not close it.

## Dynamic access

A `StructValue` (and a `UnionValue`) can be declared `dynamic`, and then `header.kind` reads the member by name:

```csharp
dynamic header = layout.Parse(bytes, "header");
uint length = header.length;   // bound at run time; header.length is a uint
```

This is a convenience for exploratory tools, scripts, and layouts that are not known when the program is compiled.
It is not the recommended style for application code, because everything the compiler would normally check moves
to run time:

- A misspelled or renamed member (`header.lenght`) compiles and fails when it runs, with the C# runtime binder's
  `RuntimeBinderException` rather than a `CStructPathException` that lists the members the struct has.
- Values keep their storage type: `header.length` is a `uint`, so `int total = header.length` throws at run time
  instead of converting, and any arithmetic on a `dynamic` operand makes the whole expression `dynamic`.
  `Get<int>("length")` performs the checked conversion instead.
- A property read is resolved against the layout's members first, so a field named like a `StructValue` property
  (`Count`, `Keys`) shadows it; methods such as `Get<T>` still bind normally. A nested path is a chain of run-time
  binds (`root.items[2].tag`) where `Get<T>("items[2].tag")` is one call.
- No IntelliSense, rename refactoring, or analyzer help; each call site pays the runtime binder's first-call cost,
  which is far above a dictionary lookup in a loop over many records; and the binder (`Microsoft.CSharp`) is
  linked into the program.

- `dynamic` is JIT-only: the C# runtime binder behind it generates code at run time, so a trimmed or Native AOT
  publish reports `IL2026`/`IL3050` for every `dynamic` operation and the program fails in the binder if they are
  suppressed. `Get<T>`, dictionary indexing, and `ReadValue<T>` work everywhere; see
  [Trimming and Native AOT](trimming-and-native-aot.md#dynamic-access-is-jit-only).

## Read one selected value

A path starts with a root and follows fields with dots. Array indices use square brackets:

```text
packet.payload[1]
```

The runtime-payload example reads a complete packet and then selects its second payload byte:

[!code-csharp[Read a runtime-sized packet and one array element](../examples/Program.cs#language-tutorial-runtime-payload)]

With `COUNT = 3` and bytes `7F 10 20 30`, the results are:

```text
packet.kind       = 0x7F
packet.payload    = [0x10, 0x20, 0x30]
packet.payload[1] = 0x20
```

The path is case-sensitive. Indexing starts at zero, so `[1]` is the second element. CStructSharp walks only the
parts of the layout required to locate and decode that target. A malformed field that occurs later and is unrelated
to the path does not block an earlier selected read.

## Understand untyped results

The non-generic `ReadValue` method returns the direct representation for the selected layout type:

| Layout value | C# result |
| --- | --- |
| Integer, floating-point, Boolean, or character | Its matching CLR primitive, such as `byte`, `ushort`, `float`, `bool`, or `char` |
| Fixed-point value | `double` |
| UUID/GUID | `Guid` |
| Array | `IList<object?>`; a one-dimensional array of a fixed-width number or `bool` is a `PrimitiveArray<T>` whose `Span` exposes the typed values |
| Fixed character buffer or terminated text | `string` |
| Struct | `StructValue` (also `IDictionary<string, object?>`; usable as `dynamic`) |
| Enum | `EnumValueResult` |
| Union | `UnionValue` |
| Pointer | `Pointer` |

These richer enum, union, and pointer objects retain information that a plain integer or dictionary would lose. Keep
them when you intend to write the value back faithfully.

A `StructValue` supports dynamic member access and dictionary lookup. Field names match the layout exactly.
`PrimitiveArray<T>` has a fixed length: you can replace an element, but cannot add or remove one. Its `Span`
accesses typed elements without boxing (wrapping a value in an object); `ToArray()` makes an independent copy.
Multidimensional arrays use nested collections, and text buffers return strings. Editing a parsed value does not
change the input bytes. Use serialization or an explicit update to write those changes.

## Stream position and failures

Successful parse and read calls advance a stream through the value they consumed. `TryReadValue<T>` behaves
differently on an expected CStructSharp failure: it restores the stream position, returns `false`, and assigns the
default value to its output.

`ResolveAddress` and `GetArrayLength` also restore the position because their purpose is inspection rather
than consumption. Do not assume every method has the same position behavior; check the relevant API reference when
combining several operations on one stream.

## Read asynchronously

Every stream read has an awaitable twin - `ParseAsync`, `ReadValueAsync`, `ReadValueAsync<T>`, `ParseWithDebugAsync`,
`ReadValueWithDebugAsync`, `ResolveAddressAsync`, `GetArrayLengthAsync`, and `TryReadValueAsync<T>` - for a
`FileStream` opened for asynchronous I/O, a network stream, or a request body: the bytes are read with
`ReadAsync` while the thread is free, and the value is then decoded by the same reader the synchronous forms use,
with the same limits and messages. A `MemoryStream` that exposes its buffer is read in place and the returned
`ValueTask` is already complete.

[!code-csharp[Parse a file asynchronously](../examples/Program.cs#api-guide-parse-async)]

A seekable stream ends just after the value on success and back at its origin on any failure; a stream that cannot
seek is consumed up to `MaxTotalBytesRead` plus one byte, whatever the value needed. `TryReadValueAsync<T>` returns a
`ReadAttempt<T>` - `Succeeded`, `Value`, `Failure` - because an `out` parameter cannot cross an `await`. The
`cancellationToken` parameter ends the wait for bytes and the decode at its next boundary; it is linked with
`ReadOptions.CancellationToken` when both are given. One difference from the synchronous stream form: the buffered
region starts at the stream's current position, so a stored absolute pointer address counts from that origin (as it
does for a span or memory input), not from the stream's first byte - read a stream whose addresses are absolute
positions from position 0, or use the synchronous form.

## Read a sequence of records

A file or a message body often holds one struct after another - a log of fixed-size entries, a stream of
count-prefixed frames - with nothing else in between. `ParseMany` reads such a sequence one record at a time: each
`MoveNext` of the returned `IEnumerable<StructValue>` parses the next record, so a `foreach` over a million-entry
file holds one value at a time and stops early whenever you `break`.

[!code-csharp[Read records from memory and from a stream](../examples/Program.cs#api-guide-parse-many)]

The root must be a struct declaration (`ParseMany` rejects a union or a scalar root as `Parse` does, before the first
record). A root with a fixed size advances by its size; a root that depends on its own data (a count-prefixed
payload) advances by the bytes the previous record consumed. Trailing bytes shorter than one record are not ignored:
the step that meets them throws, for a fixed-size root with the message a `T v[EOF]` array uses for a partial
element ("The remaining 2 bytes are not a whole number of 5-byte elements"); slice the input first when a trailer is
expected. A failure names the record by its index before the path - `[3].header.length` - and the read limits apply
to each record on its own (`MaxArrayElements` counts the elements *inside* a record; a sequence is not an array).

`ParseMany` takes a `ReadOnlyMemory<byte>`, a `ReadOnlySequence<byte>`, or a seekable `Stream` (read with the
stream reader, byte-exact, the stream left after each record). `ParseManyAsync` returns an `IAsyncEnumerable` for
`await foreach`: a fixed-size root is read exactly one record at a time with `ReadAsync`, which works on a stream
that cannot seek (a socket, a pipe); a runtime-sized root is read through a pooled window of at most
`MaxTotalBytesRead` plus one byte that refills from the start of a record it could not hold, which needs a seekable
stream. In the memory, sequence, and awaitable forms each record is its own region, so a stored absolute pointer
address counts from the record's first byte; the synchronous stream form counts from the stream's first byte, as
`Parse(Stream)` does. The generated series has the typed twin, `Records`, in
[Sequences and TryParse](generated/sequences-and-try-parse.md).

## Verify and troubleshoot

To verify a selected read:

1. Write down the root's starting stream position.
2. Calculate the target offset from the format.
3. Confirm that the path names and index match the layout exactly.
4. Compare the returned primitive type and value with the bytes.

An `InvalidPath` error means the selector does not match the compiled layout. A `ReadFailed` error means the path was
valid but the bytes could not be decoded, for example because the input was truncated. `ReadLimitExceeded` means the
operation reached a configured array, string, nesting, byte, or pointer limit.

For typed application models, continue with [Map values to C# types](typed-values.md); when the layout is known
at build time, the [generated code series](generated/index.md) reads it into generated classes without paths at
all. For the exact path grammar,
including unions and pointer `.address`/`.value` access, see [Paths and selection](../language/paths-and-selection.md).
