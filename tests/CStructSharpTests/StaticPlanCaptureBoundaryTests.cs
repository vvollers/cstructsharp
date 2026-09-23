namespace CStructSharp.Tests;

/// <summary>Checks count publication and inclusive array limits in fixed nested read plans.</summary>
[TestClass]
public class StaticPlanCaptureBoundaryTests
{
    /// <summary>A fixed nested numeric array publishes its last element under the complete containing path.</summary>
    [TestMethod]
    public void NestedNumericArray_PublishesItsLastElement()
    {
        var layout = new CStruct("struct inner { uint8 count[2]; }; struct outer { inner child; }; struct root { outer header; uint8 values[header.child.count]; uint8 tail; };");
        byte[] bytes = [1, 2, 41, 42, 99,];

        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");

        CollectionAssert.AreEqual(new object?[] { (byte)41, (byte)42, }, ((IEnumerable<object?>)parsed.values).ToArray());
        Assert.AreEqual((byte)99, (byte)parsed.tail);
    }

    /// <summary>A fixed nested character array publishes its identifier for a later length expression.</summary>
    [TestMethod]
    public void NestedCharacterArray_PublishesItsIdentifier()
    {
        var layout = new CStruct("#define AB 2\nstruct inner { char count[2]; }; struct outer { inner child; }; struct root { outer header; uint8 values[header.child.count]; uint8 tail; };");
        byte[] bytes = [65, 66, 41, 42, 99,];

        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");

        Assert.AreEqual("AB", (string)parsed.header.child.count);
        CollectionAssert.AreEqual(new object?[] { (byte)41, (byte)42, }, ((IEnumerable<object?>)parsed.values).ToArray());
        Assert.AreEqual((byte)99, (byte)parsed.tail);
    }

    /// <summary>Exactly the allowed element count is accepted for fixed character and numeric arrays.</summary>
    /// <param name="type">The element type whose static operation checks the limit.</param>
    [TestMethod]
    [DataRow("char")]
    [DataRow("uint8")]
    public void ArrayCount_EqualToLimit_IsAccepted(string type)
    {
        var layout = new CStruct("struct root { " + type + " values[2]; uint8 tail; };");
        byte[] bytes = [65, 66, 99,];

        dynamic parsed = layout.Parse(bytes.AsSpan(), "root", options: new ReadOptions { MaxArrayElements = 2, });

        if (type == "char")
        {
            Assert.AreEqual("AB", (string)parsed.values);
        }
        else
        {
            CollectionAssert.AreEqual(new object?[] { (byte)65, (byte)66, }, ((IEnumerable<object?>)parsed.values).ToArray());
        }

        Assert.AreEqual((byte)99, (byte)parsed.tail);
    }
}
