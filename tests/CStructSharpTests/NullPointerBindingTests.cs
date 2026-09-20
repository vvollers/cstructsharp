namespace CStructSharpTests;

using System.Dynamic;
using System.Runtime.CompilerServices;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Requires null pointer values to behave identically across supported caller data shapes.</summary>
[TestClass]
public class NullPointerBindingTests
{
    /// <summary>
    ///     A null pointer followed by tail = 0xA5 must encode as 00 00 A5 with a two-byte pointer width.
    /// </summary>
    /// <remarks>
    ///     Dictionaries, dynamic objects, ordinary C# members, and JSON-shaped inputs must agree. Null is a valid
    ///     address-zero value when the declared member is a pointer.
    /// </remarks>
    [TestMethod]
    public void Serialize_NullPointerMembers_AgreeAcrossBindingShapes()
    {
        const string layout = "struct root { uint16 *ptr; byte tail; };";
        var cstruct = new CStruct(layout, pointerSize: 2);

        foreach ((string name, object value) in NullPointerShapes())
        {
            byte[] serialized = cstruct.Serialize("root", value);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0xA5, }, serialized, name + "/serialize");

            using var stream = new MemoryStream();
            cstruct.Write(stream, "root", value);
            CollectionAssert.AreEqual(new byte[] { 0, 0, 0xA5, }, stream.ToArray(), name + "/write");
        }

        // A mapped class that reports the pointer as null writes the same zero address.
        var mapped = new NullablePointerProperty { Ptr = null, Tail = 0xA5, };
        CollectionAssert.AreEqual(new byte[] { 0, 0, 0xA5, }, cstruct.Serialize("root", mapped));
    }

    /// <summary>
    ///     Null must encode as zero for selected pointer slots, pointer array elements, in-place address updates, and
    ///     pointer typedef roots.
    /// </summary>
    /// <remarks>
    ///     Clearing an address must not clear the former target bytes. These cases check that null support does not
    ///     depend on wrapping the pointer in a complete struct.
    /// </remarks>
    [TestMethod]
    public void SelectedAndRootPointerWrites_AcceptNull()
    {
        var cstruct = new CStruct("struct root { uint8 *ptr; byte tail; };", pointerSize: 1);
        var data = new NullablePointerProperty { Ptr = null, Tail = 0xA5, };
        using var selected = new MemoryStream();

        cstruct.Write(selected, "root.ptr", data);

        CollectionAssert.AreEqual(new byte[] { 0, }, selected.ToArray());

        using var update = new MemoryStream([0x04, 0xA5, 0, 0, 0, 0x2A,]);
        cstruct.Update(update, "root.ptr.address", null!);
        CollectionAssert.AreEqual(new byte[] { 0, 0xA5, 0, 0, 0, 0x2A, }, update.ToArray());
        Assert.AreEqual(0L, update.Position);

        foreach (bool isLittleEndian in new[] { true, false, })
        {
            foreach (byte pointerSize in new byte[] { 1, 2, 4, 8, })
            {
                var rootPointer = new CStruct(
                    "typedef uint8 *link;",
                    pointerSize,
                    isLittleEndian: isLittleEndian);
                CollectionAssert.AreEqual(
                    new byte[pointerSize],
                    rootPointer.Serialize("link", null!),
                    $"{(isLittleEndian ? "little" : "big")}/{pointerSize}");
            }
        }

        var pointerArray = new CStruct("struct root { uint8 *items[2]; };", pointerSize: 2);
        var pointerArrayData = new Dictionary<string, object?>
        {
            ["items"] = new object?[] { null, 0x0102L, },
        };
        CollectionAssert.AreEqual(
            new byte[] { 0, 0, 2, 1, },
            pointerArray.Serialize("root", pointerArrayData));
        CollectionAssert.AreEqual(
            new byte[] { 0, 0, },
            pointerArray.Serialize("root.items[0]", null!));
    }

    /// <summary>
    ///     An ordinary numeric field, struct, union, or whole array needs a value and must not silently treat null as
    ///     zero or empty data.
    /// </summary>
    /// <remarks>
    ///     All tested binding shapes must reject it. Direct selected writes must emit nothing, and failed updates must
    ///     preserve existing bytes and position.
    /// </remarks>
    [TestMethod]
    public void NonPointerNulls_FailConsistentlyWithoutWritingTheSelectedField()
    {
        var cstruct = new CStruct("struct root { byte value; };");

        foreach ((string name, object value) in NullNonPointerShapes())
        {
            Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", value), name + "/serialize");

            using var stream = new MemoryStream();
            Assert.Throws<CStructWriteException>(() => cstruct.Write(stream, "root", value), name + "/write");
            CollectionAssert.AreEqual(Array.Empty<byte>(), stream.ToArray(), name + "/unchanged");

            using var selected = new MemoryStream();
            Assert.Throws<CStructWriteException>(
                () => cstruct.Write(selected, "root.value", value),
                name + "/selected");
            CollectionAssert.AreEqual(Array.Empty<byte>(), selected.ToArray(), name + "/selected-unchanged");
        }

        using var update = new MemoryStream([0xA5,]) { Position = 1, };
        Assert.Throws<CStructWriteException>(() => cstruct.Update(update, "root.value", null!));
        CollectionAssert.AreEqual(new byte[] { 0xA5, }, update.ToArray());
        Assert.AreEqual(1L, update.Position);

        var rootPrimitive = new CStruct("typedef uint8 scalar;");
        Assert.Throws<CStructWriteException>(() => rootPrimitive.Serialize("scalar", null!));
        Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", null!));

        var rootUnion = new CStruct("union root { byte small; uint16 large; };");
        Assert.Throws<CStructWriteException>(() => rootUnion.Serialize("root", null!));

        var nestedStruct = new CStruct("struct child { byte value; }; struct root { child item; };");
        Assert.Throws<CStructWriteException>(
            () => nestedStruct.Serialize(
                "root",
                new Dictionary<string, object?> { ["item"] = null, }));

        var pointerArray = new CStruct("struct root { uint8 *items[1]; };");
        Assert.Throws<CStructWriteException>(
            () => pointerArray.Serialize(
                "root",
                new Dictionary<string, object?> { ["items"] = null, }));
    }

    private static IEnumerable<(string Name, object Value)> NullPointerShapes()
    {
        yield return ("mapped-class", new NullablePointerProperty { Ptr = null, Tail = 0xA5, });
        yield return (
            "dictionary",
            new Dictionary<string, object?> { ["ptr"] = null, ["tail"] = (byte)0xA5, });
        yield return ("expando", CreateExpando("ptr", null, "tail", (byte)0xA5));
        yield return ("json-shaped-expando", CreateExpando("ptr", null, "tail", 165L));
    }

    private static IEnumerable<(string Name, object Value)> NullNonPointerShapes()
    {
        yield return ("mapped-class", new NullablePrimitiveProperty { Value = null, });
        yield return ("dictionary", new Dictionary<string, object?> { ["value"] = null, });
        yield return ("expando", CreateExpando("value", null));
    }

    private static ExpandoObject CreateExpando(params object?[] namesAndValues)
    {
        IDictionary<string, object?> result = new ExpandoObject();
        for (int index = 0; index < namesAndValues.Length; index += 2)
        {
            result.Add((string)namesAndValues[index]!, namesAndValues[index + 1]);
        }

        return (ExpandoObject)result;
    }

    /// <summary>A hand-written mapped class (what the generator emits for a <c>[CStructMapped]</c> class).</summary>
    internal sealed class NullablePointerProperty : ICStructMapped<NullablePointerProperty>
    {
        public long? Ptr { get; set; }

        public byte Tail { get; set; }

        public static NullablePointerProperty ReadFrom(StructValue source)
        {
            return new NullablePointerProperty { Ptr = source.Get<long?>("ptr"), Tail = source.Get<byte>("tail"), };
        }

        public static void WriteTo(NullablePointerProperty value, StructValue target)
        {
            target["ptr"] = value.Ptr;
            target["tail"] = value.Tail;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NullablePointerProperty>();
        }
    }

    /// <summary>A mapped class whose one member is a nullable primitive.</summary>
    internal sealed class NullablePrimitiveProperty : ICStructMapped<NullablePrimitiveProperty>
    {
        public byte? Value { get; set; }

        public static NullablePrimitiveProperty ReadFrom(StructValue source)
        {
            return new NullablePrimitiveProperty { Value = source.Get<byte?>("value"), };
        }

        public static void WriteTo(NullablePrimitiveProperty value, StructValue target)
        {
            target["value"] = value.Value;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<NullablePrimitiveProperty>();
        }
    }
}
