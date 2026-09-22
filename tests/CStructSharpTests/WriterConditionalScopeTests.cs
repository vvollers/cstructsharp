namespace CStructSharp.Tests;

/// <summary>Checks that conditional writers preserve enclosing values across nested declarations with the same name.</summary>
[TestClass]
public class WriterConditionalScopeTests
{
    /// <summary>A nested count must not overwrite the enclosing count used by a later conditional field.</summary>
    /// <param name="declaration">The ordinary or anonymous declaration that supplies the enclosing count.</param>
    [TestMethod]
    [DataRow("uint8 count;")]
    [DataRow("struct { uint8 count; };")]
    public void NestedCount_PreservesTheEnclosingConditionalValue(string declaration)
    {
        var layout = new CStruct("struct child { uint8 count; }; struct root { " + declaration + " child nested; if (count) { uint8 payload; } };");
        var data = new Dictionary<string, object?>
        {
            ["count"] = (byte)1,
            ["nested"] = new Dictionary<string, object?> { ["count"] = (byte)0, },
            ["payload"] = (byte)42,
        };

        CollectionAssert.AreEqual(new byte[] { 1, 0, 42, }, layout.Serialize("root", data));
    }
}
