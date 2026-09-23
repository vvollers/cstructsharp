namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks evaluated typedef-array counts at zero and outside their supported range.</summary>
[TestClass]
public class TypedefArrayBoundaryTests
{
    /// <summary>An expression that evaluates to zero creates an empty alias array without moving the next field.</summary>
    [TestMethod]
    public void EvaluatedZeroCount_KeepsTheFollowingFieldAtItsStart()
    {
        var layout = new CStruct("typedef uint8 empty[1-1]; struct root { empty values; uint8 tail; };");

        dynamic parsed = layout.Parse(new byte[] { 9, }.AsSpan(), "root");

        Assert.AreEqual(0, ((IEnumerable<object?>)parsed.values).Count());
        Assert.AreEqual((byte)9, (byte)parsed.tail);
        Assert.AreEqual(1, layout.GetStructSizeInBytes("root"));
    }

    /// <summary>Invalid evaluated counts identify the alias and the reason its shape cannot be used.</summary>
    /// <param name="count">The count expression, kept non-literal to exercise evaluation.</param>
    /// <param name="reason">The expected alias-specific diagnostic.</param>
    [TestMethod]
    [DataRow("2147483647+1", "Cannot evaluate array length for typedef values:")]
    [DataRow("-1-1", "Array length cannot be negative: values")]
    public void EvaluatedInvalidCount_ReportsTheAlias(string count, string reason)
    {
        string definition = "typedef uint8 values[" + count + "]; struct root { uint8 tail; };";

        // No binary read is needed to diagnose an invalid fixed alias shape.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));

        StringAssert.Contains(failure.Message, reason);
    }
}
