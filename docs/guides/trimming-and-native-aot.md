---
title: Trimming and Native AOT
description: Publish a trimmed or Native AOT application that uses CStructSharp - what works unchanged, how generated layouts and mapped classes stay reflection-free, and why dynamic access is JIT-only.
---

# Trimming and Native AOT

CStructSharp ships as a trimmable library on both targets and declares Native AOT compatibility on .NET 10. A
published Native AOT program runs every operation: parsing, selected reads, typed reads into your own classes,
generated layouts and views, writes from classes and dictionaries, updates in place, debug reads, and the
diagnostics. This page says what that promise covers, how generated code and your own mapped classes take part
without reflection, and why `dynamic` stays on the JIT.

The repository proves the page on every push: `tests/CStructSharp.AotConsumer` publishes with `PublishAot=true`,
reports zero trim or AOT warnings, and runs the cases below, generated layout and mapped class included.

## What the package declares

| Target | `IsTrimmable` | `IsAotCompatible` | Meaning for your publish |
| --- | --- | --- | --- |
| net10.0 | yes | yes | `PublishTrimmed` and `PublishAot` produce no warnings from the package; the trim and AOT analyzers have verified every code path |
| net8.0 | yes | no claim | `PublishTrimmed` works the same way; the package makes no Native AOT claim on .NET 8 because that SDK's analyzer cannot verify the library's dynamic-code guard (the AOT consumer runs on .NET 10) |

Nothing in the library needs runtime code generation or reflection: a `[CStructLayout]` class is ordinary C#
the generator wrote at build time, mapping to and from your classes goes through `ICStructMapped<T>` (static
code the generator or you write), and the value objects implement
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

Nothing the library does involves reflection. Parsing, selected reads, `Get<T>` of primitives, strings, enums,
arrays, and values, and writes from dictionaries or parsed values are plain code. Mapping bytes to and from your
own classes is plain code too: a class becomes a mapping target by implementing `ICStructMapped<T>` - two static
members the `[CStructMapped]` source generator writes for a `partial` class, or that you write by hand - and by
registering itself with `MappedTypes.Register<T>()`. There is no annotation to add and no member for the trimmer to
lose.

## Generated layouts

A `[CStructLayout]` class is the most direct AOT path: the generator turns the layout into readers, writers,
views, and typed setters at build time, so the published program contains straight-line code with the offsets
filled in and nothing for the trimmer to remove. The generator runs inside the compiler and ships in the package
as an analyzer; `PublishAot` and `PublishTrimmed` need no extra setting. The AOT consumer runs this class:

```csharp
[CStructLayout("struct point { int16 x; int16 y; }; struct record { uint8 tag; point origin; point corners[2]; uint8 flags[3]; };")]
public static partial class Shapes { }

Shapes.Record generated = Shapes.Parse(recordBytes);          // typed properties, no StructValue in between
byte[] written = Shapes.Serialize(generated);
var view = new Shapes.RecordView(recordBytes);                // allocation-free
Shapes.Update.Tag(edited, 9);                                 // a typed setter at a build-time offset
```

The [generated code series](generated/index.md) teaches the path; the analyzer's `CSG300` warns when a project
that publishes trimmed or AOT binds a parsed value as `dynamic`.

The awaitable and sequence forms are as AOT-clean as the rest: `ParseAsync`, `WriteAsync`, `TryParse`, `Records`,
the view enumerator, `ParseManyAsync`, the layout class's `ReadValue<T>`, `TryGet`, `GetOrDefault`, and a `with`
copy of the options are all run by the AOT consumer after publishing, with no IL warnings. Iterators and async
methods compile to ordinary state-machine classes; nothing in them is discovered at run time.

## Mapped classes

A `[CStructMapped]` class needs only the attribute; the AOT consumer maps this record with a generated mapper:

```csharp
[CStructMapped(Layout = "record")]
public sealed partial class MappedRecord
{
    public byte Tag { get; set; }
    public Point Origin { get; set; } = new();
    public IList<Point> Corners { get; set; } = [];
    public byte[] Flags { get; set; } = [];
}
```

`Point` here is the hand-written class below (a mapped property's class must be `[CStructMapped]` or implement
`ICStructMapped<T>` itself). The classes written by hand show what the generator produces and how a project
without the generator takes part:

```csharp
using System.Runtime.CompilerServices;
using CStructSharp;
using CStructSharp.Values;

public sealed class Point : ICStructMapped<Point>
{
    public short X { get; set; }
    public short Y { get; set; }

    public static Point ReadFrom(StructValue source) => new() { X = source.Get<short>("x"), Y = source.Get<short>("y"), };

    public static void WriteTo(Point value, StructValue target)
    {
        target["x"] = value.X;
        target["y"] = value.Y;
    }

    // Runs before any other code in the assembly, on every runtime; generated classes register the same way.
    [ModuleInitializer]
    internal static void Register() => MappedTypes.Register<Point>();
}

public sealed class Record : ICStructMapped<Record>
{
    public byte Tag { get; set; }
    public Point Origin { get; set; } = new();
    public Point[] Corners { get; set; } = [];
    public byte[] Flags { get; set; } = [];

    public static Record ReadFrom(StructValue source) => new()
    {
        Tag = source.Get<byte>("tag"),
        Origin = source.Get<Point>("origin"),        // a nested mapped class
        Corners = source.Get<Point[]>("corners"),    // an array of them
        Flags = source.Get<byte[]>("flags"),
    };

    public static void WriteTo(Record value, StructValue target)
    {
        target["tag"] = value.Tag;
        target["origin"] = value.Origin;             // nested instances are mapped in turn
        target["corners"] = value.Corners;
        target["flags"] = value.Flags;
    }

    [ModuleInitializer]
    internal static void Register() => MappedTypes.Register<Record>();
}
```

```csharp
var layout = new CStruct("struct point { int16 x; int16 y; }; struct record { uint8 tag; point origin; point corners[2]; uint8 flags[3]; };");
Record record = layout.ReadValue<Record>(bytes, "record");
byte[] written = layout.Serialize("record", record);
```

Register from a module initializer, never from a static constructor: a static constructor that no other code
triggers is removed by the Native AOT compiler, and the first `ReadValue<Record>` would then report the type as not
mapped. The mapper decides the collection type: `Get<T>` hands out arrays, and a mapper is free to copy them into a
`List<T>` or anything else. A class that does not implement the interface is not a mapping target; `ReadValue<T>`
reports a `CStructReadException` naming the type, and a write reports a `CStructWriteException` that says what is
writable (a `StructValue`, a dictionary, or a registered mapped class).

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

The browser runtime is the same library published trimmed (`TrimMode=full`) inside the WASM bridge. Its values are
dictionary and list shaped, so it never registers a mapped class; the details are in
[web development](../project/web-development.md#managed-bridge-trimming).

## Checklist

- `PublishAot`/`PublishTrimmed` on the application; nothing on the package.
- Every class you read into or write from is `[CStructMapped]` or implements `ICStructMapped<T>` and is registered
  (generated classes do both for you); a `[CStructLayout]` class needs nothing.
- No `dynamic` in the application: `Get<T>`, dictionary indexing, `ReadValue<T>`, or a generated class instead
  (`CSG300` points at the `dynamic` uses).
- Run the published binary once through a typed read and a write; a removed member or an interface member surfaces
  as a `CStructReadException` with the remedy in the message.

Next: [Typed values](typed-values.md) for the mapping rules themselves, and [performance](performance.md) for
what a published program spends per operation.
