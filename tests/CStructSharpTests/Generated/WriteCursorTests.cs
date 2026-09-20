namespace CStructSharpTests.Generated;

using System;
using System.Collections.Generic;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Pins <see cref="WriteCursor"/> against the runtime writer's accounting and failure texts.</summary>
[TestClass]
public class WriteCursorTests
{
    [TestMethod]
    public void Reserve_AdvancesPadsAndReportsCapacityLikeTheRuntime()
    {
        var destination = new byte[6];
        var cursor = new WriteCursor(destination);
        Codec.WriteUInt16(cursor.Reserve(2, "root.a"), 0x0102, littleEndian: false);
        cursor.Pad(2, "root");
        Codec.WriteUInt16(cursor.Reserve(2, "root.b"), 0x0304, littleEndian: false);
        Assert.AreEqual(6, cursor.Length);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 0, 0, 3, 4 }, destination);

        CStructWriteException capacity = Assert.Throws<CStructWriteException>(() =>
        {
            var small = new WriteCursor(new byte[3]);
            small.Reserve(2, "root.a");
            small.Reserve(2, "root.b");
        });
        Assert.AreEqual("root.b", capacity.Path);
        Assert.AreEqual(2L, capacity.Offset);
        var layout = new CStruct("struct root { uint16 a; uint16 b; };");
        CStructWriteException runtime = Assert.Throws<CStructWriteException>(
            () => layout.Serialize(new byte[3].AsSpan(), "root", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2, }));
        StringAssert.Contains(runtime.Message, "The serialized value exceeds the supplied destination capacity");
        StringAssert.Contains(capacity.Message, "The serialized value exceeds the supplied destination capacity");
    }

    [TestMethod]
    public void Limits_MatchTheRuntimeTexts()
    {
        CStructWriteLimitException budget = Assert.Throws<CStructWriteLimitException>(() =>
        {
            var cursor = new WriteCursor(new byte[16], new WriteOptions { MaxTotalBytesWritten = 5, });
            cursor.Reserve(4, "root.a");
            cursor.Reserve(4, "root.b");
        });
        StringAssert.Contains(budget.Message, "Write operation exceeded the configured total byte limit");
        Assert.AreEqual("root.b", budget.Path);

        var limits = new WriteOptions { MaxArrayElements = 2, MaxStringBytes = 3, MaxNestingDepth = 1, };
        var cursor = new WriteCursor(new byte[16], limits);
        cursor.RequireArrayLength(2, "items", "root.items");
        CStructWriteLimitException array = Assert.Throws<CStructWriteLimitException>(() => new WriteCursor(new byte[16], limits).RequireArrayLength(3, "items", "root.items"));
        StringAssert.Contains(array.Message, "Array length exceeds the configured write limit: items");
        cursor.RequireStringBytes(3, "root.name");
        CStructWriteLimitException text = Assert.Throws<CStructWriteLimitException>(() => new WriteCursor(new byte[16], limits).RequireStringBytes(4, "root.name"));
        StringAssert.Contains(text.Message, "String field exceeded the configured encoded-byte write limit");
        CStructWriteLimitException depth = Assert.Throws<CStructWriteLimitException>(() =>
        {
            var nested = new WriteCursor(new byte[16], limits);
            nested.EnterComposite("root");
            nested.ExitComposite();
            nested.EnterComposite("root.again");
            nested.EnterComposite("root.inner");
        });
        StringAssert.Contains(depth.Message, "Maximum nested struct write depth exceeded");
        Assert.AreEqual("root.inner", depth.Path);
        cursor.EnterComposite("root");
        Assert.AreEqual(2, cursor.MaxArrayElements);
        Assert.AreEqual(3L, cursor.MaxStringBytes);
        Assert.AreEqual(PointerAddressingMode.Absolute, cursor.AddressingMode);
        Assert.AreEqual(0L, cursor.Origin);

        var layout = new CStruct("struct root { uint8 count; uint8 items[count]; };");
        CStructWriteLimitException runtime = Assert.Throws<CStructWriteLimitException>(
            () => layout.Serialize("root", new Dictionary<string, object?> { ["count"] = 3, ["items"] = new byte[] { 1, 2, 3 }, }, options: new WriteOptions { MaxArrayElements = 2, }));
        StringAssert.Contains(runtime.Message, "Array length exceeds the configured write limit: items");
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteCursor(new byte[1], new WriteOptions { MaxArrayElements = -1, }));
    }

    [TestMethod]
    public void Align_PadsWithZeroes()
    {
        var destination = new byte[8];
        destination.AsSpan().Fill(0xFF);
        var cursor = new WriteCursor(destination);
        cursor.Reserve(1, "root.a")[0] = 1;
        cursor.Align(4, 0, "root");
        Assert.AreEqual(4, cursor.Position);
        cursor.Align(4, 0, "root");
        cursor.Position = 6;
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF }, destination);
        Assert.Throws<CStructWriteException>(() => new WriteCursor(new byte[2]) { Position = 3, });
    }
}
