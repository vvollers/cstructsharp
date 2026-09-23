namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks explanatory construction failures for unsupported uses of fixed array aliases.</summary>
[TestClass]
public class UnsupportedAliasShapeTests
{
    /// <summary>Unsupported pointer and open-ended dimensions name the field and the unsupported shape.</summary>
    /// <param name="member">The unsupported member declaration using the fixed array alias.</param>
    /// <param name="reason">The expected shape-specific diagnostic.</param>
    [TestMethod]
    [DataRow("pair *values;", "A pointer to a typedef array is not supported: values")]
    [DataRow("pair values[];", "An unsized array of a typedef array is not supported: values")]
    public void UnsupportedAliasUse_ExplainsTheShape(string member, string reason)
    {
        string definition = "typedef uint8 pair[2]; struct root { " + member + " };";

        // Reject unsupported shapes during construction rather than attempting a binary read.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));

        StringAssert.Contains(failure.Message, reason);
    }
}
