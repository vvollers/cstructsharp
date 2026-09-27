namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Parsing;

/// <summary>Checks the boundary between a typedef's type spelling and its pointer or array declarator.</summary>
[TestClass]
public class TypedefTokenBoundaryTests
{
    /// <summary>A pointer or array declarator cannot be silently joined into a longer type name.</summary>
    /// <param name="source">A declarator followed by an invalid extra identifier.</param>
    [TestMethod]
    [DataRow("typedef uint8 *first second;")]
    [DataRow("typedef uint8 first[2] second;")]
    public void TypedefDeclarator_RejectsAnExtraName(string source)
    {
        // Check syntax directly so a later unknown-type error cannot mask a lost pointer or array suffix.
        Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLayout(source));
    }

    /// <summary>A union keyword may touch its opening brace without requiring an intervening space.</summary>
    [TestMethod]
    public void CompactUnionTypedef_PreservesItsBody()
    {
        var layout = new CStruct("typedef union{uint8 small;uint16 wide;} root;");
        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
        using var stream = new MemoryStream(new byte[] { 1, 2, });
        Assert.AreEqual(0L, layout.ResolveAddress(stream, "root.small"));
        Assert.AreEqual(0L, layout.ResolveAddress(stream, "root.wide"));
    }
}
