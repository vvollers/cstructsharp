namespace CStructSharp.Tests;

using System.Collections.Generic;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     <c>CStruct.Matches</c> on a runtime layout: it recognizes by the <c>@expect</c> (or <c>@magic</c>) conditions on members at
///     build-time offsets, returns <see langword="false"/> for short input, ignores <c>@supports</c> and later members, and
///     uses caller variables and byte-string comparisons.
/// </summary>
[TestClass]
public class MatchesRuntimeTests
{
    private const string Super = "struct sb { uint32 inodes; uint16 magic @expect(magic == 0xEF53); uint16 state @supports(state < 4); uint8 tail[inodes]; };";

    /// <summary>The magic decides; the member after it, a failing <c>@supports</c> and a missing tail do not matter.</summary>
    [TestMethod]
    public void ExpectOnFixedMembers_DecidesTheMatch()
    {
        var layout = new CStruct(Super);
        Assert.IsTrue(layout.Matches("sb", new byte[] { 1, 0, 0, 0, 0x53, 0xEF, 9, 0 }));
        Assert.IsFalse(layout.Matches("sb", new byte[] { 1, 0, 0, 0, 0x53, 0xEE, 0, 0 }));
        Assert.IsFalse(layout.Matches("sb", new byte[] { 1, 0, 0, 0, 0x53 }), "the magic is not fully present");
        Assert.IsFalse(layout.Matches("sb", ReadOnlySpan<byte>.Empty));
    }

    /// <summary>A struct with a magic is recognized by it alone, so an <c>@expect</c> of another member is ignored.</summary>
    [TestMethod]
    public void Magic_IsCheckedAlone()
    {
        var layout = new CStruct("struct h { uint8 sig[4] @magic(sig == b\"\\x7fELF\", \"elf\"); uint8 cls @expect(cls == 2); };");
        Assert.IsTrue(layout.Matches("h", new byte[] { 0x7F, 0x45, 0x4C, 0x46, 7 }));
        Assert.IsFalse(layout.Matches("h", new byte[] { 0x7F, 0x45, 0x4C, 0x47, 2 }));
        Assert.IsFalse(layout.Matches("h", new byte[] { 0x7F, 0x45, 0x4C }));
    }

    /// <summary>A condition may name a caller variable and a layout constant; a variable that is not supplied cannot be evaluated, which does not match.</summary>
    [TestMethod]
    public void Variables_AndConstants()
    {
        var layout = new CStruct("#define LIMIT 10\nstruct h { uint8 version @expect(version <= LIMIT && version != BAD); };");
        var variables = new Dictionary<string, int> { ["BAD"] = 3 };
        Assert.IsTrue(layout.Matches("h", new byte[] { 2 }, variables));
        Assert.IsFalse(layout.Matches("h", new byte[] { 3 }, variables));
        Assert.IsFalse(layout.Matches("h", new byte[] { 2 }), "BAD is not supplied");
        Assert.IsFalse(layout.Matches("h", new byte[] { 11 }, variables));
    }

    /// <summary>Text compared with a byte string is checked on the bytes; a condition on a member after a runtime-sized array is not evaluated.</summary>
    [TestMethod]
    public void ByteStrings_AndMembersAfterVariableSize()
    {
        var layout = new CStruct("struct b { char oem[4] @expect(oem == \"NTFS\"); uint8 n; uint8 d[n]; uint8 after @expect(after == 1); };");
        Assert.IsTrue(layout.Matches("b", new byte[] { (byte)'N', (byte)'T', (byte)'F', (byte)'S', 9 }));
        Assert.IsFalse(layout.Matches("b", new byte[] { (byte)'N', (byte)'T', (byte)'F', (byte)'X', 9 }));
    }

    /// <summary>A struct nothing can be evaluated for, a union and an unknown name are reported, not answered.</summary>
    [TestMethod]
    public void NoConditions_AreReported()
    {
        var layout = new CStruct("struct plain { uint8 a; }; struct late { uint8 n; uint8 d[n]; uint8 z @expect(z == 1); };");
        Assert.Throws<CStructLayoutException>(() => layout.Matches("plain", new byte[] { 1 }));
        Assert.Throws<CStructLayoutException>(() => layout.Matches("late", new byte[] { 0, 1 }));
        Assert.Throws<CStructPathException>(() => layout.Matches("missing", new byte[] { 1 }));
    }
}
