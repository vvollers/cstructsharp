namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Codecs;

/// <summary>
///     Checks the primitive catalog: custom-codec descriptors registered before runtime adapter validation can hide
///     catalog errors, an empty registration reusing the built metadata, and shared metadata that never shares a
///     layout's symbols, byte order or pointer placement.
/// </summary>
[TestClass]
public class PrimitiveCatalogTests
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

    /// <summary>Layouts without custom codecs do not allocate replacement catalog dictionaries.</summary>
    [TestMethod]
    [DoNotParallelize]
    [TestCategory(TestCategories.Allocation)]
    public void EmptyRegistration_DoesNotAllocateCatalogCopies()
    {
        PrimitiveCatalog catalog = PrimitiveCatalog.For(true, 64);
        CustomCodecDescriptor[] empty = Array.Empty<CustomCodecDescriptor>();
        for (int index = 0; index < 128; index++)
        {
            _ = catalog.WithCustomCodecs(empty);
        }

        // Both the catalog and empty input are prepared outside the measurement; only registration is measured.
        int totalCodecs = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++)
        {
            totalCodecs += catalog.WithCustomCodecs(empty).CodecCount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(catalog.CodecCount * 256, totalCodecs);
        Assert.AreEqual(0L, allocated, "An empty registration must not copy the cached primitive metadata.");
        Assert.AreEqual(catalog.CodecIdOf("uint8"), catalog.WithCustomCodecs(empty).CodecIdOf("uint8"));
    }

    /// <summary>Concurrent layouts retain independent aliases, byte order and pointer sizes while sharing primitive metadata.</summary>
    [TestMethod]
    public void IndependentLayouts_KeepAliasesAndByteOrdersIsolated()
    {
        Parallel.For(0, 64, index =>
        {
            bool littleEndian = index % 2 == 0;
            byte pointerSize = (byte)(1 << (index % 4));
            string primitive = index % 3 == 0 ? "uint32" : "uint16";
            var layout = new CStruct($"typedef {primitive} value; struct root {{ value number; uint8 *ptr; }};", pointerSize, isLittleEndian: littleEndian);
            var data = new Dictionary<string, object> { ["number"] = 0x1234, ["ptr"] = 0 };
            byte[] bytes = layout.Serialize("root", data);
            int width = primitive == "uint32" ? 4 : 2;
            Assert.AreEqual(width + pointerSize, bytes.Length);
            Assert.AreEqual((byte)0x34, bytes[littleEndian ? 0 : width - 1]);
            Assert.AreEqual((byte)0x12, bytes[littleEndian ? 1 : width - 2]);
            Assert.AreEqual(0x1234, layout.ReadValue<int>(new MemoryStream(bytes), "root.number"));
        });
    }
}
