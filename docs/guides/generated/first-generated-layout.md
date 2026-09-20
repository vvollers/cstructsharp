---
title: Your first generated layout
description: The six-byte header as a [CStructLayout] class - the generated class, Parse, Serialize, and a walk through the generated file.
---

# Your first generated layout

[Install and make a first parse](../install-and-first-parse.md) read a six-byte header with the runtime API. This
page reads the same bytes through generated code. You need a project that references the `CStructSharp` package;
the package contains the generator, and nothing else has to be installed.

## Declare the layout

Put the layout text on a `static partial` class. `partial` is what lets the generator add members to a class you
declared; `static` because the class is a namespace for the generated types and methods, not something you
instantiate.

[!code-csharp[The layout class](../../examples/GeneratedExamples.cs#generated-first-layout-class)]

The text is the same layout language the runtime reads. A raw string literal (`"""..."""`) is convenient for
longer layouts: when the generator reports a mistake in the text, it points at the exact line and column inside
the literal.

## Parse and serialize

[!code-csharp[Parse and serialize](../../examples/GeneratedExamples.cs#generated-first-layout)]

`Wire.Header` is a class the generator wrote: `Kind` is a `ushort` because `uint16` holds 0-65535, `Length` a
`uint`. `Wire.Parse` reads the root declaration (the first struct, or the one named by `Root = "..."`); every
declared struct also gets its own `Parse<Name>` overloads. `Wire.Serialize` writes the class back; the bytes equal
the input because the header has no padding and no value that a write would normalize.

`Wire.Layout` is the runtime `CStruct` for the same text, built on first use. Everything the runtime API offers -
paths, introspection, the memory-analysis API - is available through it, and `Wire.Sizes.Header` is the same
number `GetStructSizeInBytes("header")` reports, computed at build time.

## The generated file, line by line

Turn on `EmitCompilerGeneratedFiles` (see [where the generated files are](index.md#where-the-generated-files-are))
and open `Wire.CStructLayout.g.cs`. Reading it once shows there is nothing hidden:

```csharp
public static partial class Wire
{
    public const string Definition = "struct header { uint16 kind; uint32 length; };";
    public const string RootName = "header";
    public static global::CStructSharp.CStruct Layout => LayoutInstance.Value;
```

The class keeps the text and the root name as constants and builds the runtime layout lazily, with the attribute's
options (`Aligned`, `LittleEndian`, `PointerSize`, ...) passed through.

```csharp
    public sealed partial class Header
    {
        public ushort Kind { get; set; }
        public uint Length { get; set; }
    }
```

One class per composite. It is `partial` too: you can add methods to it in your own file.

```csharp
    private static Header ReadHeader(ref global::CStructSharp.Generated.ReadCursor cursor, ...)
    {
        cursor.EnterComposite(member ?? "header", memberType);
        var value = new Header();
        var placement = global::CStructSharp.Generated.CompositeCursor.Start(cursor.Position, Aligned, Packing, Allocation);
        // uint16 kind
        {
            cursor.Seek(placement.AdvanceToField(2), "kind", "uint16");
            value.Kind = global::CStructSharp.Generated.Codec.ReadUInt16(cursor.Take(2, "kind", "uint16"), true);
            placement.CompleteField(cursor.Position);
        }
        ...
```

The reader is straight-line code: for each field, move to where the layout places it, take its bytes, decode them
with the shared `Codec` functions, store the property. `ReadCursor` is the small struct that carries the position,
the read options, and the budgets; `CompositeCursor` applies the same alignment and bitfield rules the runtime uses.
Every `Take` names the field it reads, so a short input fails with the same message the runtime would give:
`Not enough bytes: needed 4, available 3 (field 'length' (uint32), in 'header', offset 2)`.

The rest of the file holds the writer (`EncodeHeader`), the `Serialize`/`Write` overloads, the view, the constants,
and the setters. Each later lesson looks at one of those parts.

## Check yourself

1. Why must the attributed class be `partial`?
2. What type does `uint32 length` become, and why not `int`?
3. Is `Wire.Layout` built when the program starts?

<details>
<summary>Answers</summary>

1. The generator writes a second declaration of the same class; C# merges `partial` declarations into one type.
2. `uint`: the layout type is unsigned and needs the full 0 to 4,294,967,295 range, which `int` cannot hold.
3. No. It is a `Lazy<CStruct>`, created the first time `Layout` is read.

</details>

## Exercise

Add a second struct to the layout - `struct trailer { uint32 crc; };` - and read a trailer from the bytes
`78 56 34 12` with the generated method for it.

<details>
<summary>Solution</summary>

```csharp
[CStructLayout("struct header { uint16 kind; uint32 length; }; struct trailer { uint32 crc; };")]
public static partial class Wire { }

Wire.Trailer trailer = Wire.ParseTrailer(new byte[] { 0x78, 0x56, 0x34, 0x12 });
// trailer.Crc == 0x12345678
```

`Wire.Parse` still reads the header (the first declaration); `ParseTrailer` is generated because `trailer` is a
declared struct.

</details>
