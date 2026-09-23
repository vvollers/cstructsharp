namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks that enum-dependent definitions retain the enum's shift-width limits through aliases.</summary>
[TestClass]
public class EnumDefinitionShiftBoundaryTests
{
    /// <summary>A wide intermediate shift remains invalid even when later arithmetic would produce a small value.</summary>
    /// <param name="definitions">The direct or transitive definition chain used by the enum member.</param>
    [TestMethod]
    [DataRow("#define VALUE ((1 << 64) >> 64)\n")]
    [DataRow("#define BASE ((1 << 64) >> 64)\n#define VALUE BASE\n")]
    public void DefinitionShift_UsesTheEnumWidth(string definitions)
    {
        // The generic constant evaluator permits 128-bit shifts, but a uint64 enum permits counts only through 63.
        Assert.Throws<CStructLayoutException>(() => new CStruct(definitions + "enum flags : uint64 { Selected = VALUE }; struct root { flags value; };"));
    }
}
