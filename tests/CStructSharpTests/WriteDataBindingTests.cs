namespace CStructSharpTests;

using System.Collections.Generic;
using System.Dynamic;
using System.Runtime.CompilerServices;
using CStructSharp;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The data a write operation reads members from: struct values, dictionaries, expando objects, and mapped
///     classes registered through <see cref="MappedTypes"/>. There is no reflection: an object of any other shape
///     is reported as not writable.
/// </summary>
[TestClass]
public class WriteDataBindingTests
{
    /// <summary>A null root is returned unchanged; the compiled field decides whether null is valid.</summary>
    [TestMethod]
    public void NormalizeRootData_NullData_ReturnsNull()
    {
        Assert.IsNull(WriteDataBinding.NormalizeRootData(null!, "root"));
    }

    /// <summary>A wrapper dictionary containing the root under its layout name unwraps to the inner value.</summary>
    [TestMethod]
    public void NormalizeRootData_WrapperContainsRootName_ReturnsInnerValue()
    {
        var inner = new Dictionary<string, object?> { ["value"] = 1, };
        var wrapper = new Dictionary<string, object?> { ["root"] = inner, };

        Assert.AreSame(inner, WriteDataBinding.NormalizeRootData(wrapper, "root"));
    }

    /// <summary>Data with no matching member is treated as the root value itself.</summary>
    [TestMethod]
    public void NormalizeRootData_NoMatchingMember_ReturnsOriginalData()
    {
        var data = new Dictionary<string, object?> { ["value"] = 1, };

        Assert.AreSame(data, WriteDataBinding.NormalizeRootData(data, "root"));
    }

    /// <summary>An ExpandoObject member is read as a dictionary key.</summary>
    [TestMethod]
    public void TryGetMemberValue_ExpandoObject_ReadsDictionaryKey()
    {
        IDictionary<string, object?> expando = new ExpandoObject();
        expando["value"] = 7;

        Assert.IsTrue(WriteDataBinding.TryGetMemberValue(expando, "value", out object found));
        Assert.AreEqual(7, found);
        Assert.IsFalse(WriteDataBinding.TryGetMemberValue(expando, "other", out _));
    }

    /// <summary>A plain dictionary and a parsed struct value participate through the same named-lookup contract.</summary>
    [TestMethod]
    public void TryGetMemberValue_DictionaryAndStructValue_ReadKey()
    {
        var dictionary = new Dictionary<string, object> { ["value"] = 9, };
        Assert.IsTrue(WriteDataBinding.TryGetMemberValue(dictionary, "value", out object found));
        Assert.AreEqual(9, found);

        StructValue parsed = new CStruct("struct root { uint8 value; };").Parse(new byte[] { 3 }, "root");
        Assert.IsTrue(WriteDataBinding.TryGetMemberValue(parsed, "value", out object member));
        Assert.AreEqual((byte)3, member);
    }

    /// <summary>An arbitrary object is not writable data: no member is found and the error names the accepted shapes.</summary>
    [TestMethod]
    public void TryGetMemberValue_UnregisteredObject_IsNotWritable()
    {
        var plain = new NotMapped { Value = 1, };

        Assert.IsFalse(WriteDataBinding.TryGetMemberValue(plain, "Value", out _));
        Assert.IsFalse(WriteDataBinding.IsWritable(plain));
        CStructWriteException error = Assert.Throws<CStructWriteException>(() => WriteDataBinding.GetMemberValueOrThrow(plain, "value"));
        StringAssert.Contains(error.Message, "No value was supplied for 'value'");
        StringAssert.Contains(error.Message, "ICStructMapped<T>");
    }

    /// <summary>A required member that is missing from a dictionary reports which layout field could not be found.</summary>
    [TestMethod]
    public void GetMemberValueOrThrow_MemberMissing_Throws()
    {
        CStructWriteException error = Assert.Throws<CStructWriteException>(
            () => WriteDataBinding.GetMemberValueOrThrow(new Dictionary<string, object?>(), "length"));

        Assert.AreEqual("No value was supplied for 'length'.", error.Message);
    }

    /// <summary>An indexed list, array, and string each yield their element at the requested position.</summary>
    [TestMethod]
    public void GetIndexedValue_SupportedShapes_ReturnsElement()
    {
        Assert.AreEqual(2, WriteDataBinding.GetIndexedValue(new List<object> { 1, 2, }, 1));
        Assert.AreEqual(5, WriteDataBinding.GetIndexedValue(new int[] { 4, 5, }, 1));
        Assert.AreEqual('b', WriteDataBinding.GetIndexedValue("ab", 1));
        Assert.Throws<CStructWriteException>(() => WriteDataBinding.GetIndexedValue(new object(), 0));
    }

    /// <summary>A multi-segment path walks named members and array indexes in sequence.</summary>
    [TestMethod]
    public void ResolveDataPath_NamedAndIndexedSegments_WalksToTheTarget()
    {
        var data = new Dictionary<string, object?>
        {
            ["items"] = new List<object> { new Dictionary<string, object?> { ["tag"] = 1, }, new Dictionary<string, object?> { ["tag"] = 2, }, },
        };
        IReadOnlyList<PathSegment> segments = CStructPathResolver.Parse("items[1].tag");

        Assert.AreEqual(2, WriteDataBinding.ResolveDataPath(data, segments));
    }

    /// <summary>
    ///     A registered mapped class is materialized into a struct value of the composite's shape through its own
    ///     WriteTo, so the writer reads plain members; the registry answers by type, not by reflection.
    /// </summary>
    [TestMethod]
    public void Materialize_MappedClass_FillsAStructValueOfTheCompositeShape()
    {
        var layout = new CStruct("struct point { int16 x; int16 y; };");
        CompiledCompositeType composite = (CompiledCompositeType)layout.CompiledModel.Composites[layout.GetStruct("point")].Definition!;

        object materialized = WriteDataBinding.Materialize(new Point { X = -2, Y = 5, }, composite);

        var value = (StructValue)materialized;
        Assert.AreEqual((short)-2, value["x"]);
        Assert.AreEqual((short)5, value["y"]);
        Assert.IsTrue(MappedTypes.IsMapped(typeof(Point)));
        CollectionAssert.AreEqual(new byte[] { 0xFE, 0xFF, 0x05, 0x00, }, layout.Serialize("point", new Point { X = -2, Y = 5, }));
        object untouched = WriteDataBinding.Materialize(new Dictionary<string, object?>(), composite);
        Assert.IsInstanceOfType(untouched, typeof(Dictionary<string, object?>));
    }

    /// <summary>A hand-written mapped class, registered when first used.</summary>
    internal sealed class Point : ICStructMapped<Point>
    {
        public short X { get; set; }

        public short Y { get; set; }

        public static Point ReadFrom(StructValue source)
        {
            return new Point { X = source.Get<short>("x"), Y = source.Get<short>("y"), };
        }

        public static void WriteTo(Point value, StructValue target)
        {
            target["x"] = value.X;
            target["y"] = value.Y;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Point>();
        }
    }

    private sealed class NotMapped
    {
        public int Value { get; set; }
    }
}
