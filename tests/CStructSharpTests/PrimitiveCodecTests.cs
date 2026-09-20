namespace CStructSharpTests;

using System.Collections.Generic;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Compilation;

/// <summary>Compile-time codec identity must agree with the primitive registry vocabulary and the layout byte order.</summary>
[TestClass]
public class PrimitiveCodecTests
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

    /// <summary>
    ///     The catalog's static sizes agree with what the runtime delegates actually consume: every fixed-width
    ///     reader advances a stream by exactly the symbol's fixed size. This keeps the compile-time table (which the
    ///     source generator also uses) coupled to the real reader logic.
    /// </summary>
    [TestMethod]
    public void CatalogSizes_MatchWhatTheReadersConsume()
    {
        var layout = new CStruct("struct root { uint8 v; };");
        PrimitiveCatalog catalog = layout.Codecs.Catalog;
        foreach (string name in PrimitiveCatalog.CanonicalNames)
        {
            int? size = catalog.Symbols[name].Symbol.FixedSize;
            if (size is null)
            {
                continue;
            }

            var stream = new MemoryStream(new byte[32]);
            layout.Codecs.ReaderOf(name)!(stream);
            Assert.AreEqual(size.Value, (int)stream.Position, name);
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
}
