namespace CStructSharp.Generated.Parity;

/// <summary>Checks the nested struct values the generated fixed reader returns.</summary>
[TestClass]
public class NestedValueTests
{
    /// <summary>The bytes of one <c>outer</c>: the tag, then <c>first</c> (1, 2) and <c>second</c> (3, 4), little-endian.</summary>
    private static readonly byte[] Bytes = [9, 1, 0, 2, 0, 3, 0, 4, 0,];

    /// <summary>Every parse returns its own nested values, holding the decoded members.</summary>
    [TestMethod]
    public void FixedReader_ReturnsNewNestedValuesForEveryParse()
    {
        NestedValueLayout.Outer first = NestedValueLayout.Parse(Bytes);
        NestedValueLayout.Outer second = NestedValueLayout.Parse(Bytes);

        Assert.AreEqual((byte)9, first.Tag);
        Assert.AreEqual((ushort)1, first.First.A);
        Assert.AreEqual((ushort)2, first.First.B);
        Assert.AreEqual((ushort)3, first.Second.A);
        Assert.AreEqual((ushort)4, first.Second.B);
        Assert.AreNotSame(first.First, first.Second);
        Assert.AreNotSame(first.First, second.First);
        Assert.AreNotSame(first.Second, second.Second);
    }

    /// <summary>
    ///     A consumer's constructor may give nested members a value that is not new: the fixed reader replaces them
    ///     with new decoded values and leaves that value unchanged.
    /// </summary>
    [TestMethod]
    public void FixedReader_ReplacesNestedValuesAConsumerConstructorSupplies()
    {
        NestedConstructorLayout.Outer value = NestedConstructorLayout.Parse(Bytes);

        Assert.AreEqual((ushort)1, value.First.A);
        Assert.AreEqual((ushort)4, value.Second.B);
        Assert.AreNotSame(NestedConstructorLayout.Outer.Shared, value.First);
        Assert.AreNotSame(value.First, value.Second);
        Assert.AreEqual((ushort)0, NestedConstructorLayout.Outer.Shared.A);
        Assert.AreEqual((ushort)0, NestedConstructorLayout.Outer.Shared.B);
    }
}
