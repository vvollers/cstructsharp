namespace CStructSharpTests.Generated;

using System;
using System.Collections.Generic;
using System.IO;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Values;

/// <summary>
///     Pins <see cref="Codec"/>, the one implementation of every byte-level rule: each function agrees with the
///     runtime stream codec that now calls it (same bytes, same values, same failure texts) in both byte orders.
/// </summary>
[TestClass]
public class CodecTests
{
    /// <summary>Every fixed-width integer and floating codec of <c>Codec</c> decodes and encodes in both byte orders to the runtime's bytes.</summary>
    [TestMethod]
    public void FixedWidthIntegers_RoundTripInBothByteOrders()
    {
        Span<byte> buffer = stackalloc byte[16];
        foreach (bool le in new[] { true, false })
        {
            Codec.WriteInt16(buffer, -2, le);
            Assert.AreEqual((short)-2, Codec.ReadInt16(buffer, le));
            Assert.AreEqual(le ? 0xFE : 0xFF, buffer[0]);
            Codec.WriteUInt16(buffer, 0x1234, le);
            Assert.AreEqual((ushort)0x1234, Codec.ReadUInt16(buffer, le));
            Codec.WriteInt32(buffer, -0x12345678, le);
            Assert.AreEqual(-0x12345678, Codec.ReadInt32(buffer, le));
            Codec.WriteUInt32(buffer, 0xFEDCBA98u, le);
            Assert.AreEqual(0xFEDCBA98u, Codec.ReadUInt32(buffer, le));
            Codec.WriteInt64(buffer, long.MinValue + 5, le);
            Assert.AreEqual(long.MinValue + 5, Codec.ReadInt64(buffer, le));
            Codec.WriteUInt64(buffer, ulong.MaxValue - 1, le);
            Assert.AreEqual(ulong.MaxValue - 1, Codec.ReadUInt64(buffer, le));
            Codec.WriteInt128(buffer, Int128.MinValue + 7, le);
            Assert.AreEqual(Int128.MinValue + 7, Codec.ReadInt128(buffer, le));
            Codec.WriteUInt128(buffer, UInt128.MaxValue - 9, le);
            Assert.AreEqual(UInt128.MaxValue - 9, Codec.ReadUInt128(buffer, le));
            Codec.WriteHalf(buffer, (Half)1.5, le);
            Assert.AreEqual((Half)1.5, Codec.ReadHalf(buffer, le));
            Codec.WriteSingle(buffer, -2.5f, le);
            Assert.AreEqual(-2.5f, Codec.ReadSingle(buffer, le));
            Codec.WriteDouble(buffer, Math.PI, le);
            Assert.AreEqual(Math.PI, Codec.ReadDouble(buffer, le));
            Codec.WriteChar(buffer, 'é', le);
            Assert.AreEqual('é', Codec.ReadChar(buffer, le));
        }
    }

    /// <summary>The 24- and 48-bit codecs sign-extend on read and reject a value outside their range on write with the runtime's text.</summary>
    [TestMethod]
    public void NarrowIntegers_SignExtendAndRejectOutOfRangeWrites()
    {
        Span<byte> buffer = stackalloc byte[6];
        Codec.WriteInt24(buffer, -8388608, true);
        Assert.AreEqual(-8388608, Codec.ReadInt24(buffer, true));
        Codec.WriteInt24(buffer, -1, false);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF }, buffer[..3].ToArray());
        Codec.WriteUInt24(buffer, 0xABCDEF, false);
        CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD, 0xEF }, buffer[..3].ToArray());
        Assert.AreEqual(0xABCDEFu, Codec.ReadUInt24(buffer, false));
        Assert.AreEqual(0xEFCDABu, Codec.ReadUInt24(buffer, true));

        Codec.WriteInt48(buffer, -140_737_488_355_328, true);
        Assert.AreEqual(-140_737_488_355_328, Codec.ReadInt48(buffer, true));
        Codec.WriteUInt48(buffer, 0xFFFF_FFFF_FFFF, false);
        Assert.AreEqual(0xFFFF_FFFF_FFFFul, Codec.ReadUInt48(buffer, false));
        Codec.WriteUInt48(buffer, 0x0102_0304_0506, false);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6 }, buffer.ToArray());
        Assert.AreEqual(0x0605_0403_0201ul, Codec.ReadUInt48(buffer, true));

        AssertWriteFails(() => Codec.WriteInt24(new byte[3], 8388608, true), "Value is outside the int24 range.");
        AssertWriteFails(() => Codec.WriteInt24(new byte[3], -8388609, true), "Value is outside the int24 range.");
        AssertWriteFails(() => Codec.WriteUInt24(new byte[3], 0x1000000, true), "Value is outside the uint24 range.");
        AssertWriteFails(() => Codec.WriteInt48(new byte[6], 140_737_488_355_328, true), "Value is outside the int48 range.");
        AssertWriteFails(() => Codec.WriteInt48(new byte[6], -140_737_488_355_329, true), "Value is outside the int48 range.");
        AssertWriteFails(() => Codec.WriteUInt48(new byte[6], 0x1_0000_0000_0000, true), "Value is outside the uint48 range.");
    }

    /// <summary><c>ReadUnsigned</c>/<c>WriteUnsigned</c> handle every unit width from one to eight bytes in both orders.</summary>
    [TestMethod]
    public void Unsigned_CoversEveryWidthUpToEightBytes()
    {
        for (int size = 1; size <= 8; size++)
        {
            foreach (bool le in new[] { true, false })
            {
                ulong value = size == 8 ? 0x0102030405060708ul : (1ul << (size * 8)) - 3;
                byte[] bytes = new byte[size];
                Codec.WriteUnsigned(bytes, value, le);
                Assert.AreEqual(value, Codec.ReadUnsigned(bytes, le), $"size {size} le {le}");
                if (size == 3)
                {
                    CollectionAssert.AreEqual(le ? new byte[] { 0xFD, 0xFF, 0xFF } : new byte[] { 0xFF, 0xFF, 0xFD }, bytes);
                }
            }
        }

        Assert.Throws<InvalidOperationException>(() => Codec.ReadUnsigned(new byte[9], true));
        Assert.Throws<InvalidOperationException>(() => Codec.WriteUnsigned(new byte[0], 1, true));
    }

    /// <summary>The span LEB128 decoder agrees with the runtime's stream decoder on values, widths, and failures.</summary>
    [TestMethod]
    public void Leb128_SpanAndStreamDecodersAgree()
    {
        Span<byte> buffer = stackalloc byte[10];
        foreach ((int width, bool signed, long value) in new (int, bool, long)[]
                 {
                     (32, false, 0), (32, false, 127), (32, false, 128), (32, false, uint.MaxValue),
                     (32, true, -1), (32, true, -64), (32, true, -65), (32, true, int.MinValue), (32, true, int.MaxValue),
                     (64, false, long.MaxValue), (64, true, long.MinValue), (64, true, -1), (64, false, 300),
                 })
        {
            int written = signed
                              ? Codec.WriteSLeb128(buffer, value)
                              : Codec.WriteULeb128(buffer, unchecked((ulong)value));
            ulong decoded = Codec.ReadLeb128(buffer[..written], width, signed, out int consumed);
            Assert.AreEqual(written, consumed);
            Assert.AreEqual(unchecked((ulong)value), decoded, $"{width}/{signed}/{value}");
            using var stream = new MemoryStream(buffer[..written].ToArray());
            Assert.AreEqual(decoded, Leb128Codec.Read(stream, width, signed));
            Assert.AreEqual(written, stream.Position);
        }

        // The width check and the terminator rule are the same rule on both paths.
        byte[] tooWide = [0xFF, 0xFF, 0xFF, 0xFF, 0x1F];
        CStructReadException spanError = Assert.Throws<CStructReadException>(() => Codec.ReadLeb128(tooWide, 32, false, out _));
        CStructReadException streamError = Assert.Throws<CStructReadException>(() => Leb128Codec.Read(new MemoryStream(tooWide), 32, false));
        Assert.AreEqual(streamError.Message, spanError.Message);
        StringAssert.Contains(spanError.Message, "exceeds its declared width");

        byte[] unterminated = [0x80, 0x80];
        CStructReadException shortSpan = Assert.Throws<CStructReadException>(() => Codec.ReadLeb128(unterminated, 32, false, out _));
        CStructReadException shortStream = Assert.Throws<CStructReadException>(() => Leb128Codec.Read(new MemoryStream(unterminated), 32, false));
        Assert.AreEqual(shortStream.Message, shortSpan.Message);
        Assert.AreEqual("Not enough bytes: needed 1, available 0.", shortSpan.Message);
    }

    /// <summary>Fixed-point decoding and encoding follow the runtime's grid: exact values pass, unrepresentable ones are rejected.</summary>
    [TestMethod]
    public void FixedPoint_DecodesAndValidatesTheGrid()
    {
        Assert.AreEqual(1.5, Codec.DecodeFixedPoint(0x18000, 16));
        Assert.AreEqual(-0.5, Codec.DecodeFixedPoint(-0x8000, 16));
        Assert.AreEqual(0x18000L, Codec.EncodeFixedPoint(1.5, 32, 16, true));
        Assert.AreEqual(0x18000L, Codec.EncodeFixedPoint(1.5m, 32, 16, true));
        Assert.AreEqual(-0x8000L, Codec.EncodeFixedPoint(-0.5, 32, 16, true));
        Assert.AreEqual(0xFFFFL, Codec.EncodeFixedPoint(255.99609375, 16, 8, false));
        const string message = "Fixed-point value is outside the exact storage grid or range.";
        AssertWriteFails(() => Codec.EncodeFixedPoint(1.0000001m, 32, 16, true), message);
        AssertWriteFails(() => Codec.EncodeFixedPoint(-1, 16, 8, false), message);
        AssertWriteFails(() => Codec.EncodeFixedPoint(32768.0, 32, 16, true), message);
        AssertWriteFails(() => Codec.EncodeFixedPoint(double.NaN, 32, 16, true), message);
        Assert.Throws<ArgumentNullException>(() => Codec.EncodeFixedPoint(null!, 32, 16, true));
    }

    /// <summary>UUID and GUID codecs keep the network and Windows byte orders the runtime uses.</summary>
    [TestMethod]
    public void Identifiers_HonourNetworkAndWindowsOrder()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        Span<byte> buffer = stackalloc byte[16];
        Codec.WriteGuid(buffer, id, networkOrder: true);
        Assert.AreEqual(0x00, buffer[0]);
        Assert.AreEqual(0x11, buffer[1]);
        Assert.AreEqual(id, Codec.ReadGuid(buffer, networkOrder: true));
        Codec.WriteGuid(buffer, id, networkOrder: false);
        Assert.AreEqual(0x33, buffer[0]);
        Assert.AreEqual(id, Codec.ReadGuid(buffer, networkOrder: false));
        Assert.AreEqual(id, Codec.ToGuid(id));
        Assert.AreEqual(id, Codec.ToGuid("00112233-4455-6677-8899-aabbccddeeff"));
        AssertWriteFails(() => Codec.ToGuid("{00112233-4455-6677-8899-aabbccddeeff}"), "Identifier requires a Guid");
        AssertWriteFails(() => Codec.ToGuid(42), "Identifier requires a Guid");
    }

    /// <summary>Bitfield extraction, merging, and shift computation agree with the runtime's bitfield table for both allocations.</summary>
    [TestMethod]
    public void Bitfields_ExtractMergeAndShiftLikeTheRuntimeTable()
    {
        Assert.AreEqual(5ul, Codec.ExtractBits(0xA5, 0, 4));
        Assert.AreEqual(0xAul, Codec.ExtractBits(0xA5, 4, 4));
        Assert.AreEqual(ulong.MaxValue, Codec.ExtractBits(ulong.MaxValue, 0, 64));
        Assert.AreEqual(0x35ul, Codec.MergeBits(0xA5, 3, 4, 4));
        Assert.AreEqual(BitfieldCodecTable.MergeBitfieldValue(0xA5, 3, 4, 4), Codec.MergeBits(0xA5, 3, 4, 4));
        Assert.AreEqual(4, Codec.BitfieldShift(4, 4, 8, highBitFirst: false));
        Assert.AreEqual(0, Codec.BitfieldShift(4, 4, 8, highBitFirst: true));
        Assert.AreEqual(BitfieldCodecTable.EffectiveShift(1, 3, 16, true), Codec.BitfieldShift(1, 3, 16, true));
        Assert.AreEqual(7ul, Codec.ToBitfieldValue("flags", 3, 7));
        AssertWriteFails(() => Codec.ToBitfieldValue("flags", 3, 8), "exceeds the unsigned 3-bit range");
        AssertWriteFails(() => Codec.ToBitfieldValue("flags", 3, 1.5), "must be an unsigned integer that fits 3 bits");
        AssertWriteFails(() => Codec.ToBitfieldValue("flags", 3, null), "cannot be null");
    }

    /// <summary>Bulk primitive array decoding and encoding cover every element width in both byte orders.</summary>
    [TestMethod]
    public void PrimitiveArrays_DecodeAndEncodeEveryWidthInBothOrders()
    {
        byte[] source = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        var shorts = new short[8];
        Codec.DecodeIntegers<short>(source, shorts, littleEndian: true);
        Assert.AreEqual((short)0x0201, shorts[0]);
        Codec.DecodeIntegers<short>(source, shorts, littleEndian: false);
        Assert.AreEqual((short)0x0102, shorts[0]);
        var uints = new uint[4];
        Codec.DecodeIntegers<uint>(source, uints, littleEndian: false);
        Assert.AreEqual(0x01020304u, uints[0]);
        var doubles = new double[2];
        Codec.DecodeIntegers<double>(source, doubles, littleEndian: true);
        Assert.AreEqual(BitConverter.Int64BitsToDouble(0x0807060504030201), doubles[0]);
        var floats = new float[4];
        Codec.DecodeIntegers<float>(source, floats, littleEndian: false);
        Assert.AreEqual(BitConverter.Int32BitsToSingle(0x01020304), floats[0]);
        var bytes = new byte[16];
        Codec.DecodeIntegers<byte>(source, bytes, littleEndian: false);
        CollectionAssert.AreEqual(source, bytes);

        var encoded = new byte[8];
        Codec.EncodeIntegers<ushort>(new ushort[] { 0x0102, 0x0304, 0x0506, 0x0708 }, encoded, littleEndian: false);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, encoded);
        Codec.EncodeIntegers<ulong>(new ulong[] { 0x0102030405060708 }, encoded, littleEndian: true);
        CollectionAssert.AreEqual(new byte[] { 8, 7, 6, 5, 4, 3, 2, 1 }, encoded);
        Codec.EncodeIntegers<uint>(new uint[] { 0x01020304, 0x05060708 }, encoded, littleEndian: false);
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, encoded);

        var flags = new bool[3];
        Codec.DecodeBooleans([0, 2, 255], flags);
        CollectionAssert.AreEqual(new[] { false, true, true }, flags);
        var int24 = new int[2];
        Codec.DecodeInt24([0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x80], int24, littleEndian: true);
        CollectionAssert.AreEqual(new[] { -1, -8388608 }, int24);
        var uint24 = new uint[2];
        Codec.DecodeUInt24([0xFF, 0xFF, 0xFF, 0x00, 0x00, 0x80], uint24, littleEndian: false);
        CollectionAssert.AreEqual(new[] { 0xFFFFFFu, 0x80u }, uint24);
    }

    /// <summary>Fixed and bounded text decode as the runtime does, and the terminator search honours unit size and alignment.</summary>
    [TestMethod]
    public void Text_DecodesFixedAndBoundedBuffersAndFindsTerminators()
    {
        Assert.AreEqual("ab\0c\0\0", Codec.DecodeFixedText("ab\0c\0\0"u8, trimTrailingNuls: false));
        Assert.AreEqual("ab\0c", Codec.DecodeFixedText("ab\0c\0\0"u8, trimTrailingNuls: true));
        Assert.AreEqual("é", Codec.DecodeBoundedText([0xC3, 0xA9, 0, 0], "utf8", trimTrailingNuls: true));
        Assert.AreEqual("é\0\0", Codec.DecodeBoundedText([0xC3, 0xA9, 0, 0], "utf8", trimTrailingNuls: false));
        Assert.AreEqual("é", Codec.DecodeBoundedText([0x82], "cp437", trimTrailingNuls: false));
        CStructReadException invalid = Assert.Throws<CStructReadException>(() => Codec.DecodeBoundedText([0xC3], "utf8", false));
        Assert.AreEqual("Encoded text buffer contains an invalid byte sequence.", invalid.Message);

        Assert.AreEqual(2, Codec.FindTerminator("ab\0c"u8, "\0"u8, 1, 0));
        Assert.AreEqual(-1, Codec.FindTerminator("abc"u8, "\0"u8, 1, 0));
        byte[] wide = [(byte)'a', 0, 0, 0, (byte)'b', 0];
        Assert.AreEqual(2, Codec.FindTerminator(wide, [0, 0], 2, 0));
        Assert.AreEqual(-1, Codec.FindTerminator([0x41, 0x00, 0x00, 0x42], [0, 0], 2, 0), "a terminator straddling units is not one");
        Assert.AreEqual(1, Codec.FindTerminator([0x41, 0x00, 0x00, 0x42], [0, 0], 2, 1));

        Assert.AreEqual((byte)'A', Codec.ToNarrowCharacter('A'));
        AssertWriteFails(() => Codec.ToNarrowCharacter('€'), "does not fit the one-byte char type");
    }

    /// <summary>The runtime's stream codecs and <c>Codec</c>'s span functions produce the same bytes for the same values.</summary>
    [TestMethod]
    public void RuntimeStreamCodecs_ProduceTheSameBytesAsCodec()
    {
        var layout = new CStruct("struct root { int24 a; uint48> b; fixed16_16 c; uuid d; sleb128_64 e; float16 f; };");
        var value = new Dictionary<string, object?>
        {
            ["a"] = -2,
            ["b"] = 0x0102_0304_0506ul,
            ["c"] = -1.25,
            ["d"] = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"),
            ["e"] = -300L,
            ["f"] = (Half)0.5,
        };
        byte[] bytes = layout.Serialize("root", value);
        Span<byte> expected = stackalloc byte[3 + 6 + 4 + 16 + 10 + 2];
        int offset = 0;
        Codec.WriteInt24(expected[offset..], -2, true);
        offset += 3;
        Codec.WriteUInt48(expected[offset..], 0x0102_0304_0506ul, false);
        offset += 6;
        Codec.WriteInt32(expected[offset..], (int)Codec.EncodeFixedPoint(-1.25, 32, 16, true), true);
        offset += 4;
        Codec.WriteGuid(expected[offset..], Guid.Parse("00112233-4455-6677-8899-aabbccddeeff"), networkOrder: true);
        offset += 16;
        offset += Codec.WriteSLeb128(expected[offset..], -300);
        Codec.WriteHalf(expected[offset..], (Half)0.5, true);
        offset += 2;
        CollectionAssert.AreEqual(expected[..offset].ToArray(), bytes);

        StructValue parsed = layout.Parse(bytes, "root");
        Assert.AreEqual(-2, parsed["a"]);
        Assert.AreEqual(0x0102_0304_0506ul, parsed["b"]);
        Assert.AreEqual(-1.25, parsed["c"]);
        Assert.AreEqual(-300L, parsed["e"]);
        Assert.AreEqual((Half)0.5, parsed["f"]);
    }

    private static void AssertWriteFails(Action action, string expectedMessage)
    {
        CStructWriteException error = Assert.Throws<CStructWriteException>(action);
        StringAssert.Contains(error.Message, expectedMessage, StringComparison.Ordinal);
    }
}
