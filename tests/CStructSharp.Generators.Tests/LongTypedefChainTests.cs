namespace CStructSharp.Generators.Tests;

using System;
using System.Text;

/// <summary>Checks that generated member types retain array dimensions through long finite alias chains.</summary>
[TestClass]
public class LongTypedefChainTests
{
    /// <summary>A generated field at the end of 257 aliases remains a byte array rather than a single byte.</summary>
    [TestMethod]
    public void ArrayAliasChain_PreservesTheGeneratedPropertyType()
    {
        var definition = new StringBuilder("typedef uint8 base_type[2];");
        string name = "base_type";
        for (int index = 0; index < 257; index++)
        {
            string alias = "alias_" + index;
            definition.Append("typedef ").Append(name).Append(' ').Append(alias).Append(';');
            name = alias;
        }

        definition.Append("struct root { ").Append(name).Append(" values; uint8 tail; };");
        string source = "using CStructSharp; namespace Demo;\n" +
            "/// <summary>A consumer of the long array alias chain.</summary>\n" +
            "[CStructLayout(\"" + definition + "\", Root = \"root\")] public static partial class Aliases { }";
        GeneratorResult result = GeneratorRunner.Run(source).AssertClean();
        Type root = result.Load().GetType("Demo.Aliases+Root")!;

        Assert.AreEqual(typeof(byte[]), root.GetProperty("Values")!.PropertyType);
        Assert.AreEqual(typeof(byte), root.GetProperty("Tail")!.PropertyType);
    }
}
