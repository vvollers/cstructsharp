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

The examples below build on [the first header parse](install-and-first-parse.md).

## Read a complete dynamic object

This call reads all fields in `header`:

```csharp
dynamic header = layout.Parse(bytes, "header");
ushort kind = header.kind;
uint length = header.length;
```

The field names come from the layout. Because the result is `dynamic`, the C# compiler cannot catch a misspelling
such as `header.lenght`; that error appears at runtime. Dynamic results are useful for exploratory tools and layouts
that are not known when the application is compiled.

For a stream, use `ParseStream`. Reading starts at the stream's current position and a successful read advances past
the selected data:

```csharp
using var stream = new MemoryStream(bytes);
dynamic header = layout.ParseStream(stream, "header");
```

The stream must be readable and seekable. Keep ownership of the stream; CStructSharp does not close it.

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
| Fixed integer or character | Its matching CLR primitive, such as `byte`, `ushort`, or `char` |
| Array | `IList<object?>` |
| Fixed character buffer or terminated text | `string` |
| Struct | `ExpandoObject` |
| Enum | `EnumValueResult` |
| Union | `UnionValue` |
| Pointer | `Pointer` |

These richer enum, union, and pointer objects retain information that a plain integer or dictionary would lose. Keep
them when you intend to write the value back faithfully.

## Stream position and failures

Successful parse and read calls advance a stream through the value they consumed. `TryReadValue<T>` behaves
differently on an expected CStructSharp failure: it restores the stream position, returns `false`, and assigns the
default value to its output.

`ResolveAddress` and `GetDynamicArrayLength` also restore the position because their purpose is inspection rather
than consumption. Do not assume every method has the same position behavior; check the relevant API reference when
combining several operations on one stream.

## Verify and troubleshoot

To verify a selected read:

1. Write down the root's starting stream position.
2. Calculate the target offset from the format.
3. Confirm that the path names and index match the layout exactly.
4. Compare the returned primitive type and value with the bytes.

An `InvalidPath` error means the selector does not match the compiled layout. A `ReadFailed` error means the path was
valid but the bytes could not be decoded, for example because the input was truncated. `ReadLimitExceeded` means the
operation reached a configured array, string, nesting, byte, or pointer limit.

For typed application models, continue with [Map values to C# types](typed-values.md). For the exact path grammar,
including unions and pointer `.address`/`.value` access, see [Paths and selection](../language/paths-and-selection.md).
