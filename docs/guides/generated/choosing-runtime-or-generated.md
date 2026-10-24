---
title: Runtime or generated?
description: A decision table for the runtime API and the generated path, and what the measured numbers mean.
---

# Runtime or generated?

Both paths read and write the same layouts with the same rules. They differ in *when* the layout is known and in
*what* a read produces.

| Question | Runtime API | Generated |
| --- | --- | --- |
| Is the layout known when the program is compiled? | Either | Required |
| Do users or files supply layouts at run time? | Yes | No - use `Wire.Layout` for the fixed ones and `new CStruct` for the rest |
| Is the read in a hot loop? | `StructValue` per read | Typed class per read, or a view with no allocation |
| Do you want IntelliSense on member names? | Strings and paths | Properties |
| Native AOT or trimming? | Works (mapped classes, no `dynamic`) | Works; the generated code has no reflection to trim |
| Reading a memory image with the memory-analysis API? | Yes | Through `Wire.Layout` |
| JSON, introspection, debug output? | `CStruct` members | `Wire.Layout` and `ParseWithDebug` |
| Writing? | Dictionaries or mapped classes | Typed classes, typed `Update` setters |
| Input that may be wrong? | `TryReadValue<T>`, `TryGet`/`GetOrDefault` on the value | `TryParse` with the failure it would have thrown |
| A file of records, a stream of frames? | `ParseMany` / `ParseManyAsync` (`StructValue` per record) | `Records` / `RecordsAsync` (a class per record), or the view enumerator (nothing allocated) |
| Streams that arrive while the program runs? | `ParseAsync`, `WriteAsync`, `UpdateAsync` | `ParseAsync`, `WriteAsync` - the same buffering, the generated reader |
| Exploring a format interactively? | `dynamic`, paths | Less convenient |

The table's short form: **if the layout is in your source, generate; if it arrives with the data, use the
runtime.** Mixed programs are normal - a generated class of the full surface carries its runtime `Layout`.

## Generate less

The generator writes the same large set of members for every struct and union, however small, so a project with
hundreds of layouts of which it reads only a few members each pays for code it never calls. `Surface` chooses how much:

| `Surface` | Generated | Left out |
| --- | --- | --- |
| `GeneratedSurface.Full` (default) | Everything: typed classes, every reader, `TryParse`, `Records`, `Serialize`/`Write`, `Update`, path operations, `Layout`, `Sizes`, `Offsets`, views | Nothing |
| `GeneratedSurface.Reader` | Typed classes, `Parse` and `TryParse` over a span, an array or a `ReadOnlyMemory<byte>`, `Sizes`, `Offsets`, views | Writers, `Update`, sequence, stream and asynchronous readers, `Records`, path operations, `Layout`, the view enumerators |
| `GeneratedSurface.Parser` | Typed classes, one span `Parse` per declaration and the unnamed root `Parse`, `Sizes`, `Offsets`, views | Everything the reader surface leaves out, `TryParse`, the array and `ReadOnlyMemory` overloads, `Definition`, `RootName` |
| `GeneratedSurface.Views` | Views, `Sizes`, `Offsets`, and the layout's enums | The typed classes and every reader, so a view has no `ToObject()` |

```csharp
[CStructLayout("struct word { uint32 value; };", Surface = GeneratedSurface.Views)]
internal static partial class WordLayout { }

uint value = new WordLayout.WordView(bytes).Value;
```

The `Parser` surface suits a project that parses with `Parse(span, options)` everywhere: a `byte[]` converts to the span,
and a caller that wants `TryParse` or the memory forms chooses `Reader`. The view enumerator (`RootView.Enumerate`) is part of
the full surface; on the smaller ones `Enumerators = true` adds it, because it costs extra code for every fixed-size struct.

For the layout in the generator's tests the reader surface is less than half the full output and the views surface less
than a sixth. All three read the same values with the same failures; the smaller ones simply have fewer ways in. A
`[CStructMapped]` class and any code that uses `Layout`, `Serialize` or `Update` need the full surface, and `Views`
cannot be combined with `Views = false` (see [CSG011](diagnostics.md#csg011---the-views-surface-needs-views)).

## Project defaults

A project with hundreds of layouts should not repeat the same options on every attribute. Build properties set the
default of each option the attribute does not name; an option the attribute names always keeps its own value:

```xml
<PropertyGroup>
  <CStructSharpSurface>Reader</CStructSharpSurface>
  <CStructSharpViews>false</CStructSharpViews>
  <CStructSharpLittleEndian>false</CStructSharpLittleEndian>
</PropertyGroup>
```

| Property | Sets the default of | Values |
| --- | --- | --- |
| `CStructSharpSurface` | `Surface` | `Full`, `Reader`, `Parser`, `Views` |
| `CStructSharpViews` | `Views` | `true`, `false` |
| `CStructSharpEnumerators` | `Enumerators` | `true`, `false` |
| `CStructSharpRuntimeByteOrder` | `RuntimeByteOrder` | `true`, `false` |
| `CStructSharpShape` | `Shape` | `Class`, `Struct` |
| `CStructSharpKeepNames` | `KeepNames` | `true`, `false` |
| `CStructSharpAligned` | `Aligned` | `true`, `false` |
| `CStructSharpLittleEndian` | `LittleEndian` | `true`, `false` |
| `CStructSharpPointerSize` | `PointerSize` | `1`, `2`, `4`, `8` |
| `CStructSharpBitfieldPacking` | `BitfieldPacking` | `SysV`, `Msvc` |
| `CStructSharpBitfieldAllocation` | `BitfieldAllocation` | `LowBitFirst`, `HighBitFirst` |
| `CStructSharpCLongWidth` | `CLongWidth` | `32`, `64` |
| `CStructSharpDefaultEnumStorage` | `DefaultEnumStorage` | a type spelling such as `uint32` |

With `CStructSharpSurface` set to `Reader`, a layout that needs writers or `Layout` says `Surface = GeneratedSurface.Full`
on its own attribute. `Defined` and `Codecs` are per layout and have no project default. A value that does not parse is
reported as [CSG012](diagnostics.md#csg012---a-cstructsharp-project-property-is-not-valid) and ignored.

## One layout for both byte orders

Some formats come in two byte orders and say which in a field (a magic number, a flag). Without help that is two
layouts that differ only in `LittleEndian`, and code that picks one. `RuntimeByteOrder = true` generates one layout whose
neutral values - those spelled without a `<` or `>` suffix - are decoded in the order the caller gives when it reads:

```csharp
[CStructLayout("struct entry { uint32 inode; uint16 record_length; uint16 name_length; };",
    Root = "entry", Surface = GeneratedSurface.Parser, RuntimeByteOrder = true)]
internal static partial class DirectoryEntryLayout { }

Entry entry = DirectoryEntryLayout.Parse(bytes, littleEndian: magic == LittleMagic);
var view = new DirectoryEntryLayout.EntryView(bytes, littleEndian);   // views take it after the source
```

Every `Parse`, `Parse<Name>`, `TryParse` and view constructor (and `Enumerate`) takes `bool littleEndian` after the source;
a value with an explicit suffix (`uint32<`) keeps its order whatever the caller says, and `LittleEndian` on the attribute
is not used. It needs a surface that only reads (`Reader`, `Parser` or `Views`), because a writer encodes one order, and a
layout whose bitfields sit in the same place in both orders: a bitfield run that the compiler places differently in a
big-endian layout cannot share one placement and is reported as
[CSG013](diagnostics.md#csg013---runtimebyteorder-is-not-supported-here) with the field that moves.

## Value types instead of classes

A generated class is a heap object, and every array member of it is another one. A reader that parses a million small
records pays for both in allocations and garbage collections. `Shape = GeneratedShape.Struct` generates each struct of
the layout as a C# `struct`, so reading a value allocates nothing for it, and the value can sit in a `List<T>`, be
returned from a method and be used across an `await`, which a [view](views-and-zero-allocation.md) cannot:

```csharp
[CStructLayout("struct entry { uint32 inode; uint16 record_length; uint8 signature[4]; };",
    Root = "entry", Surface = GeneratedSurface.Parser, Shape = GeneratedShape.Struct)]
internal static partial class EntryLayout { }

EntryLayout.Entry entry = EntryLayout.Parse(bytes);          // no object, no array
bool sameMagic = entry.Signature.AsSpan().SequenceEqual(magic); // the array lives inside the struct
```

A fixed array of numbers is a generated *inline array* (`InlineArrays.ByteArray4`, `UInt16Array3`, ...): a value-type array
of a fixed length stored inside the struct, with an indexer, `AsSpan()`, `ToArray()` and `Length`. Text (`char[N]`,
`utf8[N]`, ...) is still a `string`, which allocates when read. The shape needs the `Reader` or `Parser` surface, because
a writer, `Update` and `Records` need classes, and a layout of fixed size: numbers, enums, bitfields, nested structs, text
and one-dimensional fixed arrays of numbers. A union, a conditional member, a pointer, a runtime-sized or multidimensional
array, an array of structs or enums, a custom codec and a terminated string are reported as
[CSG014](diagnostics.md#csg014---a-value-type-shape-is-not-supported-here) with the member that does not fit. The shape works
with `RuntimeByteOrder`, bitfields and `@expect` constraints.

## What the numbers mean

The [performance page](../performance.md) has a "Generated" table measured with `GeneratedBenchmarks`. For the
reference record used throughout the benchmarks (the primitives record), it compares four ways of reading the
same bytes: the runtime `Parse`, the generated `Parse`, a generated view, and hand-written `BinaryPrimitives`
code. Reading the table:

- **Runtime `Parse`** allocates a `StructValue` and its members; it is the baseline every other row is compared
  against.
- **Generated `Parse`** allocates the typed class and nothing else, and does no dictionary lookups; expect it to
  be several times faster with a fraction of the allocations.
- **Generated view** allocates nothing; each member is decoded when read. It is the number to compare with the
  hand-written row.
- **Hand-written** is what a careful programmer writes with `BinaryPrimitives` for one layout; the generated view
  should sit close to it, because it is the same code with the offsets filled in.

The nested fixture (`Nested256`) shows the same four numbers for a struct with an array of 256 nested records,
where the generated `Parse` has 256 objects to create and the view has none.

Numbers are from one machine and one run; the point is the ratios, not the microseconds.

## Check yourself

1. A program reads PNG chunks; the chunk layout is fixed. Which path?
2. The same program lets users describe extra chunk types in a config file. Which path for those?
3. Which generated form allocates nothing?

<details>
<summary>Answers</summary>

1. Generated: the layout is known at build time.
2. Runtime: the layouts are only known at run time. Both can live in one program.
3. The `readonly ref struct` view.

</details>

## Exercise

Take the `Wire` header from the [first lesson](first-generated-layout.md) and read it three ways - runtime
`Parse`, generated `Parse`, and `Wire.HeaderView` - printing the `length` from each. Then decide which you
would use in a loop over a million packets.

<details>
<summary>Solution</summary>

All three print the same value; the view, because it allocates nothing per packet and the loop cannot let the
view escape anyway.

</details>
