namespace CStructSharp.Tests;

/// <summary>Checks that stream-based read queries reject a null source before interpreting the path.</summary>
[TestClass]
public class NullReadStreamBoundaryTests
{
    /// <summary>A null stream is the primary argument error even when the selected path is empty.</summary>
    /// <param name="operation">The typed, natural-value, array-length or address-query entry point.</param>
    [TestMethod]
    [DataRow("typed")]
    [DataRow("natural")]
    [DataRow("length")]
    [DataRow("address")]
    public void NullStream_IsRejectedBeforeAnEmptyPath(string operation)
    {
        var layout = new CStruct("struct root { uint8 values[2]; };");

        // The source precondition must be checked before path parsing can report a different error.
        ArgumentNullException failure = Assert.Throws<ArgumentNullException>(() =>
        {
            switch (operation)
            {
            case "typed":
                _ = layout.ReadValue<byte>((Stream)null!, string.Empty);
                break;
            case "natural":
                _ = layout.ReadValue((Stream)null!, string.Empty);
                break;
            case "address":
                _ = layout.ResolveAddress((Stream)null!, string.Empty);
                break;
            default:
                _ = layout.GetArrayLength((Stream)null!, string.Empty);
                break;
            }
        });
        Assert.AreEqual("stream", failure.ParamName);
    }
}
