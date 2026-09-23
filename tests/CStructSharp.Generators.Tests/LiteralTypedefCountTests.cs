namespace CStructSharp.Generators.Tests;

using System.Linq;

/// <summary>Checks generated layouts reject unused array aliases with negative literal dimensions.</summary>
[TestClass]
public class LiteralTypedefCountTests
{
    /// <summary>The shared compiler reports the invalid alias instead of emitting a layout that contains it.</summary>
    /// <param name="count">A negative count in decimal or signed hexadecimal form.</param>
    [TestMethod]
    [DataRow("-1")]
    [DataRow("0xFFFFFFFF")]
    public void UnusedNegativeArrayAlias_ReportsLayoutDiagnostic(string count)
    {
        string source = "using CStructSharp; namespace Demo;\n" +
            "/// <summary>A consumer with an invalid unused array alias.</summary>\n" +
            "[CStructLayout(\"typedef uint8 invalid[" + count + "]; struct root { uint8 value; };\", Root = \"root\")] " +
            "public static partial class InvalidAlias { }";
        GeneratorResult result = GeneratorRunner.Run(source);

        StringAssert.Contains(result.DiagnosticsWithId("CSG001").Single().GetMessage(), "Array length cannot be negative: invalid");
        Assert.IsEmpty(result.GeneratedSources);
    }
}
