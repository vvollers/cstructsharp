namespace CStructSharp.Tests;

using System.Collections.Generic;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Compilation;

/// <summary>Compile-time codec identity must agree with the primitive registry vocabulary and the layout byte order.</summary>
[TestClass]
public class PrimitiveCodecIdentityTests
{
    /// <summary>
    ///     Every readable name in the catalog (canonical, neutral, and alias spellings) resolves to a known kind whose
    ///     size is the symbol's fixed size, and the catalog's alignment follows the descriptor rule (1 for
    ///     variable-length codecs and for 3-, 6-, and 16-byte identifiers, otherwise the size).
    /// </summary>
    [TestMethod]
    public void EveryCatalogName_ResolvesToAKnownKind()
    {
        var layout = new CStruct("struct root { uint8 v; };");
        PrimitiveCatalog catalog = layout.Codecs.Catalog;
        foreach (string name in catalog.CodecIds.Keys)
        {
            PrimitiveCodec codec = PrimitiveCodec.Resolve(name, true);
            Assert.AreNotEqual(PrimitiveCodecKind.None, codec.Kind, name);
            CompiledTypeSymbol symbol = catalog.Symbols[name].Symbol;
            Assert.AreEqual(codec.Size == 0 ? null : (int?)codec.Size, symbol.FixedSize, name);
            Assert.AreEqual(PrimitiveCatalog.AlignmentOf(codec), catalog.Alignments[name], name);
            Assert.AreEqual(catalog.Alignments[name], symbol.Alignment, name);
        }
    }

    /// <summary>Neutral spellings follow the layout byte order; explicit suffixes override it.</summary>
    [TestMethod]
    public void NeutralSpellings_FollowTheLayoutByteOrder()
    {
        Assert.IsTrue(PrimitiveCodec.Resolve("uint32", true).LittleEndian);
        Assert.IsFalse(PrimitiveCodec.Resolve("uint32", false).LittleEndian);
        Assert.IsTrue(PrimitiveCodec.Resolve("uint32<", false).LittleEndian);
        Assert.IsFalse(PrimitiveCodec.Resolve("uint32>", true).LittleEndian);
        Assert.AreEqual(PrimitiveCodecKind.TerminatedUtf16, PrimitiveCodec.Resolve("string<", true).Kind);
        Assert.AreEqual('\n', PrimitiveCodec.Resolve("utf8_string_newline", true).Terminator);
        Assert.AreEqual(PrimitiveCodecKind.None, PrimitiveCodec.Resolve("not-a-codec", true).Kind);
    }

    /// <summary>A neutral-spelled numeric array parses through the bulk path identically in both layout orders.</summary>
    [TestMethod]
    public void NeutralNumericArrays_ParseInEitherLayoutOrder()
    {
        byte[] bytes = new byte[4 * 300];
        for (int index = 0; index < 300; index++)
        {
            bytes[index * 4] = (byte)index;
            bytes[(index * 4) + 3] = 0x80;
        }

        dynamic little = new CStruct("struct root { uint32 values[300]; };").Parse(bytes, "root");
        dynamic big = new CStruct("struct root { uint32 values[300]; };", isLittleEndian: false).Parse(bytes, "root");
        dynamic explicitBig = new CStruct("struct root { uint32> values[300]; };").Parse(bytes, "root");
        Assert.AreEqual(0x80000000u + 7, (uint)((IList<object?>)little.values)[7]!);
        Assert.AreEqual(0x07000080u, (uint)((IList<object?>)big.values)[7]!);
        CollectionAssert.AreEqual(((IList<object?>)explicitBig.values).ToArray(), ((IList<object?>)big.values).ToArray());
    }

    /// <summary>
    ///     The layout's writer table has a delegate for exactly the canonical codecs the compiled write engine writes
    ///     through one - every codec except the one- to eight-byte numbers (<see cref="PrimitiveCodec.IsFixedWidthNumeric"/>),
    ///     which the engine encodes itself; a negative id has no writer.
    /// </summary>
    [TestMethod]
    public void DelegateWriters_CoverTheCodecsTheEngineDoesNotEncodeItself()
    {
        var layout = new CStruct("struct root { uint8 v; };");
        PrimitiveCatalog catalog = layout.Codecs.Catalog;
        foreach (string name in PrimitiveCatalog.CanonicalNames)
        {
            PrimitiveCodec codec = PrimitiveCodec.Resolve(name, true);
            Assert.AreEqual(codec.IsFixedWidthNumeric, layout.Codecs.WriterOfCodec(catalog.CodecIdOf(name)) is null, name);
        }

        Assert.IsNull(layout.Codecs.WriterOfCodec(PrimitiveCatalog.NoCodec));
    }

    /// <summary>
    ///     Every delegate writer encodes what the engine reads back: a sample value written by the canonical writer parses,
    ///     through a one-member layout of that type, into a value that serializes to the same bytes, in both directions of
    ///     every byte order the name spells.
    /// </summary>
    [TestMethod]
    public void DelegateWriters_RoundTripThroughTheEngine()
    {
        var table = new CStruct("struct root { uint8 v; };").Codecs;
        foreach (string name in PrimitiveCatalog.CanonicalNames)
        {
            if (table.WriterOfCodec(table.Catalog.CodecIdOf(name)) is not { } writer)
            {
                continue;
            }

            PrimitiveCodec codec = PrimitiveCodec.Resolve(name, true);
            object sample = codec.Kind switch
            {
                PrimitiveCodecKind.Uuid or PrimitiveCodecKind.Guid => new Guid("00112233-4455-6677-8899-aabbccddeeff"),
                PrimitiveCodecKind.Char or PrimitiveCodecKind.WChar => 'a',
                _ when codec.IsTerminatedText => "ab",
                _ => 1,
            };
            using var written = new MemoryStream();
            writer(written, sample);
            byte[] bytes = written.ToArray();
            Assert.IsNotEmpty(bytes, name);

            var layout = new CStruct("struct root { " + name + " v; };");
            CollectionAssert.AreEqual(bytes, layout.Serialize("root", layout.Parse(bytes, "root")), name);
        }
    }
}
