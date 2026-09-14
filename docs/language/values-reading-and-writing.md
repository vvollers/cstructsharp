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

`ReadValue<T>` applies the same decoding and conversion rules, with direct destination filling for eligible fixed layouts:

- numeric conversions are checked for range;
- floating/decimal targets use invariant conversion;
- CLR enums receive the exact numeric payload, including unnamed values;
- arrays and common generic collection interfaces convert item by item; and
- struct/union dictionaries can map to mutable reference-type POCOs.

A supported POCO has a public parameterless constructor and public writable properties or mutable public fields.
Names match exactly first, then by one unambiguous case-insensitive match. Every writable destination member needs a
source member; extra source members may be ignored.

The mapper does not infer pointer following, invoke parameterized constructors, set private members, honor serializer
attributes, or use a serializer package. Missing/ambiguous names and nullability/range problems become
`CStructReadException` with the most specific path available. Do not assume every exception from an application
constructor or setter is converted: unexpected application exceptions may propagate. Compiled accessors invoke
application code directly, without reflection's `TargetInvocationException` wrapper.

`TryReadValue<T>` catches only expected `CStructException` failures, returns `false`, and assigns the default output.
For streams it restores the starting position after that expected failure. Invalid arguments and unexpected runtime
defects are not hidden.

Overloads without a root select the first struct or union in source order. Pass a root explicitly when a layout
contains helper declarations.

## Stream requirements

Read, debug, address, and length operations need a readable, seekable stream because they record and may revisit
positions. `WriteStream` needs a writable, seekable stream. `UpdateStream` needs all three capabilities: readable,
writable, and seekable.

Reads use exact-read behavior: a temporarily short stream read is retried, while a true end of stream becomes
`CStructReadException`.

Successful parse/read/write calls advance the stream according to the consumed value. Failed `TryReadValue`,
`ResolveAddress`, dynamic-length lookup, and `UpdateStream` restore the original position under their documented
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
