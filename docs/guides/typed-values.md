---
title: Map values to C# types
description: Read a layout into your own mapped class, an array, a numeric type, or a CLR enum with checked conversion.
---

# Map values to C# types

Dynamic results are convenient when exploring data, but most application code is easier to maintain with normal C#
types. `ReadValue<T>` decodes using the same layout rules as `ReadValue` and maps the value to `T`. The mapping is
checked: values are not silently truncated to make them fit. This is member-by-member conversion, not copying a
native C struct into an identically shaped C# object; C# member offsets and attributes do not define the binary
format.

A class becomes a mapping target by implementing `ICStructMapped<T>`: a static `ReadFrom(StructValue)` that builds
an instance and a static `WriteTo(T, StructValue)` that stores one, plus a one-line registration with
`MappedTypes.Register<T>()`. The `[CStructMapped]` source generator writes all three for a `partial` class; this
page writes them by hand so the mechanism is visible. Nothing is discovered by reflection, so the same class works
unchanged under trimming and Native AOT.

A mapper reads only the members it names, which lets a class select the values the application needs and ignore
the rest.

## Map a struct step by step

This layout stores a signed two-dimensional point:

```c
struct point {
    int16 x;
    int16 y;
};
```

The C# class uses `short`, which is the CLR name for a signed 16-bit integer, and names the layout members `x` and
`y` in its mapper:

[!code-csharp[Define the destination class](../examples/Program.cs#api-guide-map-poco-type)]

Read it with:

[!code-csharp[Map a layout to the class](../examples/Program.cs#api-guide-map-poco)]

The four little-endian input bytes are:

```text
FE FF 05 00
└─ -2 ┘└─ 5 ┘
```

`ReadValue<Point>` produces `Point { X = -2, Y = 5 }`.

Member names in the mapper are exact and case-sensitive - `source.Get<short>("x")` reads the layout's `x` - so a C#
property may be called whatever the application prefers. A name the layout does not declare raises a
`CStructPathException` that lists the members the struct does have.

The registration runs from a *module initializer*, a method the runtime calls before any other code in the
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

## Common mapping failures

When mapping fails, inspect the exception path (`root.leaves[0].v` names the member whose conversion failed, even
inside a nested mapper) and then check:

1. Is the class registered? `ReadValue<T>` of an unregistered class fails with a message naming the type.
2. Does the mapper spell every layout member exactly as the layout declares it?
3. Can every numeric value fit its `Get<T>` target type?
4. Is null being read only into a nullable target?
5. Is every nested class the mapper asks for a registered mapped class itself?

If the failure is unclear, first read the same path without `<T>`. Seeing the direct result and its runtime type
usually reveals whether the problem is binary decoding or C# mapping.

## Trimming and Native AOT

The library ships as `IsTrimmable` and (for .NET 10) `IsAotCompatible`, and a published Native AOT program runs
every operation, including typed reads and writes from mapped classes. Mapping is the code in `ReadFrom` and
`WriteTo`, so there is no reflection and no annotation to add; the one rule is to register from a module initializer,
as above. `dynamic` access is JIT-only. [Trimming and Native AOT](trimming-and-native-aot.md) has the details;
`tests/CStructSharp.AotConsumer` runs those cases in CI.

Next, read [Write and serialize values](writing-and-serialization.md) to use mapped classes and parsed values as
output.
The generated [`ReadValue<T>` reference](xref:CStructSharp.CStruct.ReadValue``1(System.IO.Stream,System.String,System.Collections.Generic.IReadOnlyDictionary{System.String,System.Int32},CStructSharp.ReadOptions))
lists the exact overload and exceptions.
