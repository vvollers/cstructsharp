namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks count publication and inclusive array limits in fixed nested read plans.</summary>
[TestClass]
public class StaticPlanCaptureBoundaryTests
{
    /// <summary>A nested array is not an integer: naming it through its containing path fails layout construction.</summary>
    [TestMethod]
    public void NestedNumericArray_FailsConstruction()
    {
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct("struct inner { uint8 count[2]; }; struct outer { inner child; }; struct root { outer header; uint8 values[header.child.count]; uint8 tail; };"));
        StringAssert.Contains(failure.Message, "Field 'count' is an array");
    }

    /// <summary>Nested text is not an integer: naming it through its containing path fails layout construction.</summary>
    [TestMethod]
    public void NestedCharacterArray_FailsConstruction()
    {
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct("#define AB 2\nstruct inner { char count[2]; }; struct outer { inner child; }; struct root { outer header; uint8 values[header.child.count]; uint8 tail; };"));
        StringAssert.Contains(failure.Message, "Field 'count' is text");
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
