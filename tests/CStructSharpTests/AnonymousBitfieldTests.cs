namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.IO;

/// <summary>
///     Verifies unnamed nonzero-width bitfields (LANG-17, e.g. <c>uint8 :3;</c>): a declarator that carries a bit
///     width but no name reserves storage as pure padding, never becomes an addressable path, POCO member, or JSON
///     field, and always round-trips as canonical zero bits.
/// </summary>
[TestClass]
public class AnonymousBitfieldTests
{
    /// <summary>
    ///     Padding between two named declarators in one comma-separated list consumes its own bits without becoming
    ///     a member of the parsed result.
    /// </summary>
    [TestMethod]
    public void PaddingBetweenNamedDeclarators_ConsumesBitsButIsNotExposed()
    {
        var cstruct = new CStruct("struct root { uint8 flag:1, :3, other:4; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0b1011_0001, });

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(1, (int)parsed.flag);
        Assert.AreEqual(0b1011, (int)parsed.other);
        var dictionary = (IDictionary<string, object?>)parsed;
        Assert.AreEqual(2, dictionary.Count);
        Assert.IsFalse(dictionary.ContainsKey(string.Empty));
    }

    /// <summary>A field anonymous from its own leading word run (single-word type, no name at all) still compiles.</summary>
    [TestMethod]
    public void WholeFieldAnonymousFromTheStart_Compiles()
    {
        var cstruct = new CStruct("struct root { uint8 :3; };", pointerSize: 1);

        Assert.AreEqual(1, cstruct.GetStructSizeInBytes("root"));

        using var stream = new MemoryStream(new byte[] { 0xFF, });
        dynamic parsed = cstruct.ParseStream(stream, "root");
        Assert.AreEqual(0, ((IDictionary<string, object?>)parsed).Count);
    }

    /// <summary>Multiple anonymous fields in one struct do not collide with each other as duplicate names.</summary>
    [TestMethod]
    public void MultipleAnonymousFields_DoNotCollide()
    {
        var cstruct = new CStruct("struct root { uint8 :2, :3, :3; };", pointerSize: 1);

        Assert.AreEqual(1, cstruct.GetStructSizeInBytes("root"));
    }

    /// <summary>New output always writes zero for the padding bits, regardless of the surrounding named values.</summary>
    [TestMethod]
    public void WriteStreamAndSerialize_AlwaysWriteZeroForThePadding()
    {
        var cstruct = new CStruct("struct root { uint8 flag:1, :3, other:4; };", pointerSize: 1);

        byte[] bytes = cstruct.Serialize("root", new { flag = 1, other = 0b1111, });

        // flag=1 in bit 0, the middle 3 padding bits forced to zero regardless of the source byte's other bits,
        // other=0b1111 in the top 4 bits.
        CollectionAssert.AreEqual(new byte[] { 0b1111_0001, }, bytes);

        using var writeStream = new MemoryStream();
        cstruct.WriteStream(writeStream, "root", new { flag = 1, other = 0b1111, });
        CollectionAssert.AreEqual(new byte[] { 0b1111_0001, }, writeStream.ToArray());
    }

    /// <summary>Updating a named sibling through its own path only rewrites that field's own bit range, not the padding's.</summary>
    [TestMethod]
    public void UpdateStream_OnANamedSibling_DoesNotDisturbThePaddingBits()
    {
        var cstruct = new CStruct("struct root { uint8 flag:1, :3, other:4; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0b1010_1101, });

        cstruct.UpdateStream(stream, "root.flag", 0);

        // Only bit 0 (flag) changes; the padding bits (1-3) and other's bits (4-7) are untouched.
        CollectionAssert.AreEqual(new byte[] { 0b1010_1100, }, stream.ToArray());
    }

    /// <summary>
    ///     A two-or-more-token word run keeps today's "last word is the name" rule unchanged, even with a bit
    ///     width - only a one-token run is anonymous. A multi-word anonymous type is therefore out of scope for
    ///     V1: <c>unsigned int :3;</c> is not rejected, it names a field "int" of type "unsigned" (uint32).
    /// </summary>
    [TestMethod]
    public void TwoWordRunWithBitWidth_KeepsExistingLastWordAsNameSplit()
    {
        var cstruct = new CStruct("struct root { unsigned int :3; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x05, 0, 0, 0, });

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(0x05, (int)parsed.@int);
    }

    /// <summary>A declarator with neither a name nor a bit width carries no information and is rejected.</summary>
    [TestMethod]
    public void DeclaratorWithNeitherNameNorBitWidth_IsRejected()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 a, ; };"));
    }

    /// <summary>Padding is still visible in debug output, since it is inspectable storage even though it is unaddressable.</summary>
    [TestMethod]
    public void DebugOutput_StillRegistersThePaddingsByteRange()
    {
        var cstruct = new CStruct("struct root { uint8 flag:1, :3, other:4; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0b1011_0001, });

        (List<DebugData> debug, dynamic _) = cstruct.ParseStreamWithDebug(stream, "root");

        Assert.IsTrue(debug.Exists(entry => entry.DebugStackString == "root."));
    }

    /// <summary>An anonymous declarator can never be addressed by path, since a requested path segment can never be empty.</summary>
    [TestMethod]
    public void AnonymousField_IsNeverAddressableByPath()
    {
        var cstruct = new CStruct("struct root { uint8 flag:1, :3, other:4; };", pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0b1011_0001, });

        Assert.Throws<CStructPathException>(() => cstruct.ResolveAddress(stream, "root."));
    }
}
