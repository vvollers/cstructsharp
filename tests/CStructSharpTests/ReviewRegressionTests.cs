namespace CStructSharp.Tests;

using System.Dynamic;
using System.Numerics;
using CStructSharp.Structure;

/// <summary>Contains focused reproductions for correctness findings that were previously covered only indirectly.</summary>
[TestClass]
public class ReviewRegressionTests
{
    /// <summary>
    ///     The two bytes 34 12 describe both a small byte and a wider uint16 union member.
    /// </summary>
    /// <remarks>
    ///     With no member explicitly selected, serialization must preserve both original bytes. Choosing the first
    ///     member automatically would lose the second byte and change an untouched record.
    /// </remarks>
    [TestMethod]
    public void Serialize_ParsedUnion_PreservesCompleteRawStorage()
    {
        var cstruct = new CStruct("union choice { uint8 small; uint16 large; };");
        using var stream = new MemoryStream([0x34, 0x12,]);

        object parsed = cstruct.ParseStream(stream, "choice");
        byte[] serialized = cstruct.Serialize("choice", parsed);

        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, serialized);
    }

    /// <summary>
    ///     All four input bytes are FF, representing uint32 maximum 4294967295.
    /// </summary>
    /// <remarks>
    ///     The enum only names value 1, so the result must have no symbolic name but retain that exact number.
    ///     Narrowing through signed Int32 would incorrectly turn it into -1 or reject valid storage.
    /// </remarks>
    [TestMethod]
    public void ParseStream_UnknownUInt32Enum_PreservesCompleteDomain()
    {
        const string definition = "enum state : uint32 { Known = 1 }; struct root { state value; };";
        var cstruct = new CStruct(definition, isLittleEndian: true);
        using var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF,]);

        dynamic parsed = cstruct.ParseStream(stream, "root");
        var value = (EnumValueResult)parsed.value;

        Assert.AreEqual("state", value.Enum);
        Assert.IsNull(value.Name);
        Assert.AreEqual(new BigInteger(uint.MaxValue), value.Value);
    }

    /// <summary>
    ///     A pointer leads to two bytes representing 0x1234.
    /// </summary>
    /// <remarks>
    ///     Both union views must begin at that same target address, with small reading its first byte and large reading
    ///     both. The fixture checks byte order, alignment, raw storage, and debug positions so a union is not
    ///     accidentally read like sequential struct fields.
    /// </remarks>
    /// <param name="aligned">Whether the layout applies portable field alignment.</param>
    /// <param name="isLittleEndian">Whether multi-byte union members store their least-significant byte first.</param>
    [TestMethod]
    [DynamicData(nameof(RegressionTestSupport.AlignmentAndEndianMatrix), typeof(RegressionTestSupport))]
    public void ParseStream_PointerToUnion_RewindsEveryMemberToTargetAddress(
        bool aligned,
        bool isLittleEndian)
    {
        var targetBytes = new byte[2];
        RegressionTestSupport.WriteUnsigned(targetBytes, 0, 2, 0x1234, isLittleEndian);
        using PointerFixture fixture = RegressionTestSupport.CreatePointerFixture(
            "union choice { uint8 small; uint16 large; };",
            "choice",
            targetBytes,
            isLittleEndian,
            aligned: aligned);

        dynamic parsed = fixture.Layout.ParseStream(fixture.Stream, "root");
        var pointer = (Pointer)parsed.target;
        var union = (UnionValue)pointer.Value!;
        IReadOnlyDictionary<string, object?> members = union.Members;

        Assert.IsFalse(union.HasSelection);
        CollectionAssert.AreEqual(targetBytes, union.RawStorage!.Value.ToArray());
        Assert.AreEqual(targetBytes[0], (byte)members["small"]!);
        Assert.AreEqual((ushort)0x1234, (ushort)members["large"]!);
        RegressionTestSupport.AssertPositionRestored(fixture.Stream, 1);

        fixture.Stream.Position = 0;
        (List<DebugData> debug, _) = fixture.Layout.ParseStreamWithDebug(fixture.Stream, "root");
        Assert.IsTrue(
            debug.Count(item => item.CurPos == fixture.TargetAddress) >= 2,
            "Every pointer-target union member must start at the overlapping target address.");
        RegressionTestSupport.AssertPositionRestored(fixture.Stream, 1);

        fixture.Stream.Position = 0;
        Assert.AreEqual(
            fixture.TargetAddress,
            fixture.Layout.ResolveAddress(fixture.Stream, "root.target.value.large"));
        RegressionTestSupport.AssertPositionRestored(fixture.Stream, 0);
    }
}
