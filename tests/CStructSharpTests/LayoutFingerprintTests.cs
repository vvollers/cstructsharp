namespace CStructSharp.Tests;

using CStructSharp.Compilation;

/// <summary>
///     The layout fingerprint that decides whether a mapped class's direct members fit the layout a call uses: layouts
///     that place or decode a member differently must not share one.
/// </summary>
[TestClass]
public class LayoutFingerprintTests
{
    /// <summary>The same layout text gives the same fingerprint.</summary>
    [TestMethod]
    public void SameLayout_SameFingerprint()
    {
        Assert.AreEqual(Fingerprint("struct r { uint8 g[2][3]; uint16 n; };"), Fingerprint("struct r { uint8 g[2][3]; uint16 n; };"));
    }

    /// <summary>
    ///     Arrays with the same element count and total size but other dimensions nest their values differently, so every
    ///     dimension counts, not only the number of dimensions.
    /// </summary>
    [TestMethod]
    public void InnerDimensions_ChangeTheFingerprint()
    {
        Assert.AreNotEqual(Fingerprint("struct r { uint8 g[2][3]; };"), Fingerprint("struct r { uint8 g[3][2]; };"));
    }

    /// <summary>A bitfield's bit offset in its unit counts: the same widths under another packing rule decode other bits.</summary>
    [TestMethod]
    public void BitfieldPlacement_ChangesTheFingerprint()
    {
        const string Layout = "struct r { uint8 a : 3; uint8 b : 3; uint8 c : 4; };";
        ulong sysV = Fingerprint(Layout);
        ulong msvc = Fingerprint(Layout, new CStructCompilationOptions { BitfieldPacking = BitfieldPacking.Msvc, });
        Assert.AreNotEqual(sysV, msvc);
    }

    /// <summary>The fingerprint of a layout's root struct.</summary>
    /// <param name="layout">The layout text; its struct is named <c>r</c>.</param>
    /// <param name="options">The compilation options, or <see langword="null"/>.</param>
    /// <returns>The fingerprint.</returns>
    private static ulong Fingerprint(string layout, CStructCompilationOptions? options = null)
    {
        var compiled = new CStruct(layout, compilationOptions: options);
        return LayoutFingerprint.Compute((CompiledCompositeType)compiled.CompiledModel.Symbols["r"].Symbol.Definition!);
    }
}
