namespace CStructSharpTests;

using System.Dynamic;
using CStructSharp;
using CStructSharp.Structure;

/// <summary>
///     Verifies that array work limits and declared-count mismatches fail through the operation-specific exception types.
/// </summary>
[TestClass]
public class ArrayLimitTests
{
    /// <summary>
    ///     values[2] requires exactly two elements.
    /// </summary>
    /// <remarks>
    ///     A write limit of one must reject even a correctly shaped value, and supplying only one element must fail
    ///     even with the default limit. Reading with a restrictive array budget must use the read-limit exception,
    ///     distinguishing excessive read work from invalid write input.
    /// </remarks>
    [TestMethod]
    public void ArrayLimits_AreExplicitAndOperationSpecific()
    {
        var cstruct = new CStruct("struct root { byte values[2]; };");
        dynamic data = new ExpandoObject();
        data.values = new List<object> { (byte)1, (byte)2, };

        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize("root", data, options: new WriteOptions { MaxArrayElements = 1, }));

        data.values = new List<object> { (byte)1, };
        Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", data));

        using var readStream = new MemoryStream([0x2A,]);
        Assert.Throws<CStructReadLimitException>(
            () => cstruct.ParseStream(
                readStream,
                "root",
                new Dictionary<string, Expr>(),
                new ReadOptions { MaxArrayElements = 1, }));
    }
}
