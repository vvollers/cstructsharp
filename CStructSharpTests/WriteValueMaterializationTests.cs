namespace CStructSharp.Tests;

/// <summary>
///     Exercises <see cref="WriteValueMaterialization"/> directly, independent of a real write operation. Only
///     reachable indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c>
///     partial class.
/// </summary>
[TestClass]
public class WriteValueMaterializationTests
{
    /// <summary>A char array within the bound converts to the equivalent string.</summary>
    [TestMethod]
    public void ConvertToBoundedCharString_WithinBound_ReturnsTheString()
    {
        string result = WriteValueMaterialization.ConvertToBoundedCharString(new[] { 'a', 'b', 'c', }, 4, "label");

        Assert.AreEqual("abc", result);
    }

    /// <summary>A byte sequence within the bound converts each byte to its equivalent character.</summary>
    [TestMethod]
    public void ConvertToBoundedCharString_ByteSequence_ConvertsEachByte()
    {
        string result = WriteValueMaterialization.ConvertToBoundedCharString(new byte[] { 0x41, 0x42, }, 4, "label");

        Assert.AreEqual("AB", result);
    }

    /// <summary>A sequence longer than the fixed buffer cannot be consumed unboundedly.</summary>
    [TestMethod]
    public void ConvertToBoundedCharString_ExceedsMaximum_Throws()
    {
        Assert.Throws<CStructWriteException>(
            () => WriteValueMaterialization.ConvertToBoundedCharString("abcdef", 3, "label"));
    }

    /// <summary>An unsupported source shape cannot be converted to a character buffer.</summary>
    [TestMethod]
    public void ConvertToBoundedCharString_UnsupportedShape_Throws()
    {
        Assert.Throws<CStructWriteException>(
            () => WriteValueMaterialization.ConvertToBoundedCharString(42, 3, "label"));
    }

    /// <summary>An existing typed list within the bound is returned as-is.</summary>
    [TestMethod]
    public void ConvertToObjectList_ExistingListWithinBound_ReturnsSameShape()
    {
        var list = new List<object> { 1, 2, 3, };

        IList<object> result = WriteValueMaterialization.ConvertToObjectList(list, 5, "values");

        CollectionAssert.AreEqual(list, (System.Collections.ICollection)result);
    }

    /// <summary>An existing typed list beyond the bound is rejected before any further processing.</summary>
    [TestMethod]
    public void ConvertToObjectList_ExistingListExceedsBound_Throws()
    {
        var list = new List<object> { 1, 2, 3, };

        Assert.Throws<CStructWriteException>(() => WriteValueMaterialization.ConvertToObjectList(list, 2, "values"));
    }

    /// <summary>A plain CLR array within the bound converts to a boxed object list.</summary>
    [TestMethod]
    public void ConvertToObjectList_Array_ConvertsToObjectList()
    {
        int[] array = [10, 20, 30,];

        IList<object> result = WriteValueMaterialization.ConvertToObjectList(array, 5, "values");

        CollectionAssert.AreEqual(new object[] { 10, 20, 30, }, (System.Collections.ICollection)result);
    }

    /// <summary>An array beyond the bound is rejected before conversion.</summary>
    [TestMethod]
    public void ConvertToObjectList_ArrayExceedsBound_Throws()
    {
        int[] array = [10, 20, 30,];

        Assert.Throws<CStructWriteException>(() => WriteValueMaterialization.ConvertToObjectList(array, 2, "values"));
    }

    /// <summary>A single-pass enumerable within the bound is materialized in full.</summary>
    [TestMethod]
    public void ConvertToObjectList_Enumerable_MaterializesWithinBound()
    {
        IEnumerable<object> Source()
        {
            yield return 1;
            yield return 2;
        }

        IList<object> result = WriteValueMaterialization.ConvertToObjectList(Source(), 5, "values");

        CollectionAssert.AreEqual(new object[] { 1, 2, }, (System.Collections.ICollection)result);
    }

    /// <summary>An unbounded enumerable is consumed only up to one proof-of-overflow item beyond the permitted count.</summary>
    [TestMethod]
    public void ConvertToObjectList_UnboundedEnumerableExceedsBound_Throws()
    {
        static IEnumerable<object> Infinite()
        {
            int value = 0;
            while (true)
            {
                yield return value++;
            }
        }

        Assert.Throws<CStructWriteException>(() => WriteValueMaterialization.ConvertToObjectList(Infinite(), 3, "values"));
    }

    /// <summary>A value that is neither a list, array, nor enumerable cannot supply an array field.</summary>
    [TestMethod]
    public void ConvertToObjectList_UnsupportedShape_Throws()
    {
        Assert.Throws<CStructWriteException>(() => WriteValueMaterialization.ConvertToObjectList(42, 3, "values"));
    }
}
