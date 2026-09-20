---
title: Trimming and Native AOT
description: Publish a trimmed or Native AOT application that uses CStructSharp - what works unchanged, the two POCO conventions, and why dynamic access is JIT-only.
---

# Trimming and Native AOT

CStructSharp ships as a trimmable library on both targets and declares Native AOT compatibility on .NET 10. A
published Native AOT program runs every operation: parsing, selected reads, typed reads into your own classes,
writes from classes and dictionaries, updates in place, debug reads, and the diagnostics. This page says what that
promise covers, the two conventions your own mapped classes must follow, and why `dynamic` stays on the JIT.

The repository proves the page on every push: `tests/CStructSharp.AotConsumer` publishes with `PublishAot=true`,
reports zero trim or AOT warnings, and runs the cases below.

## What the package declares

| Target | `IsTrimmable` | `IsAotCompatible` | Meaning for your publish |
| --- | --- | --- | --- |
| net10.0 | yes | yes | `PublishTrimmed` and `PublishAot` produce no warnings from the package; the trim and AOT analyzers have verified every code path |
| net8.0 | yes | no claim | `PublishTrimmed` works the same way; the package makes no Native AOT claim on .NET 8 because that SDK's analyzer cannot verify the library's dynamic-code guard (the AOT consumer runs on .NET 10) |

Nothing in the library needs runtime code generation: expression trees are used only for the compiled POCO
accessors, behind a feature switch the trimmer understands, and the value objects implement
`IDynamicMetaObjectProvider` directly instead of deriving from `DynamicObject`, whose constructor requires dynamic
code. That keeps the *library* clean; a `dynamic` call site in your own code is a different matter (see
[dynamic access](#dynamic-access-is-jit-only)).

## Publish

Add the usual properties to the application project; the library needs no settings of its own:

```xml
<PropertyGroup>
  <PublishAot>true</PublishAot>
  <!-- or PublishTrimmed for a trimmed JIT publish -->
</PropertyGroup>
```

```sh
dotnet publish -c Release -r linux-x64
```

Parsing, selected reads, `Get<T>` of primitives, and writes from dictionaries or parsed values use no reflection.
Reflection is used in one place - mapping bytes to and from your own classes (`ReadValue<T>`, `TryReadValue<T>`,
`Get<T>` of a class, `Serialize`, `Write`, `Update` with a class instance) - and that is where the two conventions
below apply.

## Convention 1: keep the members of nested mapped classes

The class you name in `ReadValue<T>`, `TryReadValue<T>`, `Get<T>`, or `TryGet<T>` is annotated on the method
itself, so the trimmer keeps its public parameterless constructor, public properties, and public fields
automatically. Classes reached *through* it - a member of class type, the element type of an array or list - and
any object you hand to a write are known only at run time. Mark them:

```csharp
using System.Diagnostics.CodeAnalysis;

[DynamicallyAccessedMembers(
    DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
    DynamicallyAccessedMemberTypes.PublicProperties |
    DynamicallyAccessedMemberTypes.PublicFields)]
public sealed class Point
{
    public short X { get; set; }
    public short Y { get; set; }
}

public sealed class Record
{
    public byte Tag { get; set; }
    public Point Origin { get; set; } = new();
    public Point[] Corners { get; set; } = [];
    public byte[] Flags { get; set; } = [];
}
```

```csharp
var layout = new CStruct("struct point { int16 x; int16 y; }; struct record { uint8 tag; point origin; point corners[2]; uint8 flags[3]; };");
Record record = layout.ReadValue<Record>(bytes, "record");   // Record is preserved by the method's own annotation
byte[] written = layout.Serialize("record", record);          // Point's members are preserved by the attribute
```

A trimmer root descriptor (`TrimmerRootDescriptor`) that lists the classes works as well. A member the trimmer
removed does not fail silently: the read reports a `CStructReadException` saying the source member is missing on
the target type.

## Convention 2: declare collections as `List<T>` or `T[]`

A member declared as an interface - `IList<T>`, `ICollection<T>`, `IEnumerable<T>`, `IReadOnlyList<T>` - needs a
`List<T>` created at run time for an element type the compiler never saw, which is exactly what Native AOT cannot
do. Declare the member as `List<Point>` or `Point[]` and the mapping is static.

```csharp
public sealed class InterfaceRecord
{
    public byte Tag { get; set; }
    public IList<Point> Corners { get; set; } = [];   // works on the JIT; fails under Native AOT
}
```

Nothing about this fails at build or publish time. On a JIT runtime the member works as before. Under Native AOT
the `ReadValue<InterfaceRecord>` call throws a `CStructReadException` naming the fix:

```text
Cannot map 'record.corners' to 'System.Collections.Generic.IList`1[…Point…]' without dynamic code: declare the
member as List<Point> or Point[] when publishing with Native AOT.
```

Every other operation in the same program is unaffected; only that mapping is.

## Dynamic access is JIT-only

`dynamic header = layout.Parse(bytes, "header"); header.kind` compiles to a call into the C# runtime binder
(`Microsoft.CSharp.RuntimeBinder`), and that binder generates code at run time. The SDK says so at publish time -
every `dynamic` operation in your code reports `IL2026` under trimming and `IL3050` under Native AOT ("The
'dynamic' feature requires runtime-code generation, which is incompatible with AOT") - and if the warnings are
suppressed, the published program fails inside the binder on the first dynamic member access. This is a property
of C#'s `dynamic`, not of CStructSharp; the library's own values are AOT-clean.

In a trimmed or AOT application read members with `Get<T>`/`TryGet<T>`, index the value as a dictionary
(`header["kind"]`), or map to a class with `ReadValue<T>`:

```csharp
StructValue header = layout.Parse(bytes, "header");
ushort kind = header.Get<ushort>("kind");        // instead of (ushort)dynamicHeader.kind
object? raw = header["length"];                   // the stored value, untyped
```

[Read values and paths](reading-values.md#dynamic-access) lists what `dynamic` trades away on the JIT as well.

## The WebAssembly bridge

The browser runtime is the same library published trimmed (`TrimMode=full`) inside the WASM bridge. It never maps
POCOs, so the bridge switches the library's `CStructSharp.CompiledAccessors` feature off and the trimmer drops the
expression-tree accessor path and `System.Linq.Expressions` with it; the details are in
[web development](../project/web-development.md#managed-bridge-trimming).

## Checklist

- `PublishAot`/`PublishTrimmed` on the application; nothing on the package.
- `[DynamicallyAccessedMembers(...)]` (or a root descriptor) on every class reached through a mapped class and on
  every class handed to a write.
- Collection members as `List<T>` or `T[]`, never a collection interface.
- No `dynamic` in the application: `Get<T>`, dictionary indexing, or `ReadValue<T>` instead.
- Run the published binary once through a typed read and a write; a removed member or an interface member surfaces
  as a `CStructReadException` with the remedy in the message.

Next: [Typed values](typed-values.md) for the mapping rules themselves, and [performance](performance.md) for
what a published program spends per operation.
