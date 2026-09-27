namespace CStructSharp.Tests.Generated;

using System;
using System.Collections.Generic;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Values;

/// <summary>Pins <see cref="WriteCursor"/> against the runtime writer's accounting, failure texts, and context.</summary>
[TestClass]
public class WriteCursorTests
{
    /// <summary><c>WriteCursor.Reserve</c> advances, pads, and reports a too-small destination with the runtime's capacity text.</summary>
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
            try
            {
                small.Reserve(2, "a", "uint16");
                small.Reserve(2, "b", "uint16");
            }
            catch (CStructException exception)
            {
                // The offset is attached where the exception leaves the operation, as generated Serialize methods do.
                small.Complete(exception);
                throw;
            }
        });
        var layout = new CStruct("struct root { uint16 a; uint16 b; };");
        CStructWriteException runtime = Assert.Throws<CStructWriteException>(
            () => layout.Serialize(new byte[3].AsSpan(), "root", new Dictionary<string, object?> { ["a"] = 1, ["b"] = 2, }));
        Assert.AreEqual(runtime.Message, capacity.Message);
        Assert.AreEqual("The serialized value exceeds the supplied destination capacity (field 'b' (uint16), in 'root', offset 2).", capacity.Message);
    }

    /// <summary>The write cursor's array and string limits fail with the runtime's limit texts.</summary>
    [TestMethod]
    public void Limits_MatchTheRuntimeTexts()
    {
        CStructWriteLimitException budget = Assert.Throws<CStructWriteLimitException>(() =>
        {
            var cursor = new WriteCursor(new byte[16], new WriteOptions { MaxTotalBytesWritten = 5, }, "root");
            try
            {
                cursor.Reserve(4, "a", "uint32");
                cursor.Reserve(4, "b", "uint32");
            }
            catch (CStructException exception)
            {
                cursor.Complete(exception);
                throw;
            }
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
            try
            {
                failing.Reserve(1, "count", "uint8");
                failing.RequireArrayLength(3, "items", "uint8");
            }
            catch (CStructException exception)
            {
                failing.Complete(exception);
                throw;
            }
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

    /// <summary><c>FailExpression</c> wraps an operator failure in the runtime's <c>Cannot evaluate</c> text and passes other failures through.</summary>
    [TestMethod]
    public void FailExpression_WrapsOperatorFailuresLikeTheRuntime()
    {
        var cursor = new WriteCursor(new byte[3], path: "root");
        Exception expression = cursor.FailExpression(new DivideByZeroException("Attempted to divide by zero."), "array length for items", "items", "uint8");
        Assert.IsInstanceOfType<CStructWriteException>(expression);
        StringAssert.StartsWith(expression.Message, "Cannot evaluate array length for items: Attempted to divide by zero");
        var unrelated = new ArgumentNullException("x");
        Assert.AreSame(unrelated, cursor.FailExpression(unrelated, "array length for items", "items", "uint8"));
    }

    /// <summary>The inclusive boundaries of <c>Reserve</c> and the string limit, the option validation of the constructor, and the member and cause a failure carries.</summary>
    [TestMethod]
    public void Boundaries_AreInclusiveAndFailuresCarryTheirCauseAndMember()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteCursor(new byte[4], new WriteOptions { MaxArrayElements = -1, }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WriteCursor(new WriteOptions { MaxNestingDepth = 0, }, "root"));

        var cursor = new WriteCursor(new byte[4], new WriteOptions { MaxTotalBytesWritten = 4, MaxStringBytes = 3, }, "root");
        Assert.AreEqual(0, cursor.Reserve(0, "a", "uint8").Length, "a zero-length reservation is allowed");
        Assert.AreEqual(4, cursor.Reserve(4, "a", "uint8").Length, "reaching the byte budget exactly is allowed");
        Assert.AreEqual(4, cursor.Length);
        cursor.Position = 2;
        cursor.Reserve(1, "a", "uint8");
        Assert.AreEqual(4, cursor.Length, "writing inside the written extent does not shorten it");
        Assert.Throws<CStructWriteLimitException>(() =>
        {
            var over = new WriteCursor(new byte[8], new WriteOptions { MaxTotalBytesWritten = 4, });
            over.Reserve(4, "a", "uint8");
            over.Reserve(1, "b", "uint8");
        });
        Assert.Throws<CStructWriteLimitException>(() => new WriteCursor(new byte[8]).Reserve(-1, "a", "uint8"));
        cursor.RequireStringBytes(0, "s", "cstring");
        cursor.RequireStringBytes(3, "s", "cstring");
        Assert.Throws<CStructWriteLimitException>(() => new WriteCursor(new byte[4], new WriteOptions { MaxStringBytes = 3, }).RequireStringBytes(4, "s", "cstring"));
        Assert.Throws<CStructWriteLimitException>(() => new WriteCursor(new byte[4]).RequireStringBytes(-1, "s", "cstring"));

        var arithmetic = new DivideByZeroException();
        var expression = (CStructWriteException)cursor.FailExpression(arithmetic, "array length for items", "items", "uint8");
        Assert.AreEqual("items", expression.Member);
        Assert.AreEqual("root", expression.Path);
        Assert.AreSame(arithmetic, expression.InnerException);
        var unrelated = new InvalidCastException();
        Assert.AreSame(unrelated, cursor.FailExpression(unrelated, "array length", "items", "uint8"), "a non-expression failure passes through untouched");
        Assert.Throws<ArgumentNullException>(() => new WriteCursor(new byte[4]).FailExpression(null!, "array length", "m", "uint8"));
        Assert.Throws<ArgumentNullException>(() => new WriteCursor(new byte[4]).Complete(null!));
    }

    /// <summary>A growable cursor collects its bytes into an array, and its text helpers produce the runtime writer's bytes and failures.</summary>
    [TestMethod]
    public void GrowableCursor_CollectsTheBytesAndTextHelpersMatchTheRuntime()
    {
        var layout = new CStruct("struct root { uint8 a; uint16 b; char name[4]; cstring tail; utf8 label[3]; uint8 bits:3; uint8 more:5; };");
        var value = new StructValue { ["a"] = 1, ["b"] = 0x0203, ["name"] = "ab", ["tail"] = "hi", ["label"] = "\u00e9", ["bits"] = 5, ["more"] = 2, };
        byte[] expected = layout.Serialize("root", value);

        var cursor = new WriteCursor(null, "root");
        try
        {
            cursor.Reserve(1, "a", "uint8")[0] = 1;
            Codec.WriteUInt16(cursor.Reserve(2, "b", "uint16"), 0x0203, littleEndian: true);
            cursor.WriteFixedText(4, "ab", wide: false, littleEndian: true, "name", "char");
            cursor.WriteTerminatedString(TerminatedTextEncoding.Ascii, '\0', "hi", "tail", "cstring");
            cursor.WriteBoundedText(3, "utf8", "\u00e9", "label", "utf8");
            int unitStart = cursor.Position;
            cursor.WriteBits(new BitfieldSlot(unitStart, 1, 0), 3, 5, littleEndian: true, highBitFirst: false, "bits", "uint8");
            cursor.WriteBits(new BitfieldSlot(unitStart, 1, 3), 5, 2, littleEndian: true, highBitFirst: false, "more", "uint8");
            Assert.IsTrue(cursor.IsGrowable);
            CollectionAssert.AreEqual(expected, cursor.ToArray());
        }
        finally
        {
            cursor.Dispose();
        }

        // Each helper's failure text is the runtime's.
        foreach ((string member, object bad, string expectedText) in new (string, object, string)[]
                 {
                     ("name", "abcde", "String is too long for name: 5 > 4"),
                     ("name", "\u0100", "Character value U+0100 does not fit the one-byte char type"),
                     ("tail", "a\0b", "String value contains its encoded terminator"),
                     ("label", "\u00e9\u00e9", "Encoded string is too long for label: 4 encoded bytes > 3"),
                     ("bits", 8, "Bitfield value for 'bits' exceeds the unsigned 3-bit range"),
                 })
        {
            var broken = new StructValue { ["a"] = 1, ["b"] = 2, ["name"] = "ab", ["tail"] = "hi", ["label"] = "x", ["bits"] = 1, ["more"] = 2, };
            broken[member] = bad;
            CStructWriteException runtime = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", broken));
            StringAssert.StartsWith(runtime.Message, expectedText);
            CStructWriteException generated = Assert.Throws<CStructWriteException>(() =>
            {
                var failing = new WriteCursor(null, "root");
                try
                {
                    switch (member)
                    {
                    case "name":
                        failing.WriteFixedText(4, (string)bad, wide: false, littleEndian: true, "name", "char");
                        break;
                    case "tail":
                        failing.WriteTerminatedString(TerminatedTextEncoding.Ascii, '\0', (string)bad, "tail", "cstring");
                        break;
                    case "label":
                        failing.WriteBoundedText(3, "utf8", (string)bad, "label", "utf8");
                        break;
                    default:
                        failing.WriteBits(new BitfieldSlot(0, 1, 0), 3, bad, littleEndian: true, highBitFirst: false, "bits", "uint8");
                        break;
                    }
                }
                finally
                {
                    failing.Dispose();
                }
            });
            StringAssert.StartsWith(generated.Message, expectedText);
            Assert.AreEqual(member, generated.Member);
        }
    }
}
