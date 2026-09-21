namespace CStructSharp.Tests;

using CStructSharp.Codecs;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;

/// <summary>Protects runtime lookup and captured-value contracts that are shared by multiple operation paths.</summary>
[TestClass]
public class RuntimeLookupContractTests
{
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

    /// <summary>Captured wide integers retain exact identity and reject evaluation instead of wrapping into Int32.</summary>
    [TestMethod]
    public void WideVariable_PreservesEqualityAndRejectsInt32Evaluation()
    {
        var wide = new WideValueVariable(ulong.MaxValue);
        var same = new WideValueVariable(ulong.MaxValue);
        Assert.AreEqual(ulong.MaxValue, wide.WideValue);
        Assert.IsTrue(wide.Equals(same));
        Assert.AreEqual(wide.GetHashCode(), same.GetHashCode());
        Assert.IsFalse(wide.Equals(new WideValueVariable(ulong.MaxValue - 1)));
        Assert.IsFalse(wide.Equals(new Literal(-1)));
        StringAssert.Contains(wide.ToString(), "18446744073709551615");

        // Both expression entry points must retain the exact number in the diagnostic.
        InvalidOperationException direct = Assert.ThrowsExactly<InvalidOperationException>(() => _ = wide.Value);
        InvalidOperationException evaluated = Assert.ThrowsExactly<InvalidOperationException>(() => wide.Calc(new Dictionary<string, Expr>()));
        Assert.AreEqual(direct.Message, evaluated.Message);
        StringAssert.Contains(direct.Message, "18446744073709551615");
        StringAssert.Contains(direct.Message, "outside the 32-bit range");
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
