namespace CStructSharp.Tests;

using CStructSharp.Compilation;

/// <summary>Checks compiled bitfield positions across ordinary and inline composite members.</summary>
[TestClass]
public class CompiledBitfieldSeparationTests
{
    /// <summary>A non-bitfield member ends the previous storage run in both supported packing modes.</summary>
    /// <param name="packing">The storage-unit placement rule.</param>
    /// <param name="inline">Whether the separating byte belongs to an inline struct.</param>
    [TestMethod]
    [DataRow(BitfieldPacking.Msvc, false)]
    [DataRow(BitfieldPacking.Msvc, true)]
    [DataRow(BitfieldPacking.SysV, false)]
    [DataRow(BitfieldPacking.SysV, true)]
    public void SeparatingMember_StartsANewCompiledBitfieldRun(BitfieldPacking packing, bool inline)
    {
        string middle = inline ? "struct { uint8 value; } middle;" : "uint8 middle;";
        var layout = new CStruct("struct root { uint8 first : 3; " + middle + " uint8 last : 3; };", compilationOptions: new CStructCompilationOptions { BitfieldPacking = packing, });
        var root = (CompiledCompositeType)layout.CompiledModel.Symbols["root"].Symbol.Definition!;
        CompiledField last = root.Fields[2];

        Assert.AreEqual(2, last.FixedOffset);
        Assert.AreEqual(0, last.BitOffset);
        Assert.AreEqual(3, layout.GetStructSizeInBytes("root"));
    }
}
