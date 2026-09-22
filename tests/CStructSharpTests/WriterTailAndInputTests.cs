namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks runtime-sized struct tails and early writer input diagnostics.</summary>
[TestClass]
public class WriterTailAndInputTests
{
    /// <summary>Nested tail padding advances the next field; updates retain existing padding bytes.</summary>
    /// <param name="update">Whether to replace existing storage instead of creating a new byte array.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NestedRuntimeTail_PlacesTheFollowingField(bool update)
    {
        var layout = new CStruct("struct inner { uint32 marker; uint8 count; uint8 values[count]; }; struct root { inner child; uint8 tail; };", aligned: true);
        var child = new Dictionary<string, object?>
        {
            ["marker"] = 0x01020304U,
            ["count"] = (byte)2,
            ["values"] = new byte[] { 11, 22, },
        };
        var data = new Dictionary<string, object?> { ["child"] = child, ["tail"] = (byte)99, };
        byte padding = update ? (byte)0xAA : (byte)0;
        byte[] expected = [4, 3, 2, 1, 2, 11, 22, padding, 99, padding, padding, padding,];
        if (update)
        {
            byte[] original = Enumerable.Repeat((byte)0xAA, 12).ToArray();
            original[4] = 2;
            using var destination = new MemoryStream(original);
            layout.Update(destination, "root", data);
            CollectionAssert.AreEqual(expected, destination.ToArray());
            Assert.AreEqual(0L, destination.Position);
        }
        else
        {
            CollectionAssert.AreEqual(expected, layout.Serialize("root", data));
        }
    }

    /// <summary>A null composite reports its declared name before attempting member materialization.</summary>
    [TestMethod]
    public void NullStruct_IdentifiesTheRequiredComposite()
    {
        var layout = new CStruct("struct root { uint8 value; };");

        // The root must be a member source even though its one member has a simple byte representation.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", null!));
        StringAssert.StartsWith(failure.Message, "Null is not valid for struct or union value: root");
    }

    /// <summary>A read-only destination produces an actionable argument error without moving its origin.</summary>
    [TestMethod]
    public void ReadOnlyDestination_ExplainsRequiredCapabilities()
    {
        var layout = new CStruct("typedef uint8 item;");
        using var destination = new MemoryStream(new byte[] { 11, 22, }, writable: false) { Position = 1, };

        // Destination capabilities are checked before selecting or encoding the value.
        ArgumentException failure = Assert.Throws<ArgumentException>(() => layout.Write(destination, "item", (byte)7));
        Assert.AreEqual("stream", failure.ParamName);
        StringAssert.StartsWith(failure.Message, "Writing requires a writable, seekable stream.");
        Assert.AreEqual(1L, destination.Position);
        CollectionAssert.AreEqual(new byte[] { 11, 22, }, destination.ToArray());
    }
}
