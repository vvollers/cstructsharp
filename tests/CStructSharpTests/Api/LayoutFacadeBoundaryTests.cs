namespace CStructSharp.Tests;

using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>Checks layout-construction and array-query errors at the public facade.</summary>
[TestClass]
public class LayoutFacadeBoundaryTests
{
    /// <summary>An invalid member identifier reports both the rejected name and its complete path.</summary>
    [TestMethod]
    public void Path_InvalidIdentifierExplainsItsLocation()
    {
        CStructPathException failure = Assert.Throws<CStructPathException>(() => CStructPathResolver.Parse("root.bad-name"));
        Assert.AreEqual("Invalid path name 'bad-name' in 'root.bad-name'.", failure.Message);
    }

    /// <summary>A valid prelude must not conceal a missing layout argument by making the concatenated source valid.</summary>
    [TestMethod]
    public void Constructor_NullLayoutWithPreludeRemainsInvalid()
    {
        var options = new CStructCompilationOptions { Prelude = "struct root { uint8 value; };", };
        ArgumentNullException failure = Assert.Throws<ArgumentNullException>(() => new CStruct(null!, compilationOptions: options));
        Assert.AreEqual("layout", failure.ParamName);
    }

    /// <summary>Unsupported pointer widths name the supported byte widths in the argument error.</summary>
    [TestMethod]
    public void Constructor_InvalidPointerWidthExplainsSupportedSizes()
    {
        ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() => new CStruct("struct root { uint8 value; };", pointerSize: 3));
        Assert.AreEqual("pointerSize", failure.ParamName);
        StringAssert.StartsWith(failure.Message, "Pointer size must be 1, 2, 4, or 8 bytes.");
    }

    /// <summary>A null codec entry is rejected as caller input before descriptor properties are accessed.</summary>
    [TestMethod]
    public void Constructor_NullCodecNamesItsArgument()
    {
        var options = new CStructCompilationOptions { Codecs = new ICustomCodec[] { null!, }, };
        ArgumentNullException failure = Assert.Throws<ArgumentNullException>(() => new CStruct("struct root { uint8 value; };", compilationOptions: options));
        Assert.AreEqual("codecs", failure.ParamName);
    }

    /// <summary>A whole composite does not supply a field's array length and the error identifies that distinction.</summary>
    [TestMethod]
    public void ArrayLength_CompositeRootExplainsMissingField()
    {
        var layout = new CStruct("struct root { uint8 value; };");
        using var input = new MemoryStream(new byte[] { 9, });
        CStructPathException failure = Assert.Throws<CStructPathException>(() => layout.GetArrayLength(input, "root"));
        StringAssert.Contains(failure.Message, "Path does not resolve to an array or string field: root");
        Assert.AreEqual(0L, input.Position);
    }

    /// <summary>A scalar character is not a terminated string, and neither scalar kind has an array count.</summary>
    /// <param name="type">The ordinary scalar storage type.</param>
    [TestMethod]
    [DataRow("uint8")]
    [DataRow("char")]
    public void ArrayLength_ScalarExplainsItsInvalidSelection(string type)
    {
        var layout = new CStruct($"struct root {{ {type} value; }};");
        using var input = new MemoryStream(new byte[] { 65, 0, });
        CStructPathException failure = Assert.Throws<CStructPathException>(() => layout.GetArrayLength(input, "root.value"));
        StringAssert.Contains(failure.Message, "Path does not resolve to an array or string: root.value");
        Assert.AreEqual(0L, input.Position);
    }

    /// <summary>Parsing a typedef with a different struct tag selects the aliased value in both ordinary and debug modes.</summary>
    [TestMethod]
    public void Parse_TaggedAliasReturnsItsComposite()
    {
        var layout = new CStruct("typedef struct internal_tag { uint8 value; } external_name;");
        byte[] bytes = { 23, };
        Assert.AreEqual((byte)23, layout.Parse(bytes, "external_name")["value"]);
        Assert.AreEqual((byte)23, layout.ParseWithDebug(bytes, "external_name").Value["value"]);
    }
}
