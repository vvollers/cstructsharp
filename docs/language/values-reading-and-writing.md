---
title: Read results, streams, memory, and variables
description: Understand direct and typed values, stream requirements, memory ownership, and per-operation variables.
---

# Read results, streams, memory, and variables

`ReadValue` reads one selected target. It does not decode unrelated later siblings. The result depends on the layout
type:

| Layout shape | Direct result |
| --- | --- |
| Integer, floating-point, Boolean, or character | Matching CLR primitive |
| Fixed-point value | `Double` |
| UUID/GUID | `Guid` |
| Fixed/runtime array | `IList<object?>` (`PrimitiveArray<T>` for one-dimensional numeric/`bool` arrays: typed `Span`, fixed size) |
| Fixed character buffer or terminated text | `string` |
| Enum | `EnumValueResult` |
| Pointer | `Pointer` |
| Struct | `StructValue` (a `dynamic`-friendly `IDictionary<string, object?>`) |
| Union | `UnionValue` |

For a pointer path, `.address` returns the stored non-negative `long`; `.value` returns the target or null for a null
pointer.

## Typed reads

`ReadValue<T>` applies the same decoding and conversion rules (fully fixed layouts decode through the static read plan):

- numeric conversions are checked for range;
- floating/decimal targets use invariant conversion;
- CLR enums receive the exact numeric payload, including unnamed values;
- arrays and common generic collection interfaces convert item by item; and
- a struct value maps to a class implementing `ICStructMapped<T>` through that class's own `ReadFrom`.

A mapped class is registered with `MappedTypes.Register<T>()` (generated classes register from a module initializer); nothing about it is discovered by reflection.
A generated mapper matches each property to a member exactly first, then by one unambiguous case-insensitive
match, then by one match that ignores underscores; `[CStructMember("name")]` names the member directly. Every
mapped property needs a source member; extra source members are ignored.

The mapper does not infer pointer following, invoke parameterized constructors, set private members, honor serializer
attributes, or use a serializer package. Missing/ambiguous names and nullability/range problems become
`CStructReadException` with the most specific path available. Do not assume every exception from an application
constructor or setter is converted: unexpected application exceptions may propagate. `ReadFrom` and `WriteTo` are
ordinary code, so an application exception surfaces as itself, without a reflection wrapper.

`TryReadValue<T>` catches only expected `CStructException` failures, returns `false`, and assigns the default output.
For streams it restores the starting position after that expected failure. Invalid arguments and unexpected runtime
defects are not hidden.

Overloads without a root select the first struct or union in source order. Pass a root explicitly when a layout
contains helper declarations.

## Stream requirements

Read, debug, address, and length operations need a readable, seekable stream because they record and may revisit
positions. `Write` needs a writable, seekable stream. `Update` needs all three capabilities: readable,
writable, and seekable.

Reads use exact-read behavior: a temporarily short stream read is retried, while a true end of stream becomes
`CStructReadException`.

Successful parse/read/write calls advance the stream according to the consumed value. Failed `TryReadValue`,
`ResolveAddress`, dynamic-length lookup, and `Update` restore the original position under their documented
conditions.

## Span and memory input

`Parse`, `ReadValue`, and `TryReadValue<T>` accept `byte[]`, `ReadOnlySpan<byte>`, or `ReadOnlyMemory<byte>`. They complete
synchronously and do not retain the caller's region. Pointer coordinates start at zero inside that region.

Serialization can fill a writable span or append to `IBufferWriter<byte>`. It returns the initialized/appended count.
Unused span capacity stays unchanged. These destinations cannot roll back a prefix after a late error; stage through
the `byte[]` overload when the caller needs all-or-nothing output.

## Variables and options

Variable-bearing operations accept `IReadOnlyDictionary<string, int>`. The operation copies the entries, combines
them with layout `#define` values, and gives a caller entry precedence on a name collision. It never changes the
caller's collection.

`CStructCompilationOptions`, `ReadOptions`, `WriteOptions`, and `UpdateOptions` use init-only properties. Configure
the complete policy in an object initializer and reuse it. Each operation reads the supplied values at entry.

A selected path walks only what is needed to reach and decode that target. Array, string, structure, and pointer work
used while locating the target still counts toward the same per-operation limits.

The [reading guide](../guides/reading-values.md), [typed guide](../guides/typed-values.md), and
[span guide](../guides/spans-and-memory.md) provide runnable application examples.
