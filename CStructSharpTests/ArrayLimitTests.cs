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
    ///     Bounds writer-side array work, rejects values that do not match a fixed declaration, and distinguishes
    ///     writer failures from reader budget failures through their dedicated exception types.
    /// </summary>
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
