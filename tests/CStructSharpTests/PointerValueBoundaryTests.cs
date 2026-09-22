namespace CStructSharp.Tests;

using CStructSharp.Values;

/// <summary>Checks that pointer values cannot claim contradictory null, unresolved and followed states.</summary>
[TestClass]
public class PointerValueBoundaryTests
{
    /// <summary>Each inconsistent state identifies the offending argument and explains why it cannot represent a pointer.</summary>
    /// <param name="address">The stored address payload.</param>
    /// <param name="value">The optional followed target.</param>
    /// <param name="depth">The pointer level.</param>
    /// <param name="followed">Whether the target has been followed.</param>
    /// <param name="parameter">The offending argument name.</param>
    /// <param name="reason">The required error explanation.</param>
    [TestMethod]
    [DataRow(-1L, null, 1, false, "address", "Pointer addresses cannot be negative.")]
    [DataRow(1L, null, 0, false, "depth", "Pointer depth must be greater than zero.")]
    [DataRow(0L, 7, 1, false, "value", "A null pointer cannot contain a target value.")]
    [DataRow(0L, null, 1, true, "isDereferenced", "A null pointer cannot be marked as dereferenced.")]
    [DataRow(1L, 7, 1, false, "value", "An unresolved pointer cannot contain a target value.")]
    [DataRow(1L, null, 1, true, "value", "A dereferenced pointer must contain its target value.")]
    public void InvalidStates_ExplainTheirContradiction(long address, object? value, int depth, bool followed, string parameter, string reason)
    {
        ArgumentException failure = Assert.Throws<ArgumentException>(() => new Pointer(address, value, depth, followed));
        Assert.AreEqual(parameter, failure.ParamName);
        StringAssert.StartsWith(failure.Message, reason);
    }

    /// <summary>A followed pointer's text represents its target, while null and unresolved pointers have no target text.</summary>
    [TestMethod]
    public void Text_UsesTheFollowedTarget()
    {
        Assert.AreEqual(string.Empty, new Pointer(0, null, 1).ToString());
        Assert.AreEqual(string.Empty, new Pointer(7, null, 1).ToString());
        Assert.AreEqual("target", new Pointer(7, "target", 1, true).ToString());
    }
}
