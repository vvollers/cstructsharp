namespace CStructSharp.Tests;

/// <summary>Prepared recognition rules, typed execution, constants and allocation ownership.</summary>
[TestClass]
public class PreparedMatcherTests
{
    /// <summary>Every supported integer width, sign and boolean representation uses exact typed operands.</summary>
    [TestMethod]
    public void IntegerCodecs_PreserveEveryWidthAndSignedness()
    {
        foreach ((string type, int size, string expected) in new[] { ("uint8", 1, "255"), ("int8", 1, "-1"), ("bool", 1, "1"), ("uint16", 2, "65535"), ("int16", 2, "-1"), ("uint24", 3, "16777215"), ("int24", 3, "-1"), ("uint32", 4, "4294967295"), ("int32", 4, "-1"), ("uint64", 8, "0xFFFFFFFFFFFFFFFF"), ("int64", 8, "-1"), })
        {
            PreparedMatcher matcher = new CStruct($"struct h {{ {type} n @expect(n == {expected}); }};").PrepareMatcher("h");
            byte[] bytes = Enumerable.Repeat((byte)255, size).ToArray();
            Assert.IsTrue(matcher.Matches(bytes), type);
            Assert.IsFalse(matcher.Matches(new byte[size]), type);
            Assert.IsFalse(matcher.Matches(bytes.AsSpan(0, size - 1)), type);
        }
    }

    /// <summary>Byte predicates, later offsets, caller slots and expression failures retain runtime matching behavior.</summary>
    [TestMethod]
    public void ComparisonsAndVariables_PreserveFailureAndShortCircuitBehavior()
    {
        PreparedMatcher bytes = new CStruct("struct h { uint8 skip; uint8 sig[2] @magic(sig == b\"AB\"); };").PrepareMatcher("h");
        Assert.IsTrue(bytes.Matches(new byte[] { 0, 65, 66, }));
        Assert.IsFalse(bytes.Matches(new byte[] { 0, 65, 67, }));
        Assert.IsFalse(bytes.Matches(ReadOnlySpan<byte>.Empty));
        Assert.IsFalse(bytes.Matches(new byte[] { 0, 65, }));
        PreparedMatcher variables = new CStruct("struct h { uint8 n @expect(n == LIMIT && 10 / DIVISOR > 0); };").PrepareMatcher("h");
        Assert.IsTrue(variables.Matches(new byte[] { 2, }, new Dictionary<string, int> { ["LIMIT"] = 2, ["DIVISOR"] = 2, ["ignored"] = 9, }));
        Assert.IsFalse(variables.Matches(new byte[] { 2, }, new Dictionary<string, int> { ["LIMIT"] = 2, ["DIVISOR"] = 0, }));
        Assert.IsFalse(new CStruct("struct h { uint8 skip[4]; uint32 n @expect(n == 1); };").PrepareMatcher("h").Matches(new byte[1]));
    }

    /// <summary>A byte-string magic after a runtime array is excluded, so fixed expect rules retain recognition control.</summary>
    [TestMethod]
    public void DynamicByteComparisons_AreExplicitlyUnchecked()
    {
        var layout = new CStruct("struct h { uint8 n @expect(n == 1); uint8 data[n]; uint8 sig[2] @magic(sig == b\"AB\"); };");
        PreparedMatcher matcher = layout.PrepareMatcher("h");
        Assert.IsTrue(matcher.Matches(new byte[] { 1, }));
        Assert.IsFalse(matcher.Matches(new byte[] { 2, }));
        Assert.IsFalse(matcher.Rules.Single(rule => rule.Kind == "magic").Checked);
    }

    /// <summary>Rule reporting distinguishes selected magic, excluded expect and unsupported supports checks.</summary>
    [TestMethod]
    public void Reports_CheckedAndUncheckedRulesAndCachesPlan()
    {
        var layout = new CStruct("struct h { uint8 sig @magic(sig == 1, \"identity\"); uint8 v @expect(v == 2); uint8 feature @supports(feature < 3); };");
        PreparedMatcher matcher = layout.PrepareMatcher("h");
        Assert.AreSame(matcher, layout.PrepareMatcher("h"));
        Assert.AreEqual(3, matcher.Rules.Count);
        Assert.IsTrue(matcher.Rules[0].Checked);
        Assert.AreEqual("identity", matcher.Rules[0].Label);
        Assert.IsFalse(matcher.Rules[1].Checked);
        Assert.IsFalse(matcher.Rules[2].Checked);
        Assert.IsTrue(matcher.Matches(new byte[] { 1, }));
        Assert.IsFalse(matcher.Matches(ReadOnlySpan<byte>.Empty));
    }

    /// <summary>Caller overrides preserve runtime recognition's resolved-constant behavior, without recomputing dependent defines.</summary>
    [TestMethod]
    public void Preserves_ConstantOverridesAndLazyMissingVariables()
    {
        var layout = new CStruct("#define BASE 1\n#define LIMIT (BASE + 1)\nstruct h { uint8 n @expect(n == LIMIT || MISSING == 0); };");
        PreparedMatcher matcher = layout.PrepareMatcher("h");
        var variables = new Dictionary<string, int> { ["BASE"] = 8, };
        Assert.IsTrue(matcher.Matches(new byte[] { 2, }, variables));
        Assert.Throws<ArgumentException>(() => matcher.Matches(new byte[] { 9, }, variables));
        Assert.AreEqual(1, variables.Count);
    }

    /// <summary>Repeated success and rejection do not allocate dictionaries, scalar boxes or scratch arrays after warmup.</summary>
    [TestMethod]
    public void RepeatedIntegerMatching_AllocatesNoManagedStorage()
    {
        PreparedMatcher matcher = new CStruct("struct h { uint64 bits @magic(bits > 0x7FFFFFFFFFFFFFFF && (bits & 255) == 255); };").PrepareMatcher("h");
        byte[] bytes = Enumerable.Repeat((byte)255, 8).ToArray();
        for (int index = 0; index < 1000; index++)
        {
            Assert.IsTrue(matcher.Matches(bytes));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            matcher.Matches(bytes);
        }

        Assert.AreEqual(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }
}
