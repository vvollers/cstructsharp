namespace CStructSharpTests.Generated;

using System;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>
///     Pins <see cref="ReadCursor"/>: the accounting a generated reader performs and the failures it reports are
///     the runtime reader's, checked against what <see cref="CStruct"/> throws for the same bytes and options.
/// </summary>
[TestClass]
public class ReadCursorTests
{
    [TestMethod]
    public void Take_ConsumesChargesAndReportsShortReadsLikeTheRuntime()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var cursor = new ReadCursor(bytes);
        Assert.AreEqual(5, cursor.Remaining);
        Assert.AreEqual((ushort)0x0201, Codec.ReadUInt16(cursor.Take(2, "root.a"), littleEndian: true));
        Assert.AreEqual(2, cursor.Position);
        Assert.AreEqual(3, cursor.Peek(1, "root.b")[0]);
        Assert.AreEqual(2, cursor.Position);

        CStructReadException error = Assert.Throws<CStructReadException>(() =>
        {
            var failing = new ReadCursor(bytes) { Position = 2, };
            failing.Take(4, "root.b");
        });
        Assert.AreEqual("root.b", error.Path);
        Assert.AreEqual(2L, error.Offset);

        // The runtime reports the same text for the same bytes.
        var layout = new CStruct("struct root { uint16 a; uint32 b; };");
        CStructReadException runtime = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "root"));
        Assert.AreEqual(5L, runtime.Offset, "the stream reader reports the end it reached; the cursor reports the item's start");
        StringAssert.Contains(runtime.Message, "Not enough bytes: needed 4, available 3");
        StringAssert.Contains(error.Message, "Not enough bytes: needed 4, available 3");
    }

    [TestMethod]
    public void Budgets_MatchTheRuntimeLimits()
    {
        byte[] bytes = new byte[16];
        var options = new ReadOptions { MaxTotalBytesRead = 6, };
        CStructReadLimitException budget = Assert.Throws<CStructReadLimitException>(() =>
        {
            var cursor = new ReadCursor(bytes, options);
            cursor.Take(4, "root.a");
            cursor.Skip(4, "root.pad");
            cursor.Take(4, "root.b");
        });
        Assert.AreEqual("root.b", budget.Path);
        Assert.AreEqual(8L, budget.Offset);
        var layout = new CStruct("struct root { uint32 a; uint32 pad; uint32 b; };");
        CStructReadLimitException runtime = Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes, "root", options: options));
        Assert.AreEqual(Strip(runtime.Message), Strip(budget.Message));

        var arrays = new ReadCursor(bytes, new ReadOptions { MaxArrayElements = 3, });
        Assert.AreEqual(3, arrays.RequireArrayLength(3, "root.items"));
        CStructReadLimitException tooMany = Assert.Throws<CStructReadLimitException>(() => new ReadCursor(bytes, new ReadOptions { MaxArrayElements = 3, }).RequireArrayLength(4, "root.items"));
        StringAssert.Contains(tooMany.Message, "Array length 4 exceeds MaxArrayElements (3)");
        var countLayout = new CStruct("struct root { uint8 count; uint8 items[count]; };");
        CStructReadLimitException runtimeArray = Assert.Throws<CStructReadLimitException>(
            () => countLayout.Parse(new byte[] { 4, 0, 0, 0, 0 }, "root", options: new ReadOptions { MaxArrayElements = 3, }));
        Assert.AreEqual(Strip(runtimeArray.Message), Strip(tooMany.Message));

        var stringOptions = new ReadOptions { MaxStringBytes = 2, };
        new ReadCursor(bytes, stringOptions).RequireBoundedTextBytes(2, "root.name");
        CStructReadLimitException bounded = Assert.Throws<CStructReadLimitException>(() => new ReadCursor(bytes, stringOptions).RequireBoundedTextBytes(3, "root.name"));
        StringAssert.Contains(bounded.Message, "Encoded text buffer exceeds the configured string byte limit");
        CStructReadLimitException terminated = Assert.Throws<CStructReadLimitException>(() => new ReadCursor(bytes, stringOptions).RequireTerminatedStringBytes(3, "root.name"));
        StringAssert.Contains(terminated.Message, "String field exceeded the configured encoded-byte limit");
    }

    [TestMethod]
    public void NestingAndPointers_EnforceDepthAndAddressing()
    {
        byte[] bytes = [0x04, 0xAA, 0, 0, 0x2A];
        CStructReadLimitException depth = Assert.Throws<CStructReadLimitException>(() =>
        {
            var nested = new ReadCursor(bytes, new ReadOptions { MaxNestingDepth = 1, });
            nested.EnterComposite("root");
            nested.ExitComposite();
            nested.EnterComposite("root.again");
            nested.EnterComposite("root.inner");
        });
        StringAssert.Contains(depth.Message, "Maximum nested struct depth exceeded");
        Assert.AreEqual("root.inner", depth.Path);

        var absolute = new ReadCursor(bytes);
        int resume = absolute.EnterPointer(4, "root.ptr");
        Assert.AreEqual(0, resume);
        Assert.AreEqual(4, absolute.Position);
        Assert.AreEqual(0x2A, absolute.Take(1, "root.ptr.value")[0]);
        absolute.ExitPointer(resume);
        Assert.AreEqual(0, absolute.Position);

        var relative = new ReadCursor(bytes, new ReadOptions { AddressingMode = PointerAddressingMode.Relative, Origin = 3, });
        relative.EnterPointer(1, "root.ptr");
        Assert.AreEqual(4, relative.Position);

        CStructReadException outside = Assert.Throws<CStructReadException>(() =>
        {
            var cursor = new ReadCursor(bytes);
            cursor.EnterPointer(6, "root.ptr");
        });
        StringAssert.Contains(outside.Message, "outside the supplied memory region");

        CStructReadLimitException pointerDepth = Assert.Throws<CStructReadLimitException>(() =>
        {
            var cursor = new ReadCursor(bytes, new ReadOptions { MaxPointerDepth = 1, });
            cursor.EnterPointer(1, "root.ptr");
            cursor.EnterPointer(2, "root.ptr.value");
        });
        StringAssert.Contains(pointerDepth.Message, "Maximum pointer dereference depth exceeded");
        var defaults = new ReadCursor(bytes);
        Assert.IsTrue(defaults.DereferencePointers);
        Assert.AreEqual(16 * 1024 * 1024L, defaults.MaxStringBytes);
        Assert.AreEqual(1_000_000, defaults.MaxArrayElements);
        Assert.IsNull(defaults.MaxPointerTargetBytes);
        Assert.IsFalse(defaults.TrimFixedText);
        Assert.AreEqual(PointerAddressingMode.Absolute, defaults.AddressingMode);
        Assert.AreEqual(0L, defaults.Origin);
        Assert.AreEqual(5, defaults.Source.Length);
    }

    [TestMethod]
    public void Align_PadsFromTheOrigin()
    {
        var cursor = new ReadCursor(new byte[16]);
        cursor.Take(1, "root.a");
        cursor.Align(4, 0, "root");
        Assert.AreEqual(4, cursor.Position);
        cursor.Align(4, 0, "root");
        Assert.AreEqual(4, cursor.Position);
        cursor.Align(8, 2, "root");
        Assert.AreEqual(10, cursor.Position);
        Assert.AreEqual("root.items[3]", ReadCursor.IndexPath("root.items", 3));
        Assert.Throws<CStructReadException>(() =>
        {
            var short1 = new ReadCursor(new byte[3]) { Position = 3, };
            short1.Align(4, 0, "root");
        });
        Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[3]) { Position = 4, });
    }

    private static string Strip(string message) => message[..message.IndexOf(" (", StringComparison.Ordinal)];
}
