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
that ignores underscores - `FileName` finds `file_name`. A step that finds two members ends the search without a
match. `[CStructMember("name")]` names the member explicitly, which is how `FileName` maps to `name` in the example.

With `Layout = "header"` on the attribute, the generator resolves the names at build time against the
`[CStructLayout]` classes in the same project and reports `CSG102` for a property that matches nothing. Without
it the names are resolved at run time, when the value's shape is known.

## Direct reads and writes for a layout-bound class

`Layout = "header"` also lets the generator read the class straight from bytes when every member of that struct has
a fixed offset (no runtime-sized arrays, conditions, pointers, bitfields or unions). The class then implements
`ICStructFixedMapped<T>`: `TryReadFixed` decodes each property at its member's offset, `TryWriteFixed` stores it
there, and `FixedLayoutFingerprint` is a 64-bit summary of the struct they were generated for - every offset, size,
type and byte order.

`layout.ReadValue<Header>(bytes, "header")` and `layout.Serialize(...)` of a whole struct in memory use these
members instead of building a `StructValue` and mapping it property by property. The runtime compares the
fingerprint with that of the layout the call uses. A layout compiled with other options (another byte order or
alignment, say) has another fingerprint and takes the property-by-property route. <!-- facts:mapped-direct-costs:start -->In the repository's serializer comparison (a 79-byte record), `ReadValue<T>` into a layout-bound mapped class took 30.3 ns and `Serialize` of one took 19.8 ns; for comparison, `Parse` into a `StructValue` with every member read by path took 329 ns, and `Serialize` of a `StructValue` took 52.9 ns.<!-- facts:mapped-direct-costs:end -->

The direct members give exactly the results of the property-by-property route, so they are generated only where
that is certain:

- Reading: every property must map to a member, as that member's own type - a `uint32` as `uint`, an enum whose
  underlying type is the member's storage type, a `char[N]` as `string`, an array of the same element type, or a
  nested struct as another layout-bound mapped class. A property that needs a conversion (a `uint16` read as `long`)
  leaves the class without direct members; it is still mapped as before.
- Writing: additionally, every member of the struct must have exactly one property, and none may be text, an enum
  or a 24-bit integer, whose values the writer validates. A value the direct writer cannot store - a null nested
  object, an array of the wrong length - is written property by property, and fails there with the usual message.

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

- `Wire.ReadValue<HeaderRecord>(bytes)` reads the root into the mapped class - the same as
  `Wire.Layout.ReadValue<HeaderRecord>(bytes, "header")` without naming the root - and `Wire.TryReadValue<HeaderRecord>(bytes, out var record)`
  reports a failure as `false`. Both take a span, an array, memory, a `ReadOnlySequence<byte>`, or a stream.
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
