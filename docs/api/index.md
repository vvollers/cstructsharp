---
title: API reference
description: Generated reference for the public CStructSharp .NET API.
---

# API reference

Use this section when you already know which class or method you need and want its exact signature, parameters,
return value, or exceptions. If you are still choosing an approach, start with
[Choose the right API](../guides/choosing-an-api.md). That guide compares whole-object parsing, reading one field,
mapping to a C# type, writing, and updating.

Most programs begin with [`CStruct`](xref:CStructSharp.CStruct). A `CStruct` holds a checked and prepared layout
definition. You can reuse that object to:

- parse a complete value;
- read one value selected by a path;
- map a value to a C# type;
- serialize a new value; or
- update a field in an existing stream.

The option classes are grouped by operation:

- [`CStructCompilationOptions`](xref:CStructSharp.CStructCompilationOptions) controls how a definition is prepared;
- [`ReadOptions`](xref:CStructSharp.ReadOptions) controls reads, including pointer behavior and safety limits;
- [`WriteOptions`](xref:CStructSharp.WriteOptions) controls serialization; and
- [`UpdateOptions`](xref:CStructSharp.UpdateOptions) controls in-place updates.

Types such as [`Pointer`](xref:CStructSharp.Values.Pointer), [`UnionValue`](xref:CStructSharp.Values.UnionValue), and
[`EnumValueResult`](xref:CStructSharp.Values.EnumValueResult) preserve details that a plain C# number or object would lose.
The task guides explain when those result types appear and how to use them.

The namespaces group the public surface by role:

| Namespace | Contents |
| --- | --- |
| `CStructSharp` | `CStruct` and the option types on its methods; `CStructLayoutAttribute`/`CStructMappedAttribute` for the source generator, `ICStructMapped<T>` and `ICStructGenerated<T>` that mapped and generated classes implement, and `MappedTypes` |
| `CStructSharp.Values` | What reads return and writes accept: `StructValue`, `UnionValue`, `EnumValueResult`, `FlagValueResult`, `Pointer`, `PrimitiveArray<T>` |
| `CStructSharp.Introspection` | `LayoutInfo` and the records that describe a compiled layout's declarations, fields, and constants |
| `CStructSharp.Diagnostics` | The exception family, `CStructErrorCode`, and `DebugData` |
| `CStructSharp.Codecs` | `ICustomCodec`, the extension point for caller-defined primitive types |
| `CStructSharp.Generated` | The support the code emitted by the `[CStructLayout]` generator calls: `Codec` (the byte-level rules, shared with the runtime reader and writer), `ReadCursor`/`WriteCursor` (the runtime's accounting and diagnostics over spans), `CompositeCursor` (field placement: alignment and bitfield packing), `Pointer<T>`, and `Expressions` (the layout expression operators). Application code reads through a generated layout class or `CStruct` instead |
| `CStructSharp.Memory` and `.Memory.Metadata` | Address spaces, metadata import, sessions, traversal, and offline patches for memory images |

## Where this reference comes from

DocFX generates these pages from the `Release/net10.0` core assembly and its XML comments. The compatibility checks
also compare the public signatures produced for .NET 8 and .NET 10.

The reviewed signature list is named `managed-rc1` and is stored under
[`contracts/api/managed-rc1`](../../contracts/api/managed-rc1/manifest.json). An *API baseline* is a saved description
of the public surface. It lets maintainers notice a changed method, default value, nullability annotation, or
attribute during review instead of after packaging.

The optional WebAssembly adapter has an independently versioned
[browser interface](browser-contract.md). You do not need that page when using CStructSharp from an ordinary .NET
application.
