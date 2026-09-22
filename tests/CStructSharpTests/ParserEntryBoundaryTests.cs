namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Introspection;
using CStructSharp.Parsing;

/// <summary>Checks direct enum-member errors and token separation in continued opaque definitions.</summary>
[TestClass]
public class ParserEntryBoundaryTests
{
    /// <summary>The single-member parser rejects missing names with the expected corrective diagnostic.</summary>
    /// <param name="source">Text without a valid enum member name.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("! = 2")]
    public void EnumMember_RequiresAName(string source)
    {
        // Exercise the direct member entry point without a surrounding enum body's token guard.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseEnumValue(source));
        StringAssert.Contains(failure.Message, "an enum member name");
    }

    /// <summary>A joined physical line inserts one separator when an opaque body did not already end in a space.</summary>
    /// <param name="newline">The physical line-ending spelling following a backslash.</param>
    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void ContinuedOpaqueBodies_KeepTokensSeparate(string newline)
    {
        string join = "\\" + newline;
        var layout = new CStruct("#define TEXT LEFT" + join + "RIGHT\n#define MACRO(x) LEFT" + join + "RIGHT\nstruct root { uint8 value; };");
        Assert.AreEqual(LayoutConstantKind.Text, layout.Constants["TEXT"].Kind);
        Assert.AreEqual("LEFT RIGHT", layout.Constants["TEXT"].Value);
        Assert.AreEqual(LayoutConstantKind.Macro, layout.Constants["MACRO"].Kind);
        Assert.AreEqual("(x) LEFT RIGHT", layout.Constants["MACRO"].Value);
        Assert.AreEqual(1, layout.GetStructSizeInBytes("root"));
    }
}
