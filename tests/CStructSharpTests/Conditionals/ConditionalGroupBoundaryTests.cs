namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>
///     Checks conditional groups: each selector validated against the expression limits on its own, one shared decision
///     across each group's members, and runtime selectors kept whole when switch labels become constants.
/// </summary>
[TestClass]
public class ConditionalGroupBoundaryTests
{
    /// <summary>Recompiling a definition cannot let an input member replace a construction-time case label.</summary>
    [TestMethod]
    public void DefinitionRoundTrip_PreservesFrozenCaseLabels()
    {
        var layout = new CStruct("#define label 2\nstruct root { uint8 label; uint8 tag; switch (tag) { case label: { uint8 value; } default: { uint8 other; } } };");
        var roundTrip = new CStruct(layout.ToDefinition());
        dynamic parsed = roundTrip.Parse(new byte[] { 1, 2, 42, }.AsSpan(), "root");
        Assert.AreEqual((byte)42, (byte)parsed.value);
    }

    /// <summary>An inline child cannot make later members reevaluate the enclosing branch after changing a variable.</summary>
    [TestMethod]
    public void InlineChild_PreservesTheOuterGroupDecision()
    {
        var layout = new CStruct("#define flag 1\nstruct root { if (flag) { struct { uint8 flag; } child; uint8 chosen; } uint8 tail; };");
        byte[] bytes = [0, 42, 99,];
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual((byte)42, (byte)parsed.chosen);
        Assert.AreEqual((byte)99, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
        Assert.AreEqual(2L, layout.ResolveAddress(bytes, "root.tail"));
    }

    /// <summary>A default-only dispatch still captures its input selector before reading or writing the selected member.</summary>
    [TestMethod]
    public void DefaultOnlySwitch_CapturesItsSelector()
    {
        var layout = new CStruct("struct root { uint8 tag; switch (tag) { default: { uint8 value; } } uint8 tail; };");
        byte[] bytes = [3, 42, 99,];
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual((byte)42, (byte)parsed.value);
        Assert.AreEqual((byte)99, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
    }

    /// <summary>An unsupported selector call cannot hide behind an unconditional default arm.</summary>
    [TestMethod]
    public void DefaultOnlySwitch_ValidatesItsSelectorAtConstruction()
    {
        // Selecting the default arm does not make an invalid selector expression part of the supported language.
        Assert.Throws<CStructLayoutException>(() =>
            new CStruct("struct root { switch (unsupported(1)) { default: { uint8 value; } } };"));
    }

    /// <summary>A switch with a thousand cases compiles and selects each arm, including the default.</summary>
    [TestMethod]
    public void LargeSwitch_CompilesAndSelects()
    {
        string cases = string.Join(" ", Enumerable.Range(0, 1000).Select(index => $"case {index}: {{ uint8 f{index}; }}"));
        var layout = new CStruct($"struct root {{ uint16 k; switch (k) {{ {cases} default: {{ uint16 d; }} }} }};");

        Assert.AreEqual((byte)7, layout.ReadValue<byte>(new byte[] { 0xE7, 0x03, 7, }, "root.f999"));
        Assert.AreEqual((ushort)0x0102, layout.ReadValue<ushort>(new byte[] { 0xE8, 0x03, 2, 1, }, "root.d"));
    }

    /// <summary>
    ///     The rendered definition reads every input like the original: conditional inline structs keep their
    ///     condition, a group decided once stays one group, and empty switch arms still take their values away from
    ///     default.
    /// </summary>
    [TestMethod]
    [DataRow("struct root { uint8 a; if (a) { struct { uint8 x; } inner; } uint8 z; };", "000506", "010506")]
    [DataRow("#define flag 1\nstruct root { if (flag) { struct { uint8 flag; } child; uint8 chosen; } uint8 tail; };", "002a63", "012a63")]
    [DataRow("struct root { uint8 k; switch (k) { case 1: { } case 2: { uint8 b; } default: { uint8 d; } } uint8 t; };", "01aabb", "02aabb", "03aabb")]
    [DataRow("struct root { uint8 k; switch (k) { case 1: { uint8 a; if (a) { uint8 x; } else { struct { uint8 y; } y; } } case 2: { } } uint8 t; };", "01001122", "01011122", "0211", "0511")]
    [DataRow("struct root { uint8 k; if (k) { } else { uint8 e; } uint8 t; };", "001122", "0111")]
    public void DefinitionRoundTrip_ReadsEveryInputLikeTheOriginal(string definition, params string[] inputs)
    {
        var layout = new CStruct(definition);
        var roundTrip = new CStruct(layout.ToDefinition());

        foreach (string input in inputs)
        {
            byte[] bytes = Convert.FromHexString(input);
            Assert.AreEqual(Describe(layout, bytes), Describe(roundTrip, bytes), $"{definition} with input {input}:\n{layout.ToDefinition()}");
        }
    }

    /// <summary>
    ///     Selection evaluates each group's own selector, never a combination of nested conditions, so the limits
    ///     apply to each selector: nested short selectors are accepted and one long selector is rejected.
    /// </summary>
    [TestMethod]
    public void EachSelector_IsCheckedAgainstTheLimitsOnItsOwn()
    {
        var options = new CStructCompilationOptions { MaxExpressionTokens = 2, };

        var nested = new CStruct("struct root { if (1) { if (1) { uint8 value; } } };", compilationOptions: options);
        Assert.AreEqual((byte)7, nested.ReadValue<byte>(new byte[] { 7, }, "root.value"));

        CStructLayoutException error = Assert.Throws<CStructLayoutException>(() =>
            new CStruct("struct root { if (1 + 1 + 1) { uint8 value; } };", compilationOptions: options));
        StringAssert.Contains(error.Message, "Maximum expression evaluation work exceeded");
    }

    /// <summary>Each part of a conditional expression retains its children during case-constant normalization.</summary>
    /// <param name="condition">The expression with a nested condition, true arm or false arm.</param>
    [TestMethod]
    [DataRow("(tag+0)?1:1")]
    [DataRow("tag?(tag+1):1")]
    [DataRow("tag?1:(tag+2)")]
    public void RuntimeConditional_PreservesNestedExpressions(string condition)
    {
        var layout = new CStruct("struct root { uint8 tag; switch (tag) { case 1: {} } if (" + condition + ") { uint8 value; } };");

        dynamic parsed = layout.Parse(new byte[] { 1, 42, }.AsSpan(), "root");

        Assert.AreEqual((byte)42, (byte)parsed.value);
    }

    /// <summary>Lists the path and byte range of every value a parse of <paramref name="bytes"/> records.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="bytes">The input.</param>
    /// <returns>One line per recorded value.</returns>
    private static string Describe(CStruct layout, byte[] bytes)
    {
        return string.Join("\n", layout.ParseWithDebug(bytes, "root").Debug.Select(record => $"{record.Path} {record.Start}-{record.End}"));
    }
}
