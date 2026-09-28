namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     A count or selector that cannot be used because of the *data* (a decoded value outside the 128-bit expression
///     domain, an arithmetic overflow between decoded values, a count beyond the element limit) is a read or write
///     failure that names the value, never a layout failure and never an "undefined identifier".
/// </summary>
[TestClass]
public class OperationExpressionFailureTests
{
    private const string CountedLayout = "struct p { uint32 n; uint8 data[n]; };";

    /// <summary>A uint32 or uint64 count beyond the element limit keeps its exact value, which the read limit failure names.</summary>
    /// <param name="layoutText">The layout, whose first field <c>n</c> sizes <c>data</c>.</param>
    /// <param name="bytes">The input, starting with the little-endian count <c>n</c>.</param>
    /// <param name="value">The count as the decimal text the message must contain.</param>
    /// <param name="offset">The offset of <c>data</c>, where the failure is reported.</param>
    [TestMethod]
    [DataRow(CountedLayout, new byte[] { 0x00, 0x00, 0x00, 0x80, 1, 2, }, "2147483648", 4)]
    [DataRow(CountedLayout, new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 1, 2, }, "4294967295", 4)]
    [DataRow("struct p { uint64 n; uint8 data[n]; };", new byte[] { 0, 0, 0, 0, 0, 1, 0, 0, 1, 2, }, "1099511627776", 8)]
    [DataRow("struct p { uint64 n; uint8 data[n]; };", new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 2, }, "18446744073709551615", 8)]
    public void CountBeyondTheElementLimit_IsAReadLimitFailureNamingTheValue(string layoutText, byte[] bytes, string value, int offset)
    {
        var layout = new CStruct(layoutText);

        CStructReadLimitException exception = Assert.ThrowsExactly<CStructReadLimitException>(() => layout.Parse(bytes, "p"));

        StringAssert.StartsWith(exception.Message, "Array length " + value + " exceeds MaxArrayElements (");
        Assert.AreEqual("p", exception.Path);
        Assert.AreEqual(offset, exception.Offset);
    }

    /// <summary>A uint128 count beyond the signed 128-bit domain names the field and its value and fails as a read.</summary>
    [TestMethod]
    public void WideUInt128Count_IsAReadFailureNamingTheValue()
    {
        var layout = new CStruct("struct p { uint128 n; uint8 data[n]; };");
        byte[] bytes = new byte[18];
        bytes[15] = 0x80;

        CStructReadException exception = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(bytes, "p"));

        Assert.AreEqual(CStructErrorCode.ReadFailed, exception.Code);
        StringAssert.Contains(exception.Message, "array length for data");
        StringAssert.Contains(exception.Message, "'n' is 170141183460469231731687303715884105728, which is outside the 128-bit range");
        Assert.IsFalse(exception.Message.Contains("Undefined", StringComparison.Ordinal), exception.Message);
        Assert.AreEqual(16, exception.Offset);
    }

    /// <summary>A uint64 count inside the element limit sizes the array normally.</summary>
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

    /// <summary>Arithmetic between decoded values that overflows the 128-bit domain is a read failure in the expression's own words.</summary>
    [TestMethod]
    public void RuntimeArithmeticOverflow_IsAReadFailure()
    {
        // 2^63 * 2^63 * 2^63 = 2^189, far beyond the signed 128-bit range.
        var layout = new CStruct("struct p { uint64 a; uint64 b; uint64 c; uint8 d[a*b*c]; };");
        byte[] bytes = new byte[25];
        bytes[7] = bytes[15] = bytes[23] = 0x80;

        CStructReadException exception = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(bytes, "p"));

        StringAssert.Contains(exception.Message, "Cannot evaluate array length for d: the result is outside the 128-bit range that layout expressions support");
        Assert.AreEqual(24, exception.Offset);
    }

    /// <summary>A conditional selector fed by a value beyond the domain fails as a read that names the value.</summary>
    [TestMethod]
    public void WideSelector_IsAReadFailure()
    {
        var layout = new CStruct("struct p { uint128 kind; if (kind == 1) { uint8 a; } else { uint16 b; } };");
        byte[] bytes = new byte[18];
        bytes[15] = 0xFF;

        CStructReadException exception = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "p"));

        StringAssert.Contains(exception.Message, "conditional selector");
        StringAssert.Contains(exception.Message, "'kind' is 338953138925153547590470800371487866880");
    }

    /// <summary>The same count expression fed by supplied values fails a write as a write, and a large count meets the write limit.</summary>
    [TestMethod]
    public void WideCountOnWrite_IsAWriteFailure()
    {
        var layout = new CStruct("struct p { uint128 n; uint8 data[n]; };");

        CStructWriteException exception = Assert.ThrowsExactly<CStructWriteException>(
            () => layout.Serialize("p", new Dictionary<string, object?> { ["n"] = UInt128.MaxValue, ["data"] = new byte[] { 1, }, }));

        Assert.AreEqual(CStructErrorCode.WriteFailed, exception.Code);
        StringAssert.Contains(exception.Message, "'n' is 340282366920938463463374607431768211455");

        CStructWriteLimitException limit = Assert.ThrowsExactly<CStructWriteLimitException>(
            () => new CStruct(CountedLayout).Serialize("p", new Dictionary<string, object?> { ["n"] = 4294967295u, ["data"] = new byte[] { 1, }, }));
        StringAssert.Contains(limit.Message, "Array length exceeds the configured write limit: data");
    }

    /// <summary>Construction-time failures keep their layout classification.</summary>
    [TestMethod]
    public void DefineOverflowAtConstruction_StaysALayoutFailure()
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new CStruct("#define N (0x7FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFF + 1)\nstruct p { uint8 d[N]; };"));

        StringAssert.Contains(exception.Message, "'N' is 170141183460469231731687303715884105728, which is outside the 128-bit range");
    }
}
