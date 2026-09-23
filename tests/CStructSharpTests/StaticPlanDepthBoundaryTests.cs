namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks that a completed fixed read preserves the nesting depth of subsequent runtime-sized records.</summary>
[TestClass]
public class StaticPlanDepthBoundaryTests
{
    /// <summary>A fixed header cannot release its parent's nesting level before a deeper runtime-sized sibling.</summary>
    [TestMethod]
    public void FixedHeader_PreservesDepthForFollowingRuntimeRecord()
    {
        var layout = new CStruct("struct header { uint8 value; }; struct leaf { uint8 count; uint8 data[count]; }; struct branch { leaf child; }; struct root { header prefix; branch body; };");
        byte[] bytes = [9, 1, 42,];

        // The root, branch and leaf require three levels even after the fixed header has completed.
        Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes.AsSpan(), "root", options: new ReadOptions { MaxNestingDepth = 2, }));

        dynamic parsed = layout.Parse(bytes.AsSpan(), "root", options: new ReadOptions { MaxNestingDepth = 3, });

        Assert.AreEqual((byte)9, (byte)parsed.prefix.value);
        Assert.AreEqual((byte)42, (byte)parsed.body.child.data[0]);
    }
}
