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

    /// <summary>
    ///     Every canonical delegate pair round-trips: a sample value written by the writer reads back through the reader,
    ///     and writing the value read produces the same bytes. The pairs are the catalog's runtime half, so every canonical
    ///     name keeps a working reader and writer.
    /// </summary>
    [TestMethod]
    public void CanonicalDelegatePairs_RoundTrip()
    {
        var layout = new CStruct("struct root { uint8 v; };");
        foreach (string name in PrimitiveCatalog.CanonicalNames)
        {
            PrimitiveCodec codec = PrimitiveCodec.Resolve(name, true);
            object sample = codec.Kind switch
            {
                PrimitiveCodecKind.Uuid or PrimitiveCodecKind.Guid => new Guid("00112233-4455-6677-8899-aabbccddeeff"),
                PrimitiveCodecKind.Bool => true,
                PrimitiveCodecKind.Char or PrimitiveCodecKind.WChar => 'a',
                _ when codec.IsTerminatedText => "ab",
                _ => 1,
            };
            using var first = new MemoryStream();
            layout.Codecs.WriterOf(name)!(first, sample);
            first.Position = 0;
            object read = layout.Codecs.ReaderOf(name)!(first);
            Assert.AreEqual(first.Length, first.Position, name + ": the reader consumes what the writer wrote");

            using var second = new MemoryStream();
            layout.Codecs.WriterOf(name)!(second, read);
            CollectionAssert.AreEqual(first.ToArray(), second.ToArray(), name);
        }
    }
}
