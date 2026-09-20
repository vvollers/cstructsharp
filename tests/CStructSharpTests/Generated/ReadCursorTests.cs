namespace CStructSharpTests.Generated;

using System;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>
///     Pins <see cref="ReadCursor"/>: the accounting a generated reader performs and the failures it reports are
///     the runtime reader's - the same message, field, path, and offset - checked against what <see cref="CStruct"/>
///     throws for the same bytes and options.
/// </summary>
[TestClass]
public class ReadCursorTests
{
    /// <summary><c>ReadCursor.Take</c> consumes bytes, charges the total byte budget, and reports a short read with the runtime's text.</summary>
    [TestMethod]
    public void Take_ConsumesChargesAndReportsShortReadsLikeTheRuntime()
    {
        byte[] bytes = [1, 2, 3, 4, 5];
        var cursor = new ReadCursor(bytes, path: "root");
        Assert.AreEqual(5, cursor.Remaining);
        Assert.AreEqual("root", cursor.Path);
        Assert.AreEqual((ushort)0x0201, Codec.ReadUInt16(cursor.Take(2, "a", "uint16"), littleEndian: true));
        Assert.AreEqual(2, cursor.Position);
        Assert.AreEqual(3, cursor.Peek(1, "b", "uint32")[0]);
        Assert.AreEqual(2, cursor.Position);

        CStructReadException error = Assert.Throws<CStructReadException>(() =>
        {
            var failing = new ReadCursor(bytes, path: "root") { Position = 2, };
            try
            {
                failing.Take(4, "b", "uint32");
            }
            catch (CStructException exception)
            {
                // The offset is attached where the exception leaves the operation, as generated Parse methods do.
                failing.Complete(exception);
                throw;
            }
        });

        // The runtime reports the same text, field, path, and offset (the end of the input) for the same bytes.
        var layout = new CStruct("struct root { uint16 a; uint32 b; };");
        CStructReadException runtime = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "root"));
        Assert.AreEqual(runtime.Message, error.Message);
        Assert.AreEqual("Not enough bytes: needed 4, available 3 (field 'b' (uint32), in 'root', offset 5).", error.Message);
        Assert.AreEqual(runtime.Path, error.Path);
        Assert.AreEqual(runtime.Member, error.Member);
        Assert.AreEqual(runtime.MemberType, error.MemberType);
        Assert.AreEqual(runtime.Offset, error.Offset);
    }

    /// <summary>The cursor's array, string, nesting, pointer, and byte limits fail with the runtime's limit exceptions and texts.</summary>
    [TestMethod]
    public void Budgets_MatchTheRuntimeLimits()
    {
        byte[] bytes = new byte[16];
        var options = new ReadOptions { MaxTotalBytesRead = 6, };
        CStructReadLimitException budget = Assert.Throws<CStructReadLimitException>(() =>
        {
            var cursor = new ReadCursor(bytes, options, "root");
            try
            {
                cursor.Take(4, "a", "uint32");
                cursor.Skip(4, "pad", "uint32");
                cursor.Take(4, "b", "uint32");
            }
            catch (CStructException exception)
            {
                cursor.Complete(exception);
                throw;
            }
        });
        var layout = new CStruct("struct root { uint32 a; uint32 pad; uint32 b; };");
        CStructReadLimitException runtime = Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes, "root", options: options));
        Assert.AreEqual(Strip(runtime.Message), Strip(budget.Message));
        Assert.AreEqual("b", budget.Member);
        Assert.AreEqual(8L, budget.Offset);

        var arrayOptions = new ReadOptions { MaxArrayElements = 3, };
        Assert.AreEqual(3, new ReadCursor(bytes, arrayOptions, "root").RequireArrayLength(3, "items", "uint8"));
        CStructReadLimitException tooMany = Assert.Throws<CStructReadLimitException>(() =>
        {
            var cursor = new ReadCursor([4, 0, 0, 0, 0], arrayOptions, "root");
            try
            {
                cursor.Take(1, "count", "uint8");
                cursor.RequireArrayLength(4, "items", "uint8");
            }
            catch (CStructException exception)
            {
                cursor.Complete(exception);
                throw;
            }
        });
        var countLayout = new CStruct("struct root { uint8 count; uint8 items[count]; };");
        CStructReadLimitException runtimeArray = Assert.Throws<CStructReadLimitException>(
            () => countLayout.Parse(new byte[] { 4, 0, 0, 0, 0 }, "root", options: arrayOptions));
        Assert.AreEqual(runtimeArray.Message, tooMany.Message);
        Assert.AreEqual("Array length 4 exceeds MaxArrayElements (3) (field 'items' (uint8), in 'root', offset 1).", tooMany.Message);

        var stringOptions = new ReadOptions { MaxStringBytes = 2, };
        new ReadCursor(bytes, stringOptions).RequireBoundedTextBytes(2, "name", "utf8");
        CStructReadLimitException bounded = Assert.Throws<CStructReadLimitException>(() => new ReadCursor(bytes, stringOptions).RequireBoundedTextBytes(3, "name", "utf8"));
        StringAssert.StartsWith(bounded.Message, "Encoded text buffer exceeds the configured string byte limit");
        CStructReadLimitException terminated = Assert.Throws<CStructReadLimitException>(() => new ReadCursor(bytes, stringOptions).RequireTerminatedStringBytes(3, "name", "cstring"));
        StringAssert.StartsWith(terminated.Message, "String field exceeded the configured encoded-byte limit");
    }

    /// <summary>Composite nesting and pointer entry enforce depth, absolute and relative addressing, and cycle detection like the runtime.</summary>
    [TestMethod]
    public void NestingAndPointers_EnforceDepthAndAddressing()
    {
        byte[] bytes = [0x04, 0xAA, 0, 0, 0x2A];
        CStructReadLimitException depth = Assert.Throws<CStructReadLimitException>(() =>
        {
            var nested = new ReadCursor(bytes, new ReadOptions { MaxNestingDepth = 1, }, "root");
            nested.EnterComposite("root", null);
            nested.ExitComposite();
            nested.EnterComposite("again", "outer");
            nested.EnterComposite("inner", "inner");
        });
        StringAssert.StartsWith(depth.Message, "Maximum nested struct depth exceeded");
        Assert.AreEqual("inner", depth.Member);
        Assert.AreEqual("root", depth.Path);

        var absolute = new ReadCursor(bytes);
        int resume = absolute.EnterPointer(4, 1, 1, "uint8", "ptr", "uint8*");
        Assert.AreEqual(0, resume);
        Assert.AreEqual(4, absolute.Position);
        Assert.AreEqual(0x2A, absolute.Take(1, "ptr", "uint8*")[0]);
        absolute.ExitPointer(resume);
        Assert.AreEqual(0, absolute.Position);

        var relative = new ReadCursor(bytes, new ReadOptions { AddressingMode = PointerAddressingMode.Relative, Origin = 3, });
        relative.EnterPointer(1, 1, 1, "uint8", "ptr", "uint8*");
        Assert.AreEqual(4, relative.Position);

        CStructReadException outside = Assert.Throws<CStructReadException>(() =>
        {
            var cursor = new ReadCursor(bytes);
            cursor.EnterPointer(6, 1, 1, "uint8", "ptr", "uint8*");
        });
        StringAssert.StartsWith(outside.Message, "Pointer target is outside the readable stream range: 6");

        CStructReadLimitException pointerDepth = Assert.Throws<CStructReadLimitException>(() =>
        {
            var cursor = new ReadCursor(bytes, new ReadOptions { MaxPointerDepth = 1, });
            cursor.EnterPointer(1, 2, 1, "uint8", "ptr", "uint8**");
            cursor.EnterPointer(2, 1, 1, "uint8", "ptr", "uint8**");
        });
        StringAssert.StartsWith(pointerDepth.Message, "Maximum pointer dereference depth exceeded");

        var defaults = new ReadCursor(bytes);
        Assert.IsTrue(defaults.DereferencePointers);
        Assert.AreEqual(16 * 1024 * 1024L, defaults.MaxStringBytes);
        Assert.AreEqual(1_000_000, defaults.MaxArrayElements);
        Assert.IsNull(defaults.MaxPointerTargetBytes);
        Assert.IsFalse(defaults.TrimFixedText);
        Assert.AreEqual(PointerAddressingMode.Absolute, defaults.AddressingMode);
        Assert.AreEqual(0L, defaults.Origin);
        Assert.AreEqual(5, defaults.Source.Length);
        Assert.IsNull(defaults.Path);
    }

    /// <summary><c>Align</c> pads from the composite origin, not from the cursor's absolute position.</summary>
    [TestMethod]
    public void Align_PadsFromTheOrigin()
    {
        var cursor = new ReadCursor(new byte[16]);
        cursor.Take(1, "a", "uint8");
        cursor.Align(4, 0, "b", "uint32");
        Assert.AreEqual(4, cursor.Position);
        cursor.Align(4, 0, "b", "uint32");
        Assert.AreEqual(4, cursor.Position);
        cursor.Align(8, 2, "c", "uint64");
        Assert.AreEqual(10, cursor.Position);
        Assert.Throws<CStructReadException>(() =>
        {
            var short1 = new ReadCursor(new byte[3]) { Position = 3, };
            short1.Align(4, 0, "b", "uint32");
        });
        Assert.Throws<CStructReadException>(() => new ReadCursor(new byte[3]) { Position = 4, });
    }

    /// <summary><c>FailExpression</c> wraps an operator failure in the runtime's <c>Cannot evaluate</c> text with the member context.</summary>
    [TestMethod]
    public void FailExpression_WrapsOperatorFailuresLikeTheRuntime()
    {
        // A uint32 count at or above 2^31 is kept exact until the array length selects it.
        var layout = new CStruct("struct root { uint32 count; uint8 items[count]; };");
        byte[] bytes = [0, 0, 0, 0x80, 1];
        CStructReadException runtime = Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "root"));

        var cursor = new ReadCursor(bytes, path: "root");
        uint count = Codec.ReadUInt32(cursor.Take(4, "count", "uint32"), littleEndian: true);
        Exception generated;
        try
        {
            _ = Expressions.RequireInt32(count, "count");
            generated = new InvalidOperationException("not reached");
        }
        catch (Exception exception)
        {
            generated = cursor.FailExpression(exception, "array length for items", "items", "uint8");
            cursor.Complete((CStructException)generated);
        }

        Assert.IsInstanceOfType<CStructReadException>(generated);
        Assert.AreEqual(runtime.Message, generated.Message);
        Assert.AreEqual("Cannot evaluate array length for items: 'count' is 2147483648, which is outside the 32-bit range that layout expressions support (field 'items' (uint8), in 'root', offset 4).", generated.Message);

        // A division by zero and an overflow take the same wording as the runtime evaluator's.
        Exception zero = cursor.FailExpression(new DivideByZeroException("Attempted to divide by zero."), "array length for items", "items", "uint8");
        StringAssert.StartsWith(zero.Message, "Cannot evaluate array length for items: Attempted to divide by zero");
        Exception overflow = cursor.FailExpression(new OverflowException(), "array length for items", "items", "uint8");
        StringAssert.StartsWith(overflow.Message, "Cannot evaluate array length for items: the result is outside the 32-bit range that layout expressions support");

        // Anything that is not an expression failure passes through unchanged.
        var unrelated = new ArgumentNullException("x");
        Assert.AreSame(unrelated, cursor.FailExpression(unrelated, "array length for items", "items", "uint8"));
    }

    /// <summary><c>TakeCustom</c> hands a custom codec the remaining input and charges the budget as the runtime's memory path does.</summary>
    [TestMethod]
    public void TakeCustom_MatchesTheRuntimeMemoryPath()
    {
        // The value, the consumed bytes, and every failure the adapter can raise, against the runtime's texts.
        var layout = new CStruct("struct root { uint8 head; pair value; uint8 tail; };", compilationOptions: new CStructCompilationOptions { Codecs = [PairCodec.Instance], });
        byte[] bytes = [7, 1, 2, 9];
        var cursor = new ReadCursor(bytes, path: "root");
        cursor.Take(1, "head", "uint8");
        Assert.AreEqual(0x0102, cursor.TakeCustom(PairCodec.Instance, "value", "pair"));
        Assert.AreEqual(3, cursor.Position);
        Assert.AreEqual(9, cursor.Take(1, "tail", "uint8")[0]);

        foreach ((byte[] input, ReadOptions? options) in new (byte[], ReadOptions?)[]
                 {
                     ([7, 1], null),
                     ([7, PairCodec.Rejects, 0, 9], null),
                     ([7, PairCodec.Throws, 0, 9], null),
                     ([7, PairCodec.OverConsumes, 0, 9], null),
                     (bytes, new ReadOptions { MaxTotalBytesRead = 2, }),
                 })
        {
            CStructException runtime = Assert.Throws<CStructException>(() => layout.Parse(input, "root", options: options));
            CStructException generated = Assert.Throws<CStructException>(
                () =>
                {
                    var failing = new ReadCursor(input, options, "root");
                    try
                    {
                        failing.Take(1, "head", "uint8");
                        failing.TakeCustom(PairCodec.Instance, "value", "pair");
                    }
                    catch (CStructException exception)
                    {
                        failing.Complete(exception);
                        throw;
                    }
                });
            Assert.AreEqual(runtime.GetType(), generated.GetType());
            Assert.AreEqual(runtime.Message, generated.Message);
        }
    }

    private static string Strip(string message) => message[..message.IndexOf(" (", StringComparison.Ordinal)];

    /// <summary>Two bytes as a big-endian pair; the first byte selects a misbehaviour for the failure texts.</summary>
    private sealed class PairCodec : CStructSharp.Codecs.ICustomCodec
    {
        public const byte Rejects = 0xF0;
        public const byte Throws = 0xF1;
        public const byte OverConsumes = 0xF2;

        public static readonly PairCodec Instance = new();

        public string Name => "pair";

        public int? FixedSize => 2;

        public int Alignment => 1;

        public System.Buffers.OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            value = null;
            bytesConsumed = 0;
            if (source.Length < 2)
            {
                return System.Buffers.OperationStatus.NeedMoreData;
            }

            switch (source[0])
            {
            case Rejects:
                return System.Buffers.OperationStatus.InvalidData;
            case Throws:
                throw new InvalidOperationException("boom");
            case OverConsumes:
                value = 0;
                bytesConsumed = source.Length + 1;
                return System.Buffers.OperationStatus.Done;
            default:
                value = (source[0] << 8) | source[1];
                bytesConsumed = 2;
                return System.Buffers.OperationStatus.Done;
            }
        }

        public System.Buffers.OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            bytesWritten = 0;
            if (destination.Length < 2)
            {
                return System.Buffers.OperationStatus.DestinationTooSmall;
            }

            int packed = Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture);
            destination[0] = (byte)(packed >> 8);
            destination[1] = (byte)packed;
            bytesWritten = 2;
            return System.Buffers.OperationStatus.Done;
        }
    }
}
