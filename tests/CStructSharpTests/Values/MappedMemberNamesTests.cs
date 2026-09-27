namespace CStructSharp.Tests;

using CStructSharp.Introspection;
using CStructSharp.Values;

/// <summary>
///     The name rule a mapped property finds its layout member by, shared by the generator and the runtime: exact,
///     then case-insensitive, then ignoring the layout name's underscores; an ambiguous step ends without a match.
/// </summary>
[TestClass]
public class MappedMemberNamesTests
{
    /// <summary>Each step applies only when the earlier ones found nothing, and returns the layout's own spelling.</summary>
    [TestMethod]
    public void Steps_ApplyInOrder()
    {
        Assert.AreEqual("Size", MappedMemberNames.Match(["size", "Size"], "Size"), "exact");
        Assert.AreEqual("size", MappedMemberNames.Match(["size", "bit_depth"], "Size"), "case-insensitive");
        Assert.AreEqual("bit_depth", MappedMemberNames.Match(["size", "bit_depth"], "BitDepth"), "underscores ignored");
        Assert.IsNull(MappedMemberNames.Match(["size"], "Depth"), "no member");
    }

    /// <summary>
    ///     Two case-insensitive matches end the search, even when an underscore spelling would match once, and two
    ///     underscore spellings do too: the property is ambiguous, not bound to a third member.
    /// </summary>
    [TestMethod]
    public void AmbiguousStep_EndsWithoutAMatch()
    {
        Assert.IsNull(MappedMemberNames.Match(["flag", "FLAG", "f_lag"], "Flag"));
        Assert.IsNull(MappedMemberNames.Match(["bit_depth", "bitdep_th"], "BitDepth"));
    }

    /// <summary>The runtime's <see cref="MappedTypes.MemberName"/> applies the same rule to a parsed struct's members and falls back to the property name.</summary>
    [TestMethod]
    public void Runtime_UsesTheSameRule()
    {
        StructValue value = new CStruct("struct r { uint8 flag; uint8 FLAG; uint8 f_lag; uint8 bit_depth; };").Parse(new byte[4], "r");
        Assert.AreEqual("Flag", MappedTypes.MemberName(value, "Flag"));
        Assert.AreEqual("bit_depth", MappedTypes.MemberName(value, "BitDepth"));
    }
}
