namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;

/// <summary>Checks that finite typedef chains retain their final composite or array shape.</summary>
[TestClass]
public class LongTypedefChainTests
{
    /// <summary>A finite chain of aliases to a struct has the same public size as its underlying declaration.</summary>
    /// <param name="count">The number of aliases between the queried name and the struct.</param>
    [TestMethod]
    [DataRow(255)]
    [DataRow(256)]
    [DataRow(257)]
    public void CompositeAliases_RetainTheirRootShape(int count)
    {
        var definition = new StringBuilder("struct base_type { uint8 value; };");
        string name = "base_type";
        for (int index = 0; index < count; index++)
        {
            string alias = "alias_" + index;
            definition.Append("typedef ").Append(name).Append(' ').Append(alias).Append(';');
            name = alias;
        }

        var layout = new CStruct(definition.ToString());

        Assert.AreEqual(1, layout.GetStructSizeInBytes(name));
        dynamic parsed = layout.Parse(new byte[] { 7, }.AsSpan(), name);
        Assert.AreEqual((byte)7, (byte)parsed.value);
    }

    /// <summary>A fixed array alias at the end of a finite chain keeps all its elements in a containing record.</summary>
    /// <param name="count">The number of plain aliases before the fixed array alias.</param>
    [TestMethod]
    [DataRow(255)]
    [DataRow(257)]
    public void ArrayAliases_RetainTheirDimensions(int count)
    {
        var definition = new StringBuilder("typedef uint8 base_type[2];");
        string name = "base_type";
        for (int index = 0; index < count; index++)
        {
            string alias = "alias_" + index;
            definition.Append("typedef ").Append(name).Append(' ').Append(alias).Append(';');
            name = alias;
        }

        definition.Append("struct root { ").Append(name).Append(" values; uint8 tail; };");
        var layout = new CStruct(definition.ToString());

        Assert.AreEqual(3, layout.GetStructSizeInBytes("root"));
        byte[] bytes = [7, 8, 9,];
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        CollectionAssert.AreEqual(new object?[] { (byte)7, (byte)8, }, ((IEnumerable<object?>)parsed.values).ToArray());
        Assert.AreEqual((byte)9, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
    }

    /// <summary>Following aliases remains bounded when a declaration chain loops back to an earlier name.</summary>
    /// <param name="arrayShape">The optional fixed dimensions on one alias in the cycle.</param>
    [TestMethod]
    [DataRow("")]
    [DataRow("[2]")]
    public void CyclicAliases_RemainInvalid(string arrayShape)
    {
        string definition = "typedef second first" + arrayShape + "; typedef first second; struct root { first value; };";

        // A cycle is invalid even when its declarations also carry a fixed array shape.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));

        StringAssert.Contains(failure.Message, "Circular typedef dependency detected at:");
    }
}
