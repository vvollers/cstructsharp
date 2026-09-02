namespace CStructSharp.Tests;

using System.Collections;
using System.Numerics;

/// <summary>Defines the checked CLR projection boundary layered over natural selected reads.</summary>
[TestClass]
public class TypedValueConversionTests
{
    private enum TestMode : byte
    {
        Value = 1,
    }

    /// <summary>Represents an interface that cannot be constructed as a projection target.</summary>
    private interface IModel
    {
        /// <summary>Gets or sets the value that would otherwise match the layout.</summary>
        byte Value { get; set; }
    }

    /// <summary>Converts every supported integral source and numeric target without culture or unchecked narrowing.</summary>
    [TestMethod]
    public void NumericProjection_CoversExactIntegralAndFloatingTargets()
    {
        const string byteLayout = "struct root { byte value; };";
        Assert.AreEqual((sbyte)42, Read<sbyte>(byteLayout, [42,]));
        Assert.AreEqual((short)42, Read<short>(byteLayout, [42,]));
        Assert.AreEqual(42U, Read<uint>(byteLayout, [42,]));
        Assert.AreEqual(42L, Read<long>(byteLayout, [42,]));
        Assert.AreEqual(42UL, Read<ulong>(byteLayout, [42,]));
        Assert.AreEqual(new BigInteger(42), Read<BigInteger>(byteLayout, [42,]));
        Assert.AreEqual(42F, Read<float>(byteLayout, [42,]));
        Assert.AreEqual(42D, Read<double>(byteLayout, [42,]));
        Assert.AreEqual(42M, Read<decimal>(byteLayout, [42,]));

        Assert.AreEqual(-1, Read<int>("struct root { int8 value; };", [0xFF,]));
        Assert.AreEqual(-2, Read<int>("struct root { int16 value; };", [0xFE, 0xFF,]));
        Assert.AreEqual(
            0x12345678L,
            Read<long>("struct root { int32 value; };", [0x78, 0x56, 0x34, 0x12,]));
        Assert.AreEqual(
            0xFEDCBA98L,
            Read<long>("struct root { uint32 value; };", [0x98, 0xBA, 0xDC, 0xFE,]));
        Assert.AreEqual(
            new BigInteger(-2),
            Read<BigInteger>(
                "struct root { int64 value; };",
                [0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,]));
        Assert.AreEqual(
            new BigInteger(ulong.MaxValue),
            Read<BigInteger>(
                "struct root { uint64 value; };",
                [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,]));

        const string enumLayout = "enum mode : uint64 { Maximum=18446744073709551615 };";
        var maximum = new BigInteger(ulong.MaxValue);
        Assert.AreEqual((float)maximum, Read<float>(enumLayout, EightOnes(), "mode"));
        Assert.AreEqual((double)maximum, Read<double>(enumLayout, EightOnes(), "mode"));
        Assert.AreEqual((decimal)ulong.MaxValue, Read<decimal>(enumLayout, EightOnes(), "mode"));
    }

    /// <summary>Supports direct object values and rejects null, non-collections, and nonnumeric enum projections.</summary>
    [TestMethod]
    public void Projection_RejectsIncompatibleNullCollectionAndEnumTargets()
    {
        var scalar = new CStruct("struct root { byte value; };");
        using (var stream = new MemoryStream([42,]))
        {
            Assert.AreEqual((byte)42, scalar.ReadValue<object>(stream, "root.value"));
        }

        var pointer = new CStruct("struct root { byte *target; };", pointerSize: 1);
        using (var stream = new MemoryStream([0,]))
        {
            CStructReadException nullError = Assert.Throws<CStructReadException>(
                () => pointer.ReadValue<int>(stream, "root.target.value"));
            Assert.AreEqual("root.target.value", nullError.Path);
        }

        var text = new CStruct("struct root { cstring value; };");
        using (var stream = new MemoryStream([0,]))
        {
            Assert.Throws<CStructReadException>(
                () => text.ReadValue<int[]>(stream, "root.value"));
        }

        using (var stream = new MemoryStream([0,]))
        {
            Assert.Throws<CStructReadException>(
                () => text.ReadValue<TestMode>(stream, "root.value"));
        }

        var array = new CStruct("struct root { byte values[1]; };");
        using (var stream = new MemoryStream([42,]))
        {
            Assert.Throws<CStructReadException>(
                () => array.ReadValue<ArrayList>(stream, "root.values"));
        }

        using (var stream = new MemoryStream([42,]))
        {
            Assert.Throws<CStructReadException>(
                () => array.ReadValue<HashSet<byte>>(stream, "root.values"));
        }
    }

    /// <summary>Maps exact dictionary names and normalizes every unsupported POCO shape or invocation failure.</summary>
    [TestMethod]
    public void PocoProjection_ReportsConstructionAndMemberContracts()
    {
        var exact = new CStruct("struct root { byte Value; };");
        using (var stream = new MemoryStream([42,]))
        {
            ExactModel model = exact.ReadValue<ExactModel>(stream, "root");
            Assert.AreEqual((byte)42, model.Value);
        }

        var union = new CStruct("union choice { uint16 Wide; byte Small; };");
        using (var stream = new MemoryStream([0x34, 0x12,]))
        {
            UnionProjection model = union.ReadValue<UnionProjection>(stream, "choice");
            Assert.AreEqual((ushort)0x1234, model.Wide);
        }

        AssertPocoFailure<IModel>(exact, "mutable reference-type");
        AssertPocoFailure<ValueModel>(exact, "mutable reference-type");
        AssertPocoFailure<AbstractModel>(exact, "mutable reference-type");
        AssertPocoFailure<NoPublicConstructor>(exact, "public parameterless constructor");
        AssertPocoFailure<NoWritableMembers>(exact, "no public writable");
        AssertPocoFailure<AmbiguousMembers>(exact, "ambiguous writable members");
        AssertPocoFailure<ThrowingConstructor>(exact, "Cannot map");
        AssertPocoFailure<ThrowingSetter>(exact, "Cannot map", "root.Value");
    }

    private static void AssertPocoFailure<T>(
        CStruct cstruct,
        string expectedMessage,
        string expectedPath = "root")
    {
        using var stream = new MemoryStream([42,]);
        CStructReadException error = Assert.Throws<CStructReadException>(
            () => cstruct.ReadValue<T>(stream, "root"));
        Assert.AreEqual(expectedPath, error.Path);
        StringAssert.Contains(error.Message, expectedMessage);
    }

    private static T Read<T>(string layout, byte[] bytes, string path = "root.value")
    {
        var cstruct = new CStruct(layout);
        using var stream = new MemoryStream(bytes);
        return cstruct.ReadValue<T>(stream, path);
    }

    private static byte[] EightOnes()
    {
        return [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,];
    }

    private struct ValueModel
    {
        public byte Value { get; set; }
    }

    private abstract class AbstractModel
    {
        public byte Value { get; set; }
    }

    private sealed class ExactModel
    {
        public byte Value { get; set; }
    }

    private sealed class UnionProjection
    {
        public ushort Wide { get; set; }
    }

    private sealed class NoPublicConstructor
    {
        private NoPublicConstructor()
        {
        }

        public byte Value { get; set; }
    }

    private sealed class NoWritableMembers
    {
        public byte Value => 0;
    }

    private sealed class AmbiguousMembers
    {
        public byte Value { get; set; }

        public byte VALUE { get; set; }
    }

    private sealed class ThrowingConstructor
    {
        public ThrowingConstructor()
        {
            throw new InvalidOperationException("constructor failure");
        }

        public byte Value { get; set; }
    }

    private sealed class ThrowingSetter
    {
        public byte Value
        {
            get => 0;
            set => throw new InvalidOperationException("setter failure");
        }
    }
}
