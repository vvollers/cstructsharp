---
title: Map values to C# types
description: Read a layout into your own [CStructMapped] class, an array, a numeric type, or a CLR enum with checked conversion.
---

# Map values to C# types

Dynamic results are convenient when exploring data, but most application code is easier to maintain with normal C#
types. `ReadValue<T>` decodes using the same layout rules as `ReadValue` and maps the value to `T`. The mapping is
checked: values are not silently truncated to make them fit. This is member-by-member conversion, not copying a
native C struct into an identically shaped C# object; C# member offsets and attributes do not define the binary
format.

A class becomes a mapping target with one attribute:

[!code-csharp[A mapped class](../examples/Program.cs#api-guide-map-mapped-type)]

`[CStructMapped]` asks the CStructSharp source generator to implement `ICStructMapped<T>` for the `partial`
class at build time: a static `ReadFrom(StructValue)` that builds an instance, a static `WriteTo(T, StructValue)`
that stores one, and a module initializer that registers the type with `MappedTypes`. Nothing is discovered by
reflection, so the same class works unchanged under trimming and Native AOT. A mapper reads only the members it
names, which lets a class select the values the application needs and ignore the rest.

## Map a struct step by step

This layout stores a signed two-dimensional point:

```c
struct point {
    int16 x;
    int16 y;
};
```

The C# class uses `short`, which is the CLR name for a signed 16-bit integer. Read it with:

[!code-csharp[Map a layout to the class](../examples/Program.cs#api-guide-map-mapped)]

The four little-endian input bytes are:

```text
FE FF 05 00
└─ -2 ┘└─ 5 ┘
```

`ReadValue<MappedPoint>` produces `MappedPoint { X = -2, Y = 5 }`, and `Serialize("point", point)` writes the
same four bytes back.

Each property finds its layout member by name: the exact spelling first, then a case-insensitive match (`X`
finds `x`), then a match that ignores underscores (`BitDepth` finds `bit_depth`). `[CStructMember("name")]` on a
property names the member explicitly. With `[CStructMapped(Layout = "point")]` the generator checks the names
against a `[CStructLayout]` class in the same project at build time and warns (`CSG102`) about a property that
matches nothing; without it the names are resolved when the value is read, and a name the layout does not
declare raises a `CStructPathException` that lists the members the struct does have. The
[mapped classes lesson](generated/mapped-classes.md) covers lists, nested classes, enums, and pointers.

## What the generator writes

The generated code is ordinary C#; this is the same class written by hand, which is also how a class in a
project without the generator becomes a mapping target:

[!code-csharp[The hand-written equivalent](../examples/Program.cs#api-guide-map-poco-type)]

[!code-csharp[Reading it](../examples/Program.cs#api-guide-map-poco)]

Member names in a hand-written mapper are exact and case-sensitive - `source.Get<short>("x")` reads the layout's
`x`. The registration runs from a *module initializer*, a method the runtime calls before any other code in the
assembly. Register there (or once at startup), never from a static constructor: a static constructor that nothing
else triggers is removed by the Native AOT compiler, and the first `ReadValue<Point>` would report the type as not
mapped.

## Other supported targets

`ReadValue<T>` and `Get<T>` share one set of conversions:

- integral values when the source fits the destination's range;
- floating-point and decimal targets through checked invariant conversion;
- `EnumValueResult` to a CLR enum, including an unknown numeric value;
- arrays (`T[]`) by converting each item, so an array of structs becomes an array of mapped classes;
- nested structs into nested mapped classes, through their own `ReadFrom`; and
- `StructValue`, `UnionValue`, `Pointer`, `string`, and `object` as they are.

Null is accepted only when the target is a reference type or a nullable value type. A mapper is free to go further -
copy an array into a `List<T>`, pick a union member from the `UnionValue`, follow a `Pointer` - because it is
ordinary C# code.

## Handle expected failures

Use `ReadValue<T>` when invalid input should throw a `CStructReadException`. Use `TryReadValue<T>` when malformed or
truncated input is an ordinary result:

```csharp
if (layout.TryReadValue<Header>(bytes, out Header? header, "header"))
{
    Console.WriteLine(header.Length);
}
else
{
    Console.WriteLine("The header is incomplete or invalid.");
}
```

The equivalent branch is compiled in the [first-parse example](install-and-first-parse.md). `TryReadValue<T>` catches
only categorized CStructSharp failures. It does not hide invalid arguments, cancellation, or unrelated application
bugs.

One member of a value you already parsed has the same pair on `StructValue` and `UnionValue`: `Get<T>` throws,
`TryGet<T>(path, out value)` returns `false`, `TryGet<T>(path, out value, out CStructException? failure)` also
hands over the path or read exception `Get<T>` would have thrown, and `GetOrDefault<T>(path, fallback)` returns the
fallback in either case - an absent conditional member and a value that does not fit the type look the same to
it. A generated layout class reads the root into a mapped class without naming it: `Wire.ReadValue<HeaderRecord>(bytes)`
and `Wire.TryReadValue<HeaderRecord>(bytes, out record)` forward to `Layout.ReadValue<T>(bytes, "header")`
([mapped classes](generated/mapped-classes.md)).

## Common mapping failures

When mapping fails, inspect the exception path (`root.leaves[0].v` names the member whose conversion failed, even
inside a nested mapper) and then check:

1. Is the class registered? `ReadValue<T>` of an unregistered class fails with a message naming the type; a
   `[CStructMapped]` class registers itself, a hand-written one must.
2. Does every property (or hand-written `Get<T>` call) name a member the layout declares?
3. Can every numeric value fit its `Get<T>` target type?
4. Is null being read only into a nullable target?
5. Is every nested class the mapper asks for a registered mapped class itself?

If the failure is unclear, first read the same path without `<T>`. Seeing the direct result and its runtime type
usually reveals whether the problem is binary decoding or C# mapping.

## Trimming and Native AOT

The library ships as `IsTrimmable` and (for .NET 10) `IsAotCompatible`, and a published Native AOT program runs
every operation, including typed reads and writes from mapped classes. Mapping is the code in `ReadFrom` and
`WriteTo`, generated or hand-written, so there is no reflection and no annotation to add; the one rule for a
hand-written mapper is to register from a module initializer, as above. `dynamic` access is JIT-only. [Trimming and Native AOT](trimming-and-native-aot.md) has the details;
`tests/CStructSharp.AotConsumer` runs those cases in CI.

Next, read [Write and serialize values](writing-and-serialization.md) to use mapped classes and parsed values as
output.
The generated [`ReadValue<T>` reference](xref:CStructSharp.CStruct.ReadValue``1(System.IO.Stream,System.String,System.Collections.Generic.IReadOnlyDictionary{System.String,System.Int32},CStructSharp.ReadOptions))
lists the exact overload and exceptions.
