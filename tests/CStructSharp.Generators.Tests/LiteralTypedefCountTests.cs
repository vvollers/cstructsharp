namespace CStructSharp.Generators.Tests;

using System.Linq;

/// <summary>Checks generated layouts reject unused array aliases with dimensions that are negative or too large.</summary>
[TestClass]
public class LiteralTypedefCountTests
{
    /// <summary>The shared compiler reports the invalid alias instead of emitting a layout that contains it.</summary>
    /// <param name="count">An invalid count: negative, or a hexadecimal literal whose exact value exceeds Int32.</param>
    /// <param name="message">The expected diagnostic text.</param>
    [TestMethod]
    [DataRow("-1", "Array length cannot be negative: invalid")]
    [DataRow("-0x1", "Array length cannot be negative: invalid")]
    [DataRow("0xFFFFFFFF", "The array length for typedef invalid is 4294967295, which does not fit in a signed 32-bit integer.")]
    public void UnusedInvalidArrayAlias_ReportsLayoutDiagnostic(string count, string message)
    {
        string source = "using CStructSharp; namespace Demo;\n" +
            "/// <summary>A consumer with an invalid unused array alias.</summary>\n" +
            "[CStructLayout(\"typedef uint8 invalid[" + count + "]; struct root { uint8 value; };\", Root = \"root\")] " +
            "public static partial class InvalidAlias { }";
        GeneratorResult result = GeneratorRunner.Run(source);

        StringAssert.Contains(result.DiagnosticsWithId("CSG001").Single().GetMessage(), message);
        Assert.IsEmpty(result.GeneratedSources);
    }
}
