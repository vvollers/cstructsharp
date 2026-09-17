namespace CStructSharpTests;

using System.Collections.Generic;
using CStructSharp;
using CStructSharp.Codecs;

/// <summary>Compile-time codec identity (E1.5) must agree with the primitive registry vocabulary and the layout byte order.</summary>
[TestClass]
public class PrimitiveCodecTests
{
    /// <summary>Every registered primitive name (canonical, neutral, and alias spellings) resolves to a known kind with the registry's size.</summary>
    [TestMethod]
    public void EveryRegistryName_ResolvesToAKnownKind()
    {
        var layout = new CStruct("struct root { uint8 v; };");
        foreach (KeyValuePair<string, byte> entry in layout.FieldAlignments)
        {
            string name = entry.Key;
            if (!layout.FieldHandlers.ContainsKey(name))
            {
                continue; // composite/enum names carry alignments too
            }

            PrimitiveCodec codec = PrimitiveCodec.Resolve(name, true);
            Assert.AreNotEqual(PrimitiveCodecKind.None, codec.Kind, name);
            if (codec.IsFixedWidthNumeric || codec.IsFixedPoint || codec.IsIdentifier || codec.Kind == PrimitiveCodecKind.WChar)
            {
                Assert.AreEqual(entry.Value == 1 && codec.Size == 3 ? 3 : Math.Max(entry.Value, (byte)1), codec.Size, name);
            }
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
