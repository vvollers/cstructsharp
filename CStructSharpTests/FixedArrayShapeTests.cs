namespace CStructSharpTests;

using System.Dynamic;
using System.Numerics;
using CStructSharp;

/// <summary>
///     Verifies that fixed-array declarations retain their collection shape and byte layout for zero, one, and
///     multiple elements across every supported element category.
/// </summary>
[TestClass]
public class FixedArrayShapeTests
{
    /// <summary>
    ///     Keeps explicit fixed arrays collection-shaped even when their declared count is zero or one, while a
    ///     following sentinel proves that neither case consumes an incorrect number of bytes.
    /// </summary>
    [TestMethod]
    public void FixedPrimitiveArrays_KeepShapeForZeroAndOneElements()
    {
        var zero = new CStruct("struct root { byte values[0]; byte tail; };");
        using var zeroStream = new MemoryStream([0xA5,]);
        dynamic zeroParsed = zero.ParseStream(zeroStream, "root");

        Assert.AreEqual(0, ((IList<object>)zeroParsed.values).Count);
        Assert.AreEqual((byte)0xA5, (byte)zeroParsed.tail);

        dynamic zeroData = new ExpandoObject();
        zeroData.values = new List<object>();
        zeroData.tail = (byte)0xA5;
        CollectionAssert.AreEqual(new byte[] { 0xA5, }, zero.Serialize("root", zeroData));

        var one = new CStruct("struct root { byte values[1]; byte tail; };");
        using var oneStream = new MemoryStream([0x2A, 0xA5,]);
        dynamic oneParsed = one.ParseStream(oneStream, "root");

        Assert.AreEqual(1, ((IList<object>)oneParsed.values).Count);
        Assert.AreEqual((byte)0x2A, (byte)oneParsed.values[0]);
        Assert.AreEqual((byte)0xA5, (byte)oneParsed.tail);
        CollectionAssert.AreEqual(new byte[] { 0x2A, 0xA5, }, one.Serialize("root", oneParsed));
    }

    /// <summary>
    ///     Applies the same zero/one policy to fixed character buffers and nested-struct arrays, which use separate
    ///     conversion and recursive layout paths internally.
    /// </summary>
    [TestMethod]
    public void FixedCharacterAndNestedArrays_KeepDeclaredShape()
    {
        var chars = new CStruct("struct root { char empty[0]; char one[1]; byte tail; };");
        using var charStream = new MemoryStream([(byte)'Q', 0xA5,]);
        dynamic charParsed = chars.ParseStream(charStream, "root");

        Assert.AreEqual(string.Empty, (string)charParsed.empty);
        Assert.AreEqual("Q", (string)charParsed.one);
        Assert.AreEqual((byte)0xA5, (byte)charParsed.tail);
        CollectionAssert.AreEqual(new byte[] { (byte)'Q', 0xA5, }, chars.Serialize("root", charParsed));

        var nested = new CStruct("struct item { byte value; }; struct root { item items[1]; byte tail; };");
        using var nestedStream = new MemoryStream([0x2A, 0xA5,]);
        dynamic nestedParsed = nested.ParseStream(nestedStream, "root");

        Assert.AreEqual(1, ((IList<object>)nestedParsed.items).Count);
        Assert.AreEqual((byte)0x2A, (byte)nestedParsed.items[0].value);
        Assert.AreEqual((byte)0xA5, (byte)nestedParsed.tail);
        CollectionAssert.AreEqual(new byte[] { 0x2A, 0xA5, }, nested.Serialize("root", nestedParsed));
    }

    /// <summary>
    ///     Cross-checks the complete zero/one/two fixed-array matrix for character buffers, enums, and nested structs.
    ///     For every shape, the declared count must agree across parsing, serialized length, public size, tail address,
    ///     and round-trip bytes.
    /// </summary>
    [TestMethod]
    public void FixedArrays_ZeroOneTwoMatrixAgreesAcrossOperations()
    {
        for (int count = 0; count <= 2; count++)
        {
            var primitives = new CStruct($"struct root {{ byte values[{count}]; byte tail; }};");
            byte[] primitiveBytes = [.. Enumerable.Range(0, count).Select(index => (byte)(index + 1)), 0xA5,];
            using var primitiveStream = new MemoryStream(primitiveBytes);
            dynamic primitiveResult = primitives.ParseStream(primitiveStream, "root");
            Assert.AreEqual(count, ((IList<object>)primitiveResult.values).Count);
            CollectionAssert.AreEqual(
                Enumerable.Range(0, count).Select(index => (object)(byte)(index + 1)).ToList(),
                ((IList<object>)primitiveResult.values).ToList());
            Assert.AreEqual((byte)0xA5, (byte)primitiveResult.tail);
            Assert.AreEqual(count + 1, primitives.GetStructSizeInBytes("root"));
            primitiveStream.Position = 0;
            Assert.AreEqual(count, primitives.ResolveAddress(primitiveStream, "root.tail"));
            primitiveStream.Position = 0;
            (List<DebugData> primitiveDebug, _) = primitives.ParseStreamWithDebug(primitiveStream, "root");
            Assert.AreEqual(count, primitiveDebug.Single(item => item.DebugStackString == "root.tail").CurPos);
            CollectionAssert.AreEqual(primitiveBytes, primitives.Serialize("root", primitiveResult));

            var characters = new CStruct($"struct root {{ char values[{count}]; byte tail; }};");
            byte[] characterBytes = [.. Enumerable.Range(0, count).Select(index => (byte)('A' + index)), 0xA5,];
            using var characterStream = new MemoryStream(characterBytes);
            dynamic characterResult = characters.ParseStream(characterStream, "root");
            Assert.AreEqual(new string(Enumerable.Range(0, count).Select(index => (char)('A' + index)).ToArray()), characterResult.values);
            Assert.AreEqual((byte)0xA5, (byte)characterResult.tail);
            Assert.AreEqual(count + 1, characters.GetStructSizeInBytes("root"));
            characterStream.Position = 0;
            Assert.AreEqual(count, characters.ResolveAddress(characterStream, "root.tail"));
            characterStream.Position = 0;
            (List<DebugData> characterDebug, _) = characters.ParseStreamWithDebug(characterStream, "root");
            Assert.AreEqual(count, characterDebug.Single(item => item.DebugStackString == "root.tail").CurPos);
            CollectionAssert.AreEqual(characterBytes, characters.Serialize("root", characterResult));

            const string enumPrefix = "enum kind : byte { Zero, One, Two };";
            var enums = new CStruct(enumPrefix + $" struct root {{ kind values[{count}]; byte tail; }};");
            byte[] enumBytes = [.. Enumerable.Range(0, count).Select(index => (byte)index), 0xA5,];
            using var enumStream = new MemoryStream(enumBytes);
            dynamic enumResult = enums.ParseStream(enumStream, "root");
            Assert.AreEqual(count, ((IList<object>)enumResult.values).Count);
            CollectionAssert.AreEqual(
                Enumerable.Range(0, count).Select(value => new BigInteger(value)).ToList(),
                ((IList<object>)enumResult.values).Cast<EnumValueResult>().Select(value => value.Value).ToList());
            Assert.AreEqual((byte)0xA5, (byte)enumResult.tail);
            Assert.AreEqual(count + 1, enums.GetStructSizeInBytes("root"));
            enumStream.Position = 0;
            Assert.AreEqual(count, enums.ResolveAddress(enumStream, "root.tail"));
            enumStream.Position = 0;
            (List<DebugData> enumDebug, _) = enums.ParseStreamWithDebug(enumStream, "root");
            Assert.AreEqual(count, enumDebug.Single(item => item.DebugStackString == "root.tail").CurPos);
            CollectionAssert.AreEqual(enumBytes, enums.Serialize("root", enumResult));

            var nested = new CStruct(
                "struct item { byte value; };" +
                $" struct root {{ item values[{count}]; byte tail; }};");
            byte[] nestedBytes = [.. Enumerable.Range(0, count).Select(index => (byte)(index + 1)), 0xA5,];
            using var nestedStream = new MemoryStream(nestedBytes);
            dynamic nestedResult = nested.ParseStream(nestedStream, "root");
            Assert.AreEqual(count, ((IList<object>)nestedResult.values).Count);
            CollectionAssert.AreEqual(
                Enumerable.Range(0, count).Select(index => (object)(byte)(index + 1)).ToList(),
                ((IList<object>)nestedResult.values).Select(item => (object)(byte)((dynamic)item).value).ToList());
            Assert.AreEqual((byte)0xA5, (byte)nestedResult.tail);
            Assert.AreEqual(count + 1, nested.GetStructSizeInBytes("root"));
            nestedStream.Position = 0;
            Assert.AreEqual(count, nested.ResolveAddress(nestedStream, "root.tail"));
            nestedStream.Position = 0;
            (List<DebugData> nestedDebug, _) = nested.ParseStreamWithDebug(nestedStream, "root");
            Assert.AreEqual(count, nestedDebug.Single(item => item.DebugStackString == "root.tail").CurPos);
            CollectionAssert.AreEqual(nestedBytes, nested.Serialize("root", nestedResult));
        }
    }
}
