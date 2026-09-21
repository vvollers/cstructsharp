namespace CStructSharpTests.Generated;

using System;
using CStructSharp;
using CStructSharp.Values;

/// <summary>Pins <see cref="MappedTypes.MemberName"/>, the run-time name match a generated mapper without a resolved layout uses.</summary>
[TestClass]
public class MappedTypesTests
{
    /// <summary><c>MemberName</c> prefers the exact spelling, then the single case-insensitive match, then the single match with underscores ignored, and otherwise returns the property name so the following read reports it as missing.</summary>
    [TestMethod]
    public void MemberName_MatchesExactThenCaseInsensitiveThenWithoutUnderscores()
    {
        var layout = new CStruct("struct root { uint8 Kind; uint8 kind; uint8 bit_depth; uint8 Bit_Depth; uint8 color_type; uint8 tail; };");
        StructValue value = layout.Parse(new byte[] { 1, 2, 3, 4, 5, 6 }, "root");

        Assert.AreEqual("kind", MappedTypes.MemberName(value, "kind"), "an exact match wins even when a case-insensitive rival exists");
        Assert.AreEqual("Kind", MappedTypes.MemberName(value, "Kind"));
        Assert.AreEqual("tail", MappedTypes.MemberName(value, "Tail"), "one case-insensitive match");
        Assert.AreEqual("KIND", MappedTypes.MemberName(value, "KIND"), "two case-insensitive candidates: the property name comes back unresolved");
        Assert.AreEqual("color_type", MappedTypes.MemberName(value, "ColorType"), "one match with underscores ignored");
        Assert.AreEqual("BitDepth", MappedTypes.MemberName(value, "BitDepth"), "two underscore candidates: unresolved");
        Assert.AreEqual("Missing", MappedTypes.MemberName(value, "Missing"), "no candidate: unresolved");

        // The runtime's Get reports the unresolved name with the members that exist.
        var layoutWithoutMatch = new CStruct("struct root { uint8 a_b; uint8 A_b; uint8 ab; };");
        StructValue ambiguous = layoutWithoutMatch.Parse(new byte[] { 1, 2, 3 }, "root");
        Assert.AreEqual("ab", MappedTypes.MemberName(ambiguous, "AB"), "a single case-insensitive match is taken before underscores are considered");
        var tie = new CStruct("struct root { uint8 a_b; uint8 A_b; };").Parse(new byte[] { 1, 2 }, "root");
        Assert.AreEqual("AB", MappedTypes.MemberName(tie, "AB"), "two members tie once underscores are ignored: unresolved");

        var leading = new CStruct("struct root { uint8 _value; };").Parse(new byte[] { 7 }, "root");
        Assert.AreEqual("_value", MappedTypes.MemberName(leading, "Value"), "an underscore at index zero is still ignored");
        Assert.AreEqual((byte)7, leading.Get<byte>(MappedTypes.MemberName(leading, "Value")));

        Assert.Throws<ArgumentNullException>(() => MappedTypes.MemberName(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => MappedTypes.MemberName(value, null!));
    }
}
