---
title: Map values to C# types
description: Read a layout into a POCO, collection, numeric type, or CLR enum with checked conversion.
---

# Map values to C# types

Dynamic results are convenient when exploring data, but most application code is easier to maintain with normal C#
types. `ReadValue<T>` decodes using the same layout rules as `ReadValue` and maps values to `T`. Eligible fixed
layouts fill the destination directly; other layouts decode a result first. The mapping is checked: values are
not silently truncated to make them fit. This is field-by-field conversion, not copying a native C struct into
an identically shaped C# object; C# member offsets and attributes do not define the binary format.

POCO means *plain old CLR object*: an ordinary C# class used to hold data. A supported POCO needs:

- a public parameterless constructor;
- public writable properties or public mutable fields; and
- a compatible member for every writable target member.

Extra fields in the binary source may be ignored, which lets a POCO select only the values the application needs.

## Map a struct step by step

This layout stores a signed two-dimensional point:

```c
struct point {
    int16 x;
    int16 y;
};
```

The C# class uses `short`, which is the CLR name for a signed 16-bit integer:

[!code-csharp[Define the destination POCO](../examples/Program.cs#api-guide-map-poco-type)]

Read it with:

[!code-csharp[Map a layout to the POCO](../examples/Program.cs#api-guide-map-poco)]

The four little-endian input bytes are:

```text
FE FF 05 00
└─ -2 ┘└─ 5 ┘
```

`ReadValue<Point>` produces `Point { X = -2, Y = 5 }`.

Member matching first looks for the exact layout name. If there is no exact match, one unambiguous
case-insensitive match is allowed, so `x` can map to `X`. Two destination members that make the match ambiguous
cause a read error instead of choosing one unpredictably.

## Other supported targets

Typed reads can also map:

- integral values when the source fits the destination's range;
- floating-point and decimal targets through checked invariant conversion;
- `EnumValueResult` to a CLR enum, including an unknown numeric value;
- arrays and common generic collection interfaces by converting each item; and
- nested struct or union dictionaries recursively into nested POCOs.

Directly assignable values are returned unchanged. Null is accepted only when the target is a reference type or a
nullable value type.

The mapper does not invoke custom serializers, infer constructors with parameters, write private members, honor
serialization attributes, or automatically replace a `Pointer` with its target.

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

When mapping fails, inspect the exception path and then check:

1. Does the class have a public parameterless constructor?
2. Are the destination members public and writable?
3. Is each required name present exactly once?
4. Can every numeric value fit its destination type?
5. Is null being assigned only to a nullable target?
6. Is a nested dictionary being mapped to a compatible nested class?

If the failure is unclear, first read the same path without `<T>`. Seeing the direct result and its runtime type
usually reveals whether the problem is binary decoding or C# mapping.

## Trimming and Native AOT

The library ships as `IsTrimmable` and (for .NET 10) `IsAotCompatible`, and a published Native AOT program runs
every operation, including typed reads and POCO writes. Mapping uses reflection, so the trimmer needs to know
which members to keep:

- The type you name in `ReadValue<T>`, `TryReadValue<T>`, `Get<T>`, or `TryGet<T>` is annotated on the method
  itself: its public parameterless constructor, public properties, and public fields are preserved automatically.
- Classes reached *through* that type - a nested class used as a member type, the element type of an array or
  list member - and any object you hand to `Serialize`, `Write`, or `Update` are only known at run time. Keep
  their members explicitly by marking the class:

  ```csharp
  [DynamicallyAccessedMembers(
      DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
      DynamicallyAccessedMemberTypes.PublicProperties |
      DynamicallyAccessedMemberTypes.PublicFields)]
  public sealed class Point { public short X { get; set; } public short Y { get; set; } }
  ```

  (or list them in a trimmer root descriptor). A member the trimmer removed shows up as a
  `CStructReadException` saying the source member is missing.
- Declare collection members as `List<T>` or `T[]`. An interface such as `IList<T>` needs a `List<T>` created
  at run time, which Native AOT cannot do; the failure message says which declaration to use instead.

`tests/CStructSharp.AotConsumer` publishes with `PublishAot=true` and runs these cases in CI.

Next, read [Write and serialize values](writing-and-serialization.md) to use POCOs and dynamic objects as output.
The generated [`ReadValue<T>` reference](xref:CStructSharp.CStruct.ReadValue``1(System.IO.Stream,System.String,System.Collections.Generic.IReadOnlyDictionary{System.String,System.Int32},CStructSharp.ReadOptions))
lists the exact overload and exceptions.
