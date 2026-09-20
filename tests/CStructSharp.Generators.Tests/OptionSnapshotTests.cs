namespace CStructSharp.Generators.Tests;

using System;
using System.Reflection;
using CStructSharp;

/// <summary>
///     One snapshot per attribute option over the same layout, so a change in how an option reaches the generated
///     code (the constants the readers use, the runtime layout's options, the names, the views) is a reviewed diff;
///     each variant also proves its option took effect through the generated code and the runtime layout it builds.
/// </summary>
[TestClass]
public class OptionSnapshotTests
{
    private const string Definition = "#ifdef WIDE\\nstruct root { long a; uint8 b_c:3; uint8 d:5; uint32 e; };\\n#else\\nstruct root { uint16 a; uint8 b_c:3; uint8 d:5; uint32 e; };\\n#endif";

    [TestMethod]
    [DataRow("Defaults", "")]
    [DataRow("Aligned", ", Aligned = true")]
    [DataRow("BigEndian", ", LittleEndian = false")]
    [DataRow("Pointer2", ", PointerSize = 2")]
    [DataRow("Msvc", ", BitfieldPacking = BitfieldPacking.Msvc")]
    [DataRow("HighBitFirst", ", BitfieldAllocation = BitfieldAllocation.HighBitFirst")]
    [DataRow("CLong32", ", CLongWidth = 32, Defined = new[] { \"WIDE\" }")]
    [DataRow("Defined", ", Defined = new[] { \"WIDE\" }")]
    [DataRow("KeepNames", ", KeepNames = true")]
    [DataRow("NoViews", ", Views = false")]
    public void EachOption_HasASnapshot_AndTakesEffect(string variant, string arguments)
    {
        GeneratorResult result = GeneratorRunner.Run("using CStructSharp;\n\nnamespace Demo;\n\n[CStructLayout(\"" + Definition + "\"" + arguments + ")]\npublic static partial class Options { }\n").AssertClean();
        Snapshot.Match("Options." + variant, result.Source);
        Assembly assembly = result.Load();
        Type options = assembly.GetType("Demo.Options")!;
        var layout = (CStruct)options.GetProperty("Layout")!.GetValue(null)!;
        Type root = assembly.GetType(variant == "KeepNames" ? "Demo.Options+root" : "Demo.Options+Root")!;
        switch (variant)
        {
        case "Aligned":
            Assert.IsTrue(layout.Aligned);
            Assert.AreEqual(8, layout.GetStructSizeInBytes("root"));
            break;
        case "BigEndian":
            Assert.IsFalse(layout.IsLittleEndian);
            Assert.AreEqual((ushort)0x0102, root.GetProperty("A")!.GetValue(options.GetMethod("Parse", [typeof(byte[]), typeof(ReadOptions)])!.Invoke(null, [new byte[] { 1, 2, 0, 0, 0, 0, 0 }, null])));
            break;
        case "Pointer2":
            Assert.AreEqual(2, layout.PointerSize);
            break;
        case "Msvc":
            Assert.AreEqual(BitfieldPacking.Msvc, layout.CompilationOptions.BitfieldPacking);
            break;
        case "HighBitFirst":
            Assert.AreEqual(BitfieldAllocation.HighBitFirst, layout.CompilationOptions.BitfieldAllocation);
            Assert.AreEqual((byte)0b101, root.GetProperty("BC")!.GetValue(options.GetMethod("Parse", [typeof(byte[]), typeof(ReadOptions)])!.Invoke(null, [new byte[] { 0, 0, 0b1010_0000, 0, 0, 0, 0 }, null])));
            break;
        case "CLong32":
            Assert.AreEqual(32, layout.CompilationOptions.CLongWidth);
            Assert.AreEqual(typeof(int), root.GetProperty("A")!.PropertyType, "long is 32 bits wide");
            break;
        case "Defined":
            Assert.AreEqual(typeof(long), root.GetProperty("A")!.PropertyType, "WIDE selects the long branch");
            break;
        case "KeepNames":
            Assert.IsNotNull(root.GetProperty("b_c"));
            Assert.IsNull(root.GetProperty("BC"));
            break;
        case "NoViews":
            Assert.IsNull(assembly.GetType("Demo.Options+RootView"));
            break;
        default:
            Assert.IsNotNull(assembly.GetType("Demo.Options+RootView"));
            Assert.AreEqual(typeof(ushort), root.GetProperty("A")!.PropertyType);
            break;
        }
    }
}
