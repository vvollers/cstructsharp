---
title: Mapped classes
description: "[CStructMapped] generates ReadFrom and WriteTo for your own class - the typed path that works from the runtime, from generated code, and under Native AOT."
---

# Mapped classes

A generated layout class gives you *its* types. Sometimes you want *your* class - one that already exists, one
with a `List<T>` instead of an array, one that the rest of the program is built around. `[CStructMapped]` asks
the generator to write the conversion between such a class and a parsed value.

[!code-csharp[Two mapped classes](../../examples/GeneratedExamples.cs#generated-mapped-classes-class)]

[!code-csharp[Reading into them](../../examples/GeneratedExamples.cs#generated-mapped-classes)]

## What is generated

For each attributed class the generator adds an implementation of `ICStructMapped<T>`: a static `ReadFrom(StructValue)`
that creates an instance and assigns each property, a static `WriteTo(T, StructValue)` that fills a value from an
instance, and a module initializer that registers the type with `MappedTypes`. There is no reflection anywhere:
the runtime's `ReadValue<T>`, `Get<T>`, and the write operations look the type up in the registry, which is why
mapped classes work in a trimmed or Native AOT application (see [Trimming and Native AOT](../trimming-and-native-aot.md)).

The mapper considers every public property with a getter and a setter (an `init` setter counts). A property with
no public setter is left alone.

## Matching properties to members

A property finds its layout member by name: the exact spelling first, then a case-insensitive match, then a match
that ignores underscores - `FileName` finds `file_name`. `[CStructMember("name")]` names the member explicitly,
which is how `FileName` maps to `name` in the example.

With `Layout = "header"` on the attribute, the generator resolves the names at build time against the
`[CStructLayout]` classes in the same project and reports `CSG102` for a property that matches nothing. Without
it the names are resolved at run time, when the value's shape is known.

## Conversions

Each property receives its member through the same rules `Get<T>` applies:

- Integers widen without loss and fail with `Get<T>`'s message when the value does not fit.
- A C# enum takes the layout enum's numeric value, or matches by name.
- A `string` receives text; a fixed `char[]` member keeps its padding unless `TrimFixedText` is set.
- Arrays map to `T[]`, `List<T>`, `IList<T>`, `IReadOnlyList<T>`, or `IEnumerable<T>`.
- A nested struct maps to another `[CStructMapped]` class (or to `StructValue` to keep it untyped); a class that is
  neither is `CSG101`.
- A pointer maps to `Pointer<T>` for a typed target or to `Values.Pointer` for the raw address.
- A union maps to `UnionValue`; read the member you want from it, as [unions](../unions.md) shows.
- A nullable property (`uint?`) receives `null` for a conditional member that was not read.

## From generated code

A generated layout class bridges to mapped classes without leaving the typed world:

- `Wire.ToMapped<HeaderRecord>(header)` converts a generated instance.
- `Wire.ParseMapped<HeaderRecord>(bytes)` parses straight into the mapped class.
- `Wire.SerializeMapped(record)` writes one.

Each goes through a `StructValue` built from the generated instance's bytes, so the cost is a runtime parse; use
it at the edges of a program, not in a hot loop where the generated class itself is the faster type.

## Check yourself

1. Which of these properties is mapped: `public int A { get; set; }`, `public int B { get; }`, `public int C { get; init; }`?
2. What does `Layout = "..."` on the attribute change?
3. Why does a mapped class not need any trimming annotations?

<details>
<summary>Answers</summary>

1. `A` and `C`; `B` has no setter.
2. Name resolution happens at build time, so a property with no counterpart becomes a `CSG102` warning instead of a run-time failure.
3. The generator writes the property assignments as ordinary C#; nothing is discovered by reflection at run time.

</details>

## Exercise

Give `SampleRecord` a property `public byte Missing { get; set; }` and `Layout = "sample"` on its attribute, then
build. Remove the property (or add `[CStructMember("count")]`) to make the warning go away.

<details>
<summary>Solution</summary>

The build reports `CSG102: 'Missing' matches no member of the layout 'sample' (by exact name, case-insensitively,
or ignoring underscores); add [CStructMember("name")] or rename it`. With `[CStructMember("count")]` both `Count`
and `Missing` receive the same member.

</details>
