namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     A count or selector that cannot be evaluated because of the *data* (a decoded value outside the 32-bit
///     expression domain, an arithmetic overflow between decoded values) is a read or write failure that names the
///     value, never a layout failure and never an "undefined identifier".
/// </summary>
[TestClass]
public class OperationExpressionFailureTests
{
    private const string CountedLayout = "struct p { uint32 n; uint8 data[n]; };";

    /// <summary>A uint32 count from 2^31 upward names the field and its value and fails as a read.</summary>
    [TestMethod]
    [DataRow(new byte[] { 0x00, 0x00, 0x00, 0x80, 1, 2, }, "2147483648")]
    [DataRow(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 1, 2, }, "4294967295")]
    public void WideUInt32Count_IsAReadFailureNamingTheValue(byte[] bytes, string value)
    {
        var layout = new CStruct(CountedLayout);

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "p"));

        Assert.AreEqual(CStructErrorCode.ReadFailed, exception.Code);
        StringAssert.Contains(exception.Message, "array length for data");
        StringAssert.Contains(exception.Message, "'n' is " + value);
        StringAssert.Contains(exception.Message, "32-bit range");
        Assert.AreEqual("p", exception.Path);
        Assert.AreEqual(4, exception.Offset);
    }

    /// <summary>A uint64 count beyond the domain reports its exact 64-bit value.</summary>
    [TestMethod]
    public void WideUInt64Count_IsAReadFailureNamingTheValue()
    {
        var layout = new CStruct("struct p { uint64 n; uint8 data[n]; };");
        byte[] bytes = [0, 0, 0, 0, 0, 1, 0, 0, 1, 2,];

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "p"));

        StringAssert.Contains(exception.Message, "'n' is 1099511627776");
        Assert.IsFalse(exception.Message.Contains("Undefined", StringComparison.Ordinal), exception.Message);
    }

    /// <summary>A uint64 count inside the domain still works as before.</summary>
    [TestMethod]
    public void UInt64CountInsideDomain_Reads()
    {
        var layout = new CStruct("struct p { uint64 n; uint8 data[n]; };");
        dynamic value = layout.Parse(new byte[] { 3, 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, }, "p");

        Assert.AreEqual(3, ((IList<object?>)value.data).Count);
    }

    /// <summary>A negative count is a read failure, not a layout failure.</summary>
    [TestMethod]
    public void NegativeCount_IsAReadFailure()
    {
        var layout = new CStruct("struct p { int8 n; uint8 data[n]; };");

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(new byte[] { 0xFF, 1, 2, }, "p"));

        Assert.AreEqual(CStructErrorCode.ReadFailed, exception.Code);
    }

    /// <summary>Arithmetic between decoded values that overflows the domain is a read failure in the expression's own words.</summary>
    [TestMethod]
    public void RuntimeArithmeticOverflow_IsAReadFailure()
    {
        var layout = new CStruct("struct p { uint32 a; uint32 b; uint8 d[a*b]; };");
        byte[] bytes = [0, 0, 1, 0, 0, 0, 1, 0, 1,];

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "p"));

        StringAssert.Contains(exception.Message, "array length for d");
        StringAssert.Contains(exception.Message, "32-bit range");
        Assert.AreEqual(8, exception.Offset);
    }

    /// <summary>A conditional selector fed by a wide decoded value fails as a read that names the value.</summary>
    [TestMethod]
    public void WideSelector_IsAReadFailure()
    {
        var layout = new CStruct("struct p { uint64 kind; if (kind == 1) { uint8 a; } else { uint16 b; } };");
        byte[] bytes = [0, 0, 0, 0, 0, 0, 0, 1, 1, 1,];

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "p"));

        StringAssert.Contains(exception.Message, "conditional selector");
        StringAssert.Contains(exception.Message, "'kind' is 72057594037927936");
    }

    /// <summary>The same count expression fed by supplied values fails a write as a write.</summary>
    [TestMethod]
    public void WideCountOnWrite_IsAWriteFailure()
    {
        var layout = new CStruct(CountedLayout);

        CStructWriteException exception = Assert.Throws<CStructWriteException>(
            () => layout.Serialize("p", new Dictionary<string, object?> { ["n"] = 4294967295u, ["data"] = new byte[] { 1, }, }));

        Assert.AreEqual(CStructErrorCode.WriteFailed, exception.Code);
        StringAssert.Contains(exception.Message, "'n' is 4294967295");
    }

    /// <summary>Construction-time failures keep their layout classification.</summary>
    [TestMethod]
    public void DefineOverflowAtConstruction_StaysALayoutFailure()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("#define N (0x7fffffff + 1)\nstruct p { uint8 d[N]; };"));

        StringAssert.Contains(exception.Message, "32-bit range");
    }
}
