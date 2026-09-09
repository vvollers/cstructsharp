namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>
///     Exercises <see cref="PocoDataBinding"/> directly, independent of a compiled <see cref="CStruct"/> layout.
///     Only reachable indirectly through the public API before this type was extracted from the God-Object
///     <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class PocoDataBindingTests
{
    /// <summary>A null root is returned unchanged; the compiled field decides whether null is valid.</summary>
    [TestMethod]
    public void NormalizeRootData_NullData_ReturnsNull()
    {
        Assert.IsNull(PocoDataBinding.NormalizeRootData(null!, "root", PocoBindingMode.PublicReadable));
    }

    /// <summary>A wrapper object containing the root under its layout name unwraps to the inner value.</summary>
    [TestMethod]
    public void NormalizeRootData_WrapperContainsRootName_ReturnsInnerValue()
    {
        dynamic wrapper = new ExpandoObject();
        wrapper.root = 123;

        object result = PocoDataBinding.NormalizeRootData(wrapper, "root", PocoBindingMode.PublicReadable);

        Assert.AreEqual(123, result);
    }

    /// <summary>An object with no matching member is treated as the root value itself.</summary>
    [TestMethod]
    public void NormalizeRootData_NoMatchingMember_ReturnsOriginalData()
    {
        var sample = new Sample();

        object result = PocoDataBinding.NormalizeRootData(sample, "root", PocoBindingMode.PublicReadable);

        Assert.AreSame(sample, result);
    }

    /// <summary>An ExpandoObject member is read as a dictionary key.</summary>
    [TestMethod]
    public void TryGetMemberValue_ExpandoObject_ReadsDictionaryKey()
    {
        dynamic data = new ExpandoObject();
        data.name = "value";

        Assert.IsTrue(PocoDataBinding.TryGetMemberValue(data, "name", PocoBindingMode.PublicReadable, out object value));
        Assert.AreEqual("value", value);
    }

    /// <summary>A plain dictionary participates through the same named-lookup contract.</summary>
    [TestMethod]
    public void TryGetMemberValue_Dictionary_ReadsKey()
    {
        var data = new Dictionary<string, object> { ["name"] = "value", };

        Assert.IsTrue(PocoDataBinding.TryGetMemberValue(data, "name", PocoBindingMode.PublicReadable, out object value));
        Assert.AreEqual("value", value);
    }

    /// <summary>A read-only property is visible under the permissive binding mode.</summary>
    [TestMethod]
    public void TryGetMemberValue_ReadOnlyProperty_PublicReadable_Found()
    {
        var sample = new Sample();

        Assert.IsTrue(
            PocoDataBinding.TryGetMemberValue(sample, "ReadOnlyValue", PocoBindingMode.PublicReadable, out object value));
        Assert.AreEqual(42, value);
    }

    /// <summary>A read-only property is rejected under the stricter read/write binding mode.</summary>
    [TestMethod]
    public void TryGetMemberValue_ReadOnlyProperty_PublicReadWrite_NotFound()
    {
        var sample = new Sample();

        Assert.IsFalse(
            PocoDataBinding.TryGetMemberValue(sample, "ReadOnlyValue", PocoBindingMode.PublicReadWrite, out _));
    }

    /// <summary>A public field is used only when no matching property exists.</summary>
    [TestMethod]
    public void TryGetMemberValue_PublicField_Found()
    {
        var sample = new Sample();

        Assert.IsTrue(
            PocoDataBinding.TryGetMemberValue(sample, "PublicField", PocoBindingMode.PublicReadable, out object value));
        Assert.AreEqual(9, value);
    }

    /// <summary>An unknown member name is reported as not found rather than throwing.</summary>
    [TestMethod]
    public void TryGetMemberValue_UnknownMember_ReturnsFalse()
    {
        var sample = new Sample();

        Assert.IsFalse(PocoDataBinding.TryGetMemberValue(sample, "DoesNotExist", PocoBindingMode.PublicReadable, out _));
    }

    /// <summary>A required member that is present is returned directly.</summary>
    [TestMethod]
    public void GetMemberValueOrThrow_MemberPresent_ReturnsValue()
    {
        var sample = new Sample();

        object result = PocoDataBinding.GetMemberValueOrThrow(sample, "ReadWriteValue", PocoBindingMode.PublicReadable);

        Assert.AreEqual(7, result);
    }

    /// <summary>A required member that is missing reports which layout field could not be found.</summary>
    [TestMethod]
    public void GetMemberValueOrThrow_MemberMissing_Throws()
    {
        var sample = new Sample();

        Assert.Throws<CStructWriteException>(
            () => PocoDataBinding.GetMemberValueOrThrow(sample, "Missing", PocoBindingMode.PublicReadable));
    }

    /// <summary>An indexed list, array, and string each yield their element at the requested position.</summary>
    [TestMethod]
    public void GetIndexedValue_SupportedShapes_ReturnsElement()
    {
        Assert.AreEqual(2, PocoDataBinding.GetIndexedValue(new List<object> { 1, 2, 3, }, 1));
        Assert.AreEqual(30, PocoDataBinding.GetIndexedValue(new[] { 10, 20, 30, }, 2));
        Assert.AreEqual('b', PocoDataBinding.GetIndexedValue("abc", 1));
    }

    /// <summary>An unsupported value shape cannot be indexed.</summary>
    [TestMethod]
    public void GetIndexedValue_UnsupportedShape_Throws()
    {
        Assert.Throws<CStructWriteException>(() => PocoDataBinding.GetIndexedValue(42, 0));
    }

    /// <summary>A multi-segment path walks named members and array indexes in sequence.</summary>
    [TestMethod]
    public void ResolveDataPath_NamedAndIndexedSegments_WalksToTheTarget()
    {
        dynamic inner = new ExpandoObject();
        inner.values = new List<object> { 10, 20, 30, };
        dynamic dynamicRoot = new ExpandoObject();
        dynamicRoot.inner = inner;
        object root = dynamicRoot;

        object result = PocoDataBinding.ResolveDataPath(
            root,
            [new PathSegment("inner", []), new PathSegment("values", [2]),],
            PocoBindingMode.PublicReadable);

        Assert.AreEqual(30, result);
    }

#pragma warning disable SA1401 // Public test fixture fields intentionally exercise field binding.
    private sealed class Sample
    {
        public int PublicField = 9;

        public int ReadOnlyValue => 42;

        public int ReadWriteValue { get; set; } = 7;
    }
#pragma warning restore SA1401
}
