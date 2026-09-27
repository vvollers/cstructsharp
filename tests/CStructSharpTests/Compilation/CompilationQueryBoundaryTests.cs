namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks named composite queries and expression normalization within conditional declarations.</summary>
[TestClass]
public class CompilationQueryBoundaryTests
{
    /// <summary>Size and alignment queries distinguish missing declarations from aliases that are not scalar composites.</summary>
    /// <param name="name">The invalid composite query name.</param>
    /// <param name="message">The exact reason the name cannot identify one composite.</param>
    [TestMethod]
    [DataRow("pointer", "Declaration is not a struct: pointer")]
    [DataRow("array", "Declaration is not a struct: array")]
    [DataRow("scalar", "Declaration is not a struct: scalar")]
    [DataRow("missing", "Unknown struct declaration: missing")]
    public void NonCompositeQueries_ExplainTheRejectedName(string name, string message)
    {
        var layout = new CStruct("struct item { uint8 value; }; typedef item *pointer; typedef item array[2]; typedef uint8 scalar;");

        // A pointer's pointee and an array's element are not the storage object the caller named.
        Assert.AreEqual(message, Assert.Throws<CStructPathException>(() => layout.GetStructSizeInBytes(name)).Message);

        // Alignment uses the same composite-name contract as size.
        Assert.AreEqual(message, Assert.Throws<CStructPathException>(() => layout.GetStructAlignmentInBytes(name)).Message);
    }

    /// <summary>Both composite queries identify a missing argument before resolving any declaration.</summary>
    [TestMethod]
    public void NullCompositeName_IdentifiesTheArgument()
    {
        var layout = new CStruct("struct root { uint8 value; };");

        // A null name must not degrade into an unknown-name lookup failure.
        Assert.AreEqual("name", Assert.Throws<ArgumentNullException>(() => layout.GetStructSizeInBytes(null!)).ParamName);

        // Alignment follows the same argument validation route.
        Assert.AreEqual("name", Assert.Throws<ArgumentNullException>(() => layout.GetStructAlignmentInBytes(null!)).ParamName);
    }

    /// <summary>A conditional expression inside a switch preserves its selector and both branches during normalization.</summary>
    /// <param name="hex">Input bytes selecting a switch arm and a conditional branch, followed by the tail.</param>
    [TestMethod]
    [DataRow("01012A63")]
    [DataRow("010063")]
    [DataRow("00012A63")]
    public void ConditionalInsideSwitch_PreservesBothTernaryArms(string hex)
    {
        var layout = new CStruct("struct root { uint8 tag; uint8 flag; switch (tag) { case 1: { if (flag ? 1 : 0) { uint8 chosen; } } default: { uint8 other; } } uint8 tail; };");
        byte[] bytes = Convert.FromHexString(hex);
        Assert.AreEqual((byte)99, layout.Parse(bytes, "root").Get<byte>("tail"));
        Assert.AreEqual((long)(bytes.Length - 1), layout.ResolveAddress(bytes, "root.tail"));
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(bytes, "root.tail"));
    }
}
