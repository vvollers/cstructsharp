namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The one layout-variable capture rule, checked through every operation that captures: an expression may use only
///     integer fields (enums at their Int32 boundaries included); a field that is not an integer, such as an enum
///     array, fails construction when it alone supplies a name and makes a shared name unusable; unnamed padding
///     publishes nothing; and an integer outside the Int32 range fails with its exact value when used.
/// </summary>
[TestClass]
public class LayoutVariableCaptureRuleTests
{
    /// <summary>A name only a non-integer field supplies fails layout construction and names what the field holds.</summary>
    /// <param name="declaration">The field declaration.</param>
    /// <param name="reason">What the diagnostic says the field is.</param>
    [TestMethod]
    [DataRow("char name[4];", "text")]
    [DataRow("utf8 name[4];", "text")]
    [DataRow("cstring name;", "text")]
    [DataRow("char name[];", "text")]
    [DataRow("uint8 name[2];", "an array")]
    [DataRow("float32 name;", "a floating-point value")]
    [DataRow("fixed16_16 name;", "a fixed-point value")]
    [DataRow("struct { uint8 a; } name;", "a struct")]
    public void NonIntegerField_InAnExpression_FailsConstruction(string declaration, string reason)
    {
        string layout = "struct root { " + declaration + " uint8 data[name]; };";

        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct(layout));
        StringAssert.Contains(failure.Message, $"Field 'name' is {reason}, but a layout expression uses it");
        Assert.AreEqual(layout.IndexOf("name", StringComparison.Ordinal), failure.SourceOffset);
    }

    /// <summary>A definition of the same name keeps the name valid; the definition's value applies until the field is read.</summary>
    [TestMethod]
    public void NonIntegerField_WithADefinitionOfTheSameName_Constructs()
    {
        var layout = new CStruct("#define name 2\nstruct root { uint8 data[name]; char name[2]; };");
        StructValue value = layout.Parse(new byte[] { 1, 2, 65, 66, }, "root");
        Assert.AreEqual("AB", (string)value["name"]!);
    }

    /// <summary>
    ///     A text field sharing its name with an integer field makes the name unusable while its value is in effect, with
    ///     the same message through reading, selected reading, address resolution, updating and writing.
    /// </summary>
    [TestMethod]
    public void SharedName_TextCapture_FailsEveryOperationAlike()
    {
        var layout = new CStruct("struct inner { char n[2]; }; struct root { uint8 n; inner i; uint8 v[n]; };");
        byte[] bytes = [1, 65, 66, 7,];
        const string expected = "'n' is text, but layout expressions can only use integer fields";

        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Parse(bytes, "root")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ReadValue(bytes, "root.v")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ResolveAddress(bytes, "root.v")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => layout.Update(bytes, "root.v", new byte[] { 9, })).Message, expected);
        StringAssert.Contains(
            Assert.Throws<Exception>(() => layout.Serialize("root", new Dictionary<string, object?> { ["n"] = 1, ["i"] = new Dictionary<string, object?> { ["n"] = "AB", }, ["v"] = new byte[] { 7, }, })).Message,
            expected);
    }

    /// <summary>A pointer address outside the Int32 range fails with the exact address through every operation.</summary>
    [TestMethod]
    public void WidePointerAddress_FailsEveryOperationWithItsValue()
    {
        var layout = new CStruct("struct root { uint8 *p; uint8 v[p]; };", pointerSize: 8);
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 7,];
        var options = new ReadOptions { DereferencePointers = false, };
        const string expected = "'p' is 4294967296, which is outside the 32-bit range";

        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Parse(bytes, "root", options: options)).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ReadValue(bytes, "root.v", options: options)).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ResolveAddress(bytes, "root.v", options: options)).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => layout.Update(bytes, "root.v", new byte[] { 9, })).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Serialize("root", new Dictionary<string, object?> { ["p"] = 4294967296L, ["v"] = new byte[] { 7, }, })).Message, expected);
    }

    /// <summary>An enum value outside the Int32 range fails with the exact number through every operation.</summary>
    [TestMethod]
    public void WideEnumValue_FailsEveryOperationWithItsValue()
    {
        var layout = new CStruct("enum big : uint64 { X = 0x100000000 }; struct root { big e; uint8 v[e]; };");
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 7,];
        const string expected = "'e' is 4294967296, which is outside the 32-bit range";

        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Parse(bytes, "root")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ReadValue(bytes, "root.v")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.ResolveAddress(bytes, "root.v")).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => layout.Update(bytes, "root.v", new byte[] { 9, })).Message, expected);
        StringAssert.Contains(Assert.Throws<Exception>(() => _ = layout.Serialize("root", new Dictionary<string, object?> { ["e"] = "X", ["v"] = new byte[] { 7, }, })).Message, expected);
    }

    /// <summary>Integer, character, bool, enum and pointer fields all read as numbers, identically when read and when resolved.</summary>
    [TestMethod]
    public void IntegerFields_ReadTheSameThroughEveryPath()
    {
        var layout = new CStruct(
            "enum kind : uint8 { A = 2 }; struct root { uint8 u; char c; bool b; kind k; uint8 *p; uint8 v[u + b + k + p + (c - 64)]; };",
            pointerSize: 1);
        byte[] bytes = [1, 65, 1, 2, 3, 0, 0, 0, 0, 0, 0, 0, 0,];
        var options = new ReadOptions { DereferencePointers = false, };

        // u + b + k + p + (c - 64) = 1 + 1 + 2 + 3 + 1 = 8 elements after the five one-byte fields.
        StructValue value = layout.Parse(bytes, "root", options: options);
        Assert.HasCount(8, (System.Collections.IList)value["v"]!);
        Assert.AreEqual(5L, layout.ResolveAddress(bytes, "root.v", options: options));
        Assert.AreEqual(8, layout.GetArrayLength(bytes, "root.v", options: options));
    }

    /// <summary>
    ///     An enum array read under a name a definition also supplies makes that name unusable while its value is in
    ///     effect: the array is not an integer, and the definition's older value must not stand in for it.
    /// </summary>
    [TestMethod]
    public void EnumArray_MakesASharedNameUnusable()
    {
        var layout = new CStruct("#define count 1\nenum kind : uint8 { first = 1, second = 2 }; struct root { kind count[2]; uint8 data[count]; uint8 tail; };");
        using var source = new MemoryStream(new byte[] { 1, 2, 0xA5, 0xB6, });
        CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(source, "root"));
        StringAssert.Contains(failure.Message, "'count' is an array");
    }

    /// <summary>Both inclusive Int32 endpoints may select a later field during reading and writing.</summary>
    /// <param name="value">The signed enum value at one expression-domain endpoint.</param>
    /// <param name="hex">The little-endian enum bytes followed by the selected payload.</param>
    [TestMethod]
    [DataRow(int.MinValue, "000000802A")]
    [DataRow(int.MaxValue, "FFFFFF7F2A")]
    public void BoundaryEnumValue_RemainsAvailableToACondition(int value, string hex)
    {
        var layout = new CStruct("enum edge : int32 { Low = -2147483648, High = 2147483647 }; struct root { edge value; if (value != 0) { uint8 payload; } };");
        var data = new Dictionary<string, object?> { ["value"] = value, ["payload"] = (byte)42, };
        byte[] expected = Convert.FromHexString(hex);

        CollectionAssert.AreEqual(expected, layout.Serialize("root", data));
        Assert.AreEqual((byte)42, layout.ReadValue<byte>(expected.AsSpan(), "root.payload"));
    }

    /// <summary>Unnamed padding never captures a layout variable, so it can never publish an empty name.</summary>
    /// <param name="array">Whether the padding is an array.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Padding_DoesNotPublishAnEmptyName(bool array)
    {
        string padding = array ? "uint8 _[2];" : "uint8 _;";
        var layout = new CStruct("struct root { " + padding + " uint8 key; uint8 values[key]; uint8 tail; };");
        Assert.IsFalse(layout.CompiledModel.AllFields().Single(field => field.Name.Length == 0).CapturesLayoutVariable);

        byte[] bytes = array ? [2, 2, 1, 0xAA, 0xBB,] : [2, 1, 0xAA, 0xBB,];
        StructValue result = layout.Parse(bytes.AsSpan(), "root", new Dictionary<string, int> { [string.Empty] = 1, });
        Assert.AreEqual((byte)0xBB, result["tail"]);
        Assert.IsFalse(result.ContainsKey(string.Empty));
    }
}
