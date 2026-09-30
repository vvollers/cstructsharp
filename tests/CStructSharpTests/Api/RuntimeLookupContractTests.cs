namespace CStructSharp.Tests;

using CStructSharp.Codecs;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;

/// <summary>Protects runtime lookup and captured-value contracts that are shared by multiple operation paths.</summary>
[TestClass]
public class RuntimeLookupContractTests
{
    /// <summary>Layouts without custom codecs reuse one immutable table per byte-order and C-long-width setting.</summary>
    /// <param name="littleEndian">The byte order shared by two independently compiled layouts.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PrimitiveLayouts_ReuseTheirCodecTable(bool littleEndian)
    {
        var first = new CStruct("struct first { uint8 value; };", isLittleEndian: littleEndian);
        var second = new CStruct("struct second { uint16 value; };", isLittleEndian: littleEndian);
        var opposite = new CStruct("struct third { uint8 value; };", isLittleEndian: !littleEndian);

        // Inspect the retained table, not delegate equality: rebuilding its arrays would defeat this shared cache.
        System.Reflection.FieldInfo field = typeof(CStruct).GetField("codecs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
        object? table = field.GetValue(first);
        Assert.IsNotNull(table);
        Assert.AreSame(table, field.GetValue(second));
        Assert.AreNotSame(table, field.GetValue(opposite));
    }

    /// <summary>A codec table rejects a writer array that does not cover its catalog, and returns the registered delegate by id.</summary>
    [TestMethod]
    public void CodecTable_RequiresACompleteWriterArrayAndPreservesDelegateIdentity()
    {
        PrimitiveCatalog catalog = PrimitiveCatalog.For(true, 32);
        var writers = new Action<Stream, object>?[catalog.CodecCount];

        // Reject a short array before a later lookup can index beyond it.
        Assert.ThrowsExactly<InvalidOperationException>(() => new CodecTable(catalog, []));
        int id = catalog.CodecIdOf("uint16<");
        writers[id] = WriteByte;
        var table = new CodecTable(catalog, writers);
        Assert.AreSame(writers[id], table.WriterOfCodec(id));
        Assert.IsNull(table.WriterOfCodec(catalog.CodecIdOf("uint32<")));
        Assert.IsNull(table.WriterOfCodec(PrimitiveCatalog.NoCodec));
    }

    /// <summary>Captured integers beyond the signed 128-bit domain retain exact identity and reject evaluation instead of wrapping.</summary>
    [TestMethod]
    public void WideVariable_PreservesEqualityAndRejectsDomainEvaluation()
    {
        var wide = new WideValueVariable(UInt128.MaxValue);
        var same = new WideValueVariable(UInt128.MaxValue);
        Assert.AreEqual(UInt128.MaxValue, wide.WideValue);
        Assert.IsTrue(wide.Equals(same));
        Assert.AreEqual(wide.GetHashCode(), same.GetHashCode());
        Assert.IsFalse(wide.Equals(new WideValueVariable(UInt128.MaxValue - 1)));
        Assert.IsFalse(wide.Equals(new Literal(-1)));
        StringAssert.Contains(wide.ToString(), "340282366920938463463374607431768211455");

        // An expression that selects the variable fails with the exact number in the diagnostic.
        InvalidOperationException evaluated = Assert.ThrowsExactly<InvalidOperationException>(() => new Identifier("w").Evaluate(new Dictionary<string, Expr> { ["w"] = wide, }));
        StringAssert.Contains(evaluated.Message, "340282366920938463463374607431768211455");
        StringAssert.Contains(evaluated.Message, "outside the 128-bit range");

        // A 64-bit value is an ordinary exact variable.
        Assert.AreEqual((Int128)ulong.MaxValue, new Identifier("w").Evaluate(new Dictionary<string, Expr> { ["w"] = new Literal(ulong.MaxValue), }));
    }

    /// <summary>Writes one byte for delegate-identity checks; this is not a uint16 implementation.</summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="value">The byte to write.</param>
    private static void WriteByte(Stream stream, object value) => stream.WriteByte((byte)value);
}
