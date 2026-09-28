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

    /// <summary>Codec tables reject incomplete directions and distinguish unknown names from registered delegates.</summary>
    [TestMethod]
    public void CodecTable_RequiresCompleteDirectionsAndPreservesDelegateIdentity()
    {
        PrimitiveCatalog catalog = PrimitiveCatalog.For(true, 32);
        var readers = new Func<Stream, object>?[catalog.CodecCount];
        var writers = new Action<Stream, object>?[catalog.CodecCount];

        // Reject either missing direction before a later lookup can index beyond the supplied array.
        Assert.ThrowsExactly<InvalidOperationException>(() => new CodecTable(catalog, [], writers));
        Assert.ThrowsExactly<InvalidOperationException>(() => new CodecTable(catalog, readers, []));
        readers[catalog.CodecIdOf("uint16")] = ReadByte;
        writers[catalog.CodecIdOf("uint16")] = WriteByte;
        var table = new CodecTable(catalog, readers, writers);
        Assert.AreSame(readers[catalog.CodecIdOf("uint16")], table.ReaderOf("uint16<"));
        Assert.AreSame(writers[catalog.CodecIdOf("uint16")], table.WriterOf("uint16<"));
        Assert.IsNull(table.ReaderOf("not_registered"));
        Assert.IsNull(table.WriterOf("not_registered"));
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

    /// <summary>Reads one byte for delegate-identity checks; this is not a uint16 implementation.</summary>
    /// <param name="stream">The source stream.</param>
    /// <returns>The byte or end-of-stream marker.</returns>
    private static object ReadByte(Stream stream) => stream.ReadByte();

    /// <summary>Writes one byte for delegate-identity checks; this is not a uint16 implementation.</summary>
    /// <param name="stream">The destination stream.</param>
    /// <param name="value">The byte to write.</param>
    private static void WriteByte(Stream stream, object value) => stream.WriteByte((byte)value);
}
