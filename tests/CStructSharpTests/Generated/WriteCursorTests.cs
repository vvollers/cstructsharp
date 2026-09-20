namespace CStructSharpTests.Generated;

using System;
using System.Collections.Generic;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Pins <see cref="WriteCursor"/> against the runtime writer's accounting, failure texts, and context.</summary>
[TestClass]
public class WriteCursorTests
{
    [TestMethod]
    public void Reserve_AdvancesPadsAndReportsCapacityLikeTheRuntime()
    {
        var destination = new byte[6];
        var cursor = new WriteCursor(destination, path: "root");
        Codec.WriteUInt16(cursor.Reserve(2, "a", "uint16"), 0x0102, littleEndian: false);
        cursor.Pad(2, "a", "uint16");
        Codec.WriteUInt16(cursor.Reserve(2, "b", "uint16"), 0x0304, littleEndian: false);
        Assert.AreEqual(6, cursor.Length);
        Assert.AreEqual("root", cursor.Path);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 0, 0, 3, 4 }, destination);

        CStructWriteException capacity = Assert.Throws<CStructWriteException>(() =>
        {
            var small = new WriteCursor(new byte[3], path: "root");
            small.Reserve(2, "a", "uint16");
            small.Reserve(2, "b", "uint16");
        });
        var layout = new CStruct("struct root { uint16 a; uint16 b; };");
        CStructWriteException runtime = Assert.Throws<CStructWriteException>(
            () => layout.Serialize(new byte[3].AsSpan(), "root", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2, }));
        Assert.AreEqual(runtime.Message, capacity.Message);
        Assert.AreEqual("The serialized value exceeds the supplied destination capacity (field 'b' (uint16), in 'root', offset 2).", capacity.Message);
    }

    [TestMethod]
    public void Limits_MatchTheRuntimeTexts()
    {
        CStructWriteLimitException budget = Assert.Throws<CStructWriteLimitException>(() =>
        {
            var cursor = new WriteCursor(new byte[16], new WriteOptions { MaxTotalBytesWritten = 5, }, "root");
            cursor.Reserve(4, "a", "uint32");
            cursor.Reserve(4, "b", "uint32");
        });
        StringAssert.StartsWith(budget.Message, "Write operation exceeded the configured total byte limit");
        Assert.AreEqual("b", budget.Member);
        Assert.AreEqual("root", budget.Path);

        var limits = new WriteOptions { MaxArrayElements = 2, MaxStringBytes = 3, MaxNestingDepth = 1, };
        var cursor = new WriteCursor(new byte[16], limits, "root");
        cursor.RequireArrayLength(2, "items", "uint8");
        CStructWriteLimitException array = Assert.Throws<CStructWriteLimitException>(() =>
        {
            var failing = new WriteCursor(new byte[16], limits, "root");
            failing.Reserve(1, "count", "uint8");
            failing.RequireArrayLength(3, "items", "uint8");
        });
        var layout = new CStruct("struct root { uint8 count; uint8 items[count]; };");
        CStructWriteLimitException runtime = Assert.Throws<CStructWriteLimitException>(
            () => layout.Serialize("root", new Dictionary<string, object?> { ["count"] = 3, ["items"] = new byte[] { 1, 2, 3 }, }, options: limits));
        Assert.AreEqual(runtime.Message, array.Message);
        Assert.AreEqual("Array length exceeds the configured write limit: items (field 'items' (uint8), in 'root', offset 1).", array.Message);

        cursor.RequireStringBytes(3, "name", "cstring");
        CStructWriteLimitException text = Assert.Throws<CStructWriteLimitException>(() => new WriteCursor(new byte[16], limits).RequireStringBytes(4, "name", "cstring"));
        StringAssert.StartsWith(text.Message, "String field exceeded the configured encoded-byte write limit");
        CStructWriteLimitException depth = Assert.Throws<CStructWriteLimitException>(() =>
        {
            var nested = new WriteCursor(new byte[16], limits, "root");
            nested.EnterComposite("root", null);
            nested.ExitComposite();
            nested.EnterComposite("again", "outer");
            nested.EnterComposite("inner", "inner");
        });
        StringAssert.StartsWith(depth.Message, "Maximum nested struct write depth exceeded");
        Assert.AreEqual("inner", depth.Member);
        cursor.EnterComposite("root", null);
        Assert.AreEqual(2, cursor.MaxArrayElements);
        Assert.AreEqual(3L, cursor.MaxStringBytes);
        Assert.AreEqual(PointerAddressingMode.Absolute, cursor.AddressingMode);
        Assert.AreEqual(0L, cursor.Origin);
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteCursor(new byte[1], new WriteOptions { MaxArrayElements = -1, }));
    }

    [TestMethod]
    public void FailUnwritable_MatchesTheRuntimeValueDiagnostics()
    {
        var layout = new CStruct("struct root { uint8 a; uint16 b; };");
        var cursor = new WriteCursor(new byte[3], path: "root");
        cursor.Reserve(1, "a", "uint8");

        CStructWriteException range = Assert.Throws<CStructWriteException>(
            () => layout.Serialize("root", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 70000, }));
        CStructWriteException generatedRange = cursor.FailUnwritable(70000, "uint16", "0 to 65535", "b", new OverflowException());

        // The runtime's static write plan validates the whole block before writing a byte, so it reports offset 0
        // where the sequential generated writer has already placed 'a'; the words, field, and path are the same.
        Assert.AreEqual(WithoutOffset(range.Message), WithoutOffset(generatedRange.Message));
        Assert.AreEqual(0L, range.Offset);
        Assert.AreEqual("Value 70000 does not fit: uint16 accepts 0 to 65535 (field 'b' (uint16), in 'root', offset 1).", generatedRange.Message);
        Assert.IsInstanceOfType<OverflowException>(generatedRange.InnerException);

        CStructWriteException kind = Assert.Throws<CStructWriteException>(
            () => layout.Serialize("root", new Dictionary<string, object?> { ["a"] = 1, ["b"] = "abc", }));
        CStructWriteException generatedKind = cursor.FailUnwritable("abc", "uint16", "0 to 65535", "b");
        Assert.AreEqual(WithoutOffset(kind.Message), WithoutOffset(generatedKind.Message));
        Assert.AreEqual("Value \"abc\" cannot be written as uint16 (field 'b' (uint16), in 'root', offset 1).", generatedKind.Message);
        Assert.IsNull(generatedKind.InnerException);

        Exception expression = cursor.FailExpression(new DivideByZeroException("Attempted to divide by zero."), "array length for items", "items", "uint8");
        Assert.IsInstanceOfType<CStructWriteException>(expression);
        StringAssert.StartsWith(expression.Message, "Cannot evaluate array length for items: Attempted to divide by zero");
        var unrelated = new ArgumentNullException("x");
        Assert.AreSame(unrelated, cursor.FailExpression(unrelated, "array length for items", "items", "uint8"));
    }

    private static string WithoutOffset(string message) => System.Text.RegularExpressions.Regex.Replace(message, @", offset \d+", string.Empty);

    [TestMethod]
    public void Align_PadsWithZeroes()
    {
        var destination = new byte[8];
        destination.AsSpan().Fill(0xFF);
        var cursor = new WriteCursor(destination);
        cursor.Reserve(1, "a", "uint8")[0] = 1;
        cursor.Align(4, 0, "b", "uint32");
        Assert.AreEqual(4, cursor.Position);
        cursor.Align(4, 0, "b", "uint32");
        cursor.Position = 6;
        CollectionAssert.AreEqual(new byte[] { 1, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF }, destination);
        Assert.Throws<CStructWriteException>(() => new WriteCursor(new byte[2]) { Position = 3, });
    }
}
