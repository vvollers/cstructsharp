namespace CStructSharp.Tests;

using System.Buffers.Binary;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Layout expressions evaluate in the signed 128-bit domain, so the 64-bit values of memory dumps and disk images
///     (kernel addresses above 2^63, <c>uint64</c> sizes and tags) take part in conditions, counts and switches with
///     their exact values through every operation.
/// </summary>
[TestClass]
public class WideExpressionDomainTests
{
    /// <summary>A kernel-style address above 2^63: as a signed 64-bit integer it would be negative.</summary>
    private const ulong KernelAddress = 0xFFFF_8000_0000_1000;

    private const string NodeLayout = "struct node { uint64 next; if (next != 0) { uint32 payload; } uint8 tail; };";

    /// <summary>Reads, resolves, updates and writes a node whose <c>next</c> field is nonzero above 2^63 or zero.</summary>
    /// <param name="next">The <c>next</c> value.</param>
    /// <param name="present">Whether <c>payload</c> is present.</param>
    [TestMethod]
    [DataRow(KernelAddress, true)]
    [DataRow(0x8000_0000_0000_0000UL, true)]
    [DataRow(ulong.MaxValue, true)]
    [DataRow(0UL, false)]
    public void ConditionOnUInt64AboveTwoToThe63_SelectsInEveryOperation(ulong next, bool present)
    {
        var layout = new CStruct(NodeLayout);
        byte[] bytes = Node(next, present);

        // Read: the arm is chosen by the exact value, and the tail follows it.
        StructValue value = layout.Parse(bytes, "node");
        Assert.AreEqual(next, value["next"]);
        Assert.AreEqual(present, value.ContainsKey("payload"));
        Assert.AreEqual((byte)0x5A, value["tail"]);

        // Selected read and address resolution measure the same arm without reading it.
        Assert.AreEqual((byte)0x5A, layout.ReadValue<byte>(bytes, "node.tail"));
        Assert.AreEqual(present ? 12L : 8L, layout.ResolveAddress(bytes, "node.tail"));

        // Write: the same condition, evaluated on the supplied value, writes the same bytes.
        CollectionAssert.AreEqual(bytes, layout.Serialize("node", value));

        // Update: a field after the condition is placed through the same decision.
        byte[] updated = (byte[])bytes.Clone();
        layout.Update(updated, "node.tail", (byte)0x33);
        Assert.AreEqual((byte)0x33, updated[^1]);
        Assert.AreEqual(bytes.Length, updated.Length);
    }

    /// <summary>A count derived from a uint64 above 2^63 is exact: <c>next - 0xFFFF800000000FFE</c> is 2.</summary>
    [TestMethod]
    public void CountFromUInt64AboveTwoToThe63_IsExact()
    {
        var layout = new CStruct("struct r { uint64 next; uint8 data[next - 0xFFFF800000000FFE]; };");
        byte[] bytes = new byte[10];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, KernelAddress);
        bytes[8] = 1;
        bytes[9] = 2;

        Assert.AreEqual(2, layout.GetArrayLength(bytes, "r.data"));
        StructValue value = layout.Parse(bytes, "r");
        CollectionAssert.AreEqual(new object[] { (byte)1, (byte)2, }, (System.Collections.ICollection)value["data"]!);
        CollectionAssert.AreEqual(bytes, layout.Serialize("r", value));
    }

    /// <summary>A switch on a uint64 tag matches labels over the whole 64-bit range, including 0xFFFFFFFFFFFFFFFF.</summary>
    /// <param name="tag">The tag value.</param>
    /// <param name="member">The member the matching arm declares, or <see langword="null"/> for the default arm.</param>
    [TestMethod]
    [DataRow(ulong.MaxValue, "all")]
    [DataRow(0x8000_0000_0000_0000UL, "top")]
    [DataRow(1UL, "one")]
    [DataRow(2UL, null)]
    public void SwitchOnUInt64Tag_MatchesLabelsAcrossTheWholeRange(ulong tag, string? member)
    {
        var layout = new CStruct(
            "struct r { uint64 tag; switch (tag) { case 0xFFFFFFFFFFFFFFFF: { uint8 all; } case 0x8000000000000000: { uint8 top; } case 1: { uint8 one; } default: { uint8 other; } } };");
        byte[] bytes = new byte[9];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, tag);
        bytes[8] = 7;

        StructValue value = layout.Parse(bytes, "r");

        Assert.AreEqual((byte)7, value[member ?? "other"]);
        CollectionAssert.AreEqual(bytes, layout.Serialize("r", value));
    }

    /// <summary>A literal is its exact value: <c>0xFFFFFFFF</c> is 4294967295, as in C, never a reinterpreted -1.</summary>
    [TestMethod]
    public void HexadecimalLiteral_IsItsExactValue()
    {
        var layout = new CStruct("#define ALL 0xFFFFFFFF\nstruct r { uint8 data[ALL - 4294967293]; uint8 flag[ALL == 4294967295]; uint8 neg[-0x80000000 == -2147483648]; };");

        StructValue value = layout.Parse(new byte[] { 1, 2, 3, 4, }, "r");

        Assert.HasCount(2, (System.Collections.IList)value["data"]!);
        Assert.HasCount(1, (System.Collections.IList)value["flag"]!);
        Assert.HasCount(1, (System.Collections.IList)value["neg"]!);
        Assert.AreEqual(new System.Numerics.BigInteger(4294967295), layout.Constants["ALL"].Value);
    }

    /// <summary>Arithmetic that leaves the signed 128-bit range fails; the same operands one step inside succeed.</summary>
    [TestMethod]
    public void ArithmeticOutsideInt128_FailsAndNamesTheDomain()
    {
        var layout = new CStruct("struct r { uint64 a; uint64 b; uint8 data[(a * b) >> 124]; };");
        byte[] bytes = new byte[17];

        // (2^63 * 2^63) >> 124 = 4; one factor larger is still inside the domain.
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, 1UL << 63);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 1UL << 63);
        Assert.AreEqual(4, layout.GetArrayLength(bytes, "r.data"));

        // (2^64 - 1)^2 is below 2^128 but above 2^127 - 1: the product itself fails.
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, ulong.MaxValue);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), ulong.MaxValue);
        CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(bytes, "r"));
        StringAssert.StartsWith(failure.Message, "Cannot evaluate array length for data: the result is outside the 128-bit range that layout expressions support");
    }

    /// <summary>A shift count must be a bit index of the 128-bit domain: 127 is the last valid one.</summary>
    [TestMethod]
    public void ShiftCount128_FailsWhereTheLayoutIsBuilt()
    {
        Assert.AreEqual(1, new CStruct("struct r { uint8 data[(1 << 126) >> 126]; };").GetStructSizeInBytes("r"));

        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct("struct r { uint8 data[(1 << 128) >> 128]; };"));
        StringAssert.Contains(failure.Message, "Expression shift count must be between 0 and 127.");
    }

    /// <summary>A consumer that stores an int rejects a larger domain value with the value and what it was for.</summary>
    /// <param name="layoutText">The layout.</param>
    /// <param name="message">The expected failure text.</param>
    [TestMethod]
    [DataRow("struct r { uint8 a @align(1 << 31); };", "The alignment override for a is 2147483648, which does not fit in a signed 32-bit integer.")]
    [DataRow("struct r { uint8 a; uint8 b @ 0x100000000; };", "The offset assertion for b is 4294967296, which does not fit in a signed 32-bit integer.")]
    [DataRow("struct r { uint8 data[0x100000000]; };", "The array length for data is 4294967296, which does not fit in a signed 32-bit integer.")]
    [DataRow("typedef uint8 block[0x100000000]; struct r { uint8 a; };", "The array length for typedef block is 4294967296, which does not fit in a signed 32-bit integer.")]
    public void Int32Consumer_RejectsALargerValueWithItsValue(string layoutText, string message)
    {
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct(layoutText));

        StringAssert.Contains(failure.Message, message);
    }

    /// <summary>Encodes a node: <c>next</c>, the payload when present, and the tail byte 0x5A.</summary>
    /// <param name="next">The <c>next</c> value.</param>
    /// <param name="present">Whether the payload is present.</param>
    /// <returns>The node's bytes.</returns>
    private static byte[] Node(ulong next, bool present)
    {
        byte[] bytes = new byte[present ? 13 : 9];
        BinaryPrimitives.WriteUInt64LittleEndian(bytes, next);
        if (present)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8), 0xDEADBEEF);
        }

        bytes[^1] = 0x5A;
        return bytes;
    }
}
