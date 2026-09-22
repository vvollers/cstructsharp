namespace CStructSharp.Tests;

using CStructSharp.Codecs;

/// <summary>Checks custom-codec descriptor registration before runtime adapter validation can hide catalog errors.</summary>
[TestClass]
public class PrimitiveCatalogBoundaryTests
{
    /// <summary>Invalid identifiers, duplicate names and invalid storage descriptors report the catalog registration error.</summary>
    /// <param name="name">The requested codec name.</param>
    /// <param name="size">The fixed size in bytes.</param>
    /// <param name="alignment">The byte alignment.</param>
    /// <param name="reason">The required diagnostic fragment.</param>
    [TestMethod]
    [DataRow("", 1, 1, "is not an identifier")]
    [DataRow("2bad", 1, 1, "is not an identifier")]
    [DataRow("bad-name", 1, 1, "is not an identifier")]
    [DataRow("uint8", 1, 1, "is already a primitive type")]
    [DataRow("custom", 1, 0, "needs a power-of-two alignment and a non-negative size")]
    [DataRow("custom", 1, 3, "needs a power-of-two alignment and a non-negative size")]
    [DataRow("custom", -1, 1, "needs a power-of-two alignment and a non-negative size")]
    public void InvalidDescriptors_ExplainTheirConstraint(string name, int size, int alignment, string reason)
    {
        PrimitiveCatalog catalog = PrimitiveCatalog.For(true, 64);

        // Exercise catalog validation directly; the runtime custom-codec wrapper validates independently.
        ArgumentException failure = Assert.Throws<ArgumentException>(() => catalog.WithCustomCodecs(new[] { new CustomCodecDescriptor(name, size, alignment), }));
        Assert.AreEqual("codecs", failure.ParamName);
        StringAssert.Contains(failure.Message, reason);
    }

    /// <summary>Zero-size and variable-size codecs retain unique ids, exact alignment and published immutable symbols.</summary>
    [TestMethod]
    public void ValidDescriptors_PublishCompleteMetadataWithoutChangingTheBaseline()
    {
        PrimitiveCatalog baseline = PrimitiveCatalog.For(true, 64);
        Assert.AreEqual("void", baseline.Symbols["void"].Symbol.Name);
        var empty = new CustomCodecDescriptor("_empty", 0, 1);
        var variable = new CustomCodecDescriptor("variable", null, 8);
        PrimitiveCatalog catalog = baseline.WithCustomCodecs(new[] { empty, variable, });
        Assert.AreEqual(baseline.CodecCount + 2, catalog.CodecCount);
        Assert.AreEqual(baseline.CodecCount, catalog.CodecIdOf("_empty"));
        Assert.AreEqual(baseline.CodecCount + 1, catalog.CodecIdOf("variable"));
        Assert.AreEqual((byte)1, catalog.Alignments["_empty"]);
        Assert.AreEqual((byte)8, catalog.Alignments["variable"]);
        Assert.AreEqual(0, catalog.Symbols["_empty"].Symbol.FixedSize);
        Assert.IsNull(catalog.Symbols["variable"].Symbol.FixedSize);
        Assert.IsTrue(catalog.Symbols["_empty"].Symbol.IsFrozen);
        Assert.IsTrue(catalog.Symbols["variable"].Symbol.IsBound);
        CollectionAssert.AreEqual(new[] { empty, variable, }, catalog.CustomCodecs.ToArray());
        Assert.IsFalse(baseline.Symbols.ContainsKey("_empty"));

        // A duplicate in the same batch must be rejected before publishing conflicting codec identifiers.
        ArgumentException failure = Assert.Throws<ArgumentException>(() => baseline.WithCustomCodecs(new[] { empty, empty, }));
        StringAssert.Contains(failure.Message, "Custom codec name '_empty' is already a primitive type.");
    }
}
