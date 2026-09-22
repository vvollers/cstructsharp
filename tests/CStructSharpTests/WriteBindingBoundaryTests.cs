namespace CStructSharp.Tests;

using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks null inputs, collection ownership and precise write-data conversion failures.</summary>
[TestClass]
public class WriteBindingBoundaryTests
{
    /// <summary>Null data remains null so the caller's compiled pointer or field policy can decide its validity.</summary>
    [TestMethod]
    public void Materialize_NullRemainsNull()
    {
        Assert.IsNull(WriteDataBinding.Materialize(null!, Composite()));
    }

    /// <summary>Collections containing only ordinary values are borrowed without rebuilding either dimension.</summary>
    [TestMethod]
    public void Materialize_PreservesUnchangedCollections()
    {
        MappedTypes.Register<Container>();
        var row = new object?[] { new Dictionary<string, object?> { ["value"] = (byte)7, }, null, };
        var items = new object[] { row, };
        var materialized = (StructValue)WriteDataBinding.Materialize(new Container { Items = items, }, Composite());
        Assert.AreSame(items, materialized["items"]);
        Assert.AreSame(row, ((object[])materialized["items"]!)[0]);
    }

    /// <summary>A mapped element inside a nested collection is converted before the writer reads any members.</summary>
    [TestMethod]
    public void Materialize_RebuildsNestedMappedCollections()
    {
        MappedTypes.Register<Container>();
        MappedTypes.Register<Item>();
        var row = new object[] { new Item { Value = 9, }, };
        var items = new object[] { row, };
        var materialized = (StructValue)WriteDataBinding.Materialize(new Container { Items = items, }, Composite());
        var rebuilt = (IList<object?>)materialized["items"]!;
        Assert.AreNotSame(items, rebuilt);
        var rebuiltRow = (IList<object?>)rebuilt[0]!;
        Assert.AreNotSame(row, rebuiltRow);
        Assert.AreEqual((byte)9, ((StructValue)rebuiltRow[0]!)["value"]);
        Assert.IsInstanceOfType<Item>(row[0], "Materialization must not replace the caller's original elements.");
    }

    /// <summary>A nullable value-type array takes the Array path and explains its missing element.</summary>
    [TestMethod]
    public void IndexedValue_NullArrayElementIsExplained()
    {
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => WriteDataBinding.GetIndexedValue(new int?[] { null, }, 0));
        Assert.AreEqual("Null array element.", failure.Message);
    }

    /// <summary>An unsupported indexed object reports its actual type instead of an empty diagnostic.</summary>
    [TestMethod]
    public void IndexedValue_UnsupportedTypeIsExplained()
    {
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => WriteDataBinding.GetIndexedValue(7, 0));
        Assert.AreEqual("Index not supported on value: Int32", failure.Message);
    }

    /// <summary>Enum conversion identifies the declared storage domain rather than leaking a generic cast failure.</summary>
    [TestMethod]
    public void EnumStorage_OverflowNamesItsDomain()
    {
        Assert.IsTrue(EnumIntegerCodec.TryCreate("uint8", out EnumIntegerCodec? codec));
        Assert.IsNotNull(codec);
        OverflowException failure = Assert.Throws<OverflowException>(() => codec.ToStorageValue(256));
        Assert.AreEqual("Value 256 is outside the uint8 enum domain.", failure.Message);
    }

    /// <summary>Builds metadata for a two-dimensional collection of single-byte records.</summary>
    /// <returns>The root composite used by the materializer.</returns>
    private static CompiledCompositeType Composite()
    {
        var layout = new CStruct("struct item { uint8 value; }; struct root { item items[1][2]; };");
        return (CompiledCompositeType)layout.CompiledModel.Composites[layout.GetStruct("root")].Definition!;
    }

    /// <summary>A hand-written mapper that deliberately leaves nested collection conversion to the binding layer.</summary>
    private sealed class Container : ICStructMapped<Container>
    {
        public object Items { get; init; } = Array.Empty<object>();

        /// <summary>Retains the parsed collection as the mapper's collection.</summary>
        /// <param name="source">The parsed root.</param>
        /// <returns>A container borrowing its items.</returns>
        public static Container ReadFrom(StructValue source) => new() { Items = source["items"]!, };

        /// <summary>Passes the caller's collection to the binding layer without changing it.</summary>
        /// <param name="value">The caller's container.</param>
        /// <param name="target">The root being materialized.</param>
        public static void WriteTo(Container value, StructValue target) => target["items"] = value.Items;
    }

    /// <summary>A nested record mapper whose conversion must occur inside the collection traversal.</summary>
    private sealed class Item : ICStructMapped<Item>
    {
        public byte Value { get; init; }

        /// <summary>Reads the sole byte member.</summary>
        /// <param name="source">The parsed item.</param>
        /// <returns>The mapped item.</returns>
        public static Item ReadFrom(StructValue source) => new() { Value = source.Get<byte>("value"), };

        /// <summary>Writes the sole byte member.</summary>
        /// <param name="value">The caller's item.</param>
        /// <param name="target">The item being materialized.</param>
        public static void WriteTo(Item value, StructValue target) => target["value"] = value.Value;
    }
}
