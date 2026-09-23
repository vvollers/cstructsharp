namespace CStructSharp.Generators.Tests;

using System.Linq;

/// <summary>Checks generated layouts cannot hide unsupported pointer-to-array storage in an alias.</summary>
[TestClass]
public class PointerArrayAliasTests
{
    /// <summary>The shared compiler rejects the alias whether or not the generated root uses it.</summary>
    /// <param name="used">Whether a field instantiates the invalid pointer alias.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void PointerToArrayAlias_ReportsLayoutDiagnostic(bool used)
    {
        string member = used ? "link value;" : "uint8 value;";
        string source = "using CStructSharp; namespace Demo;\n" +
            "/// <summary>A consumer containing a pointer to an array alias.</summary>\n" +
            "[CStructLayout(\"typedef uint8 pair[2]; typedef pair *link; struct root { " + member + " };\", Root = \"root\")] " +
            "public static partial class InvalidAlias { }";
        GeneratorResult result = GeneratorRunner.Run(source);

        StringAssert.Contains(result.DiagnosticsWithId("CSG001").Single().GetMessage(), "A pointer to a typedef array is not supported: link");
        Assert.IsEmpty(result.GeneratedSources);
    }
}
