namespace CStructSharp.Tests;

/// <summary>Checks qualified count publication through runtime-sized nested records.</summary>
[TestClass]
public class ReaderNestedCaptureTests
{
    /// <summary>Nested scalar and enum counts retain every containing field name.</summary>
    /// <param name="countType">The primitive or enum count type.</param>
    [TestMethod]
    [DataRow("uint8")]
    [DataRow("kind")]
    public void RuntimeNestedCount_PublishesTheCompletePath(string countType)
    {
        var layout = new CStruct("enum kind : uint8 { TWO = 2 }; struct inner { " + countType + " count; uint8 padding[count]; }; struct outer { inner child; }; struct root { outer header; uint8 values[header.child.count]; uint8 tail; };");
        byte[] bytes = [2, 31, 32, 41, 42, 99,];

        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");

        // The nested runtime array prevents a fixed-layout plan from bypassing field-by-field publication.
        CollectionAssert.AreEqual(new object?[] { (byte)41, (byte)42, }, ((IEnumerable<object?>)parsed.values).ToArray());
        Assert.AreEqual((byte)99, (byte)parsed.tail);
    }
}
