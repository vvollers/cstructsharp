namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>The primitives added for header parity: six- and sixteen-byte integers, binary16 floats, <c>void *</c>, and function pointers.</summary>
[TestClass]
public class WidePrimitiveTests
{
    /// <summary><c>int48</c>/<c>uint48</c> read six bytes in either order, sign-extend, and reject out-of-range writes.</summary>
    [TestMethod]
    public void Int48_ReadsAndWritesSixBytes()
    {
        var layout = new CStruct("struct root { int48 a; uint48 b; uint48> c; };");
        Assert.AreEqual(18, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(1, layout.GetStructAlignmentInBytes("root"));

        byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3, 4, 5, 6, 0, 0, 0, 0, 0, 7,];
        dynamic value = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual(-1L, (long)value.a);
        Assert.AreEqual(0x060504030201UL, (ulong)value.b);
        Assert.AreEqual(7UL, (ulong)value.c);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", value));

        Assert.Throws<CStructWriteException>(() => layout.Serialize("root", new Dictionary<string, object?> { ["a"] = 1L << 47, ["b"] = 0UL, ["c"] = 0UL, }));
        Assert.Throws<CStructWriteException>(() => layout.Serialize("root", new Dictionary<string, object?> { ["a"] = 0L, ["b"] = 1UL << 48, ["c"] = 0UL, }));
    }

    /// <summary><c>int128</c>/<c>uint128</c> (and their <c>OWORD</c>/<c>__int128</c> spellings) read into <see cref="Int128"/>/<see cref="UInt128"/>.</summary>
    [TestMethod]
    public void Int128_ReadsAndWritesSixteenBytes()
    {
        var layout = new CStruct("struct root { int128 a; OWORD b; unsigned __int128 c; };");
        Assert.AreEqual(48, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(16, new CStruct("struct root { uint8 pad; uint128 a; };", aligned: true).GetStructAlignmentInBytes("root"));

        byte[] bytes = new byte[48];
        Array.Fill(bytes, (byte)0xFF, 0, 16);
        bytes[16] = 1;
        bytes[47] = 0x80;
        dynamic value = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual((Int128)(-1), (Int128)value.a);
        Assert.AreEqual((UInt128)1, (UInt128)value.b);
        Assert.AreEqual(UInt128.One << 127, (UInt128)value.c);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", value));

        byte[] written = layout.Serialize("root", new Dictionary<string, object?> { ["a"] = new BigInteger(-2), ["b"] = "3", ["c"] = 4, });
        Assert.AreEqual(0xFE, written[0]);
        Assert.AreEqual(3, written[16]);
        Assert.AreEqual(4, written[32]);
        Assert.Throws<CStructWriteException>(() => layout.Serialize("root", new Dictionary<string, object?> { ["a"] = 0, ["b"] = -1, ["c"] = 0, }));
    }

    /// <summary><c>float16</c> is a bit-exact binary16 read into <see cref="Half"/>.</summary>
    [TestMethod]
    public void Float16_IsBitExact()
    {
        var layout = new CStruct("struct root { float16 a; float16> b; };");
        Assert.AreEqual(4, layout.GetStructSizeInBytes("root"));
        dynamic value = layout.Parse(new byte[] { 0x00, 0x3C, 0x3C, 0x00, }.AsSpan(), "root");
        Assert.AreEqual((Half)1.0, (Half)value.a);
        Assert.AreEqual((Half)1.0, (Half)value.b);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x40, 0x3C, 0x00, }, layout.Serialize("root", new Dictionary<string, object?> { ["a"] = 2.0, ["b"] = (Half)1.0, }));
    }

    /// <summary>The wide primitives take part in debug, address, update, and typed-read operations.</summary>
    [TestMethod]
    public void WidePrimitives_RoundTripThroughEveryOperation()
    {
        var layout = new CStruct("struct root { uint48 a; float16 b; int128 c; };");
        byte[] bytes = new byte[24];
        bytes[0] = 1;
        bytes[7] = 0x3C;
        bytes[8] = 2;
        using var stream = new MemoryStream((byte[])bytes.Clone());
        (List<DebugData> debug, dynamic _) = layout.ParseStreamWithDebug(stream, "root");
        stream.Position = 0;
        Assert.IsTrue(debug.Any(item => item.Path == "root.c" && item.Start == 8 && item.End == 24));
        Assert.AreEqual(6, layout.ResolveAddress(stream, "root.b"));
        layout.UpdateStream(stream, "root.a", 0x0102UL);
        Assert.AreEqual(0x0102UL, layout.ReadValue<ulong>(stream.ToArray().AsSpan(), "root.a"));
        layout.UpdateStream(stream, "root.c", (Int128)(-1));
        Assert.AreEqual((Int128)(-1), layout.ReadValue<Int128>(stream.ToArray().AsSpan(), "root.c"));
        Assert.AreEqual((Half)1.0, layout.ReadValue<Half>(stream.ToArray().AsSpan(), "root.b"));
    }

    /// <summary><c>void *</c> and function pointers store an opaque address of pointer width and never dereference.</summary>
    [TestMethod]
    public void VoidAndFunctionPointers_AreOpaqueAddresses()
    {
        var layout = new CStruct("struct root { void *data; uint8 (*callback)(uint8, void *); PVOID p; };", pointerSize: 4);
        Assert.AreEqual(12, layout.GetStructSizeInBytes("root"));

        dynamic value = layout.Parse(new byte[] { 4, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, }.AsSpan(), "root");
        var data = (Pointer)value.data;
        Assert.AreEqual(4, data.Address);
        Assert.IsFalse(data.IsDereferenced);
        Assert.IsNull(data.Value);
        Assert.AreEqual(8, ((Pointer)value.callback).Address);
        Assert.IsTrue(((Pointer)value.p).IsNull);
        CollectionAssert.AreEqual(new byte[] { 4, 0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, }, layout.Serialize("root", value));

        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { void v; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { void v[2]; };"));
        Assert.AreEqual(8, new CStruct("struct root { void **pp; };").GetStructSizeInBytes("root"));
    }
}
