namespace CStructSharp.Tests;

using System.Collections.Immutable;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>
///     Exercises <see cref="EnumIntegerCodecTable"/> directly, independent of a compiled <see cref="CStruct"/>
///     layout. Only reachable indirectly through the public API before this type was extracted from the
///     God-Object <c>CStruct</c> partial class. <see cref="EnumIntegerCodec"/> itself already has thorough direct
///     coverage in <c>EnumDomainTests.cs</c>, so these tests focus on the table's own construction-time
///     resolution and lookup behavior.
/// </summary>
[TestClass]
public class EnumIntegerCodecTableTests
{
    /// <summary>An enum declared with a direct primitive storage spelling resolves to that codec.</summary>
    [TestMethod]
    public void Constructor_DirectStorageSpelling_ResolvesToThatCodec()
    {
        CstructEnum state = MakeEnum("state", "uint8");
        var table = new EnumIntegerCodecTable([state,], new Dictionary<string, CStructElement>());

        EnumIntegerCodec codec = table.Get("state");

        Assert.AreEqual("uint8", codec.StorageType);
        Assert.AreEqual(1, codec.SizeInBytes);
    }

    /// <summary>A storage type that is a typedef alias chain must resolve down to its final built-in spelling.</summary>
    [TestMethod]
    public void Constructor_TypedefChain_ResolvesToTheFinalBuiltInSpelling()
    {
        var cStructElements = new Dictionary<string, CStructElement>
        {
            ["word"] = new Typedef(new Identifier("word"), new Identifier("uint16")),
            ["flags_t"] = new Typedef(new Identifier("flags_t"), new Identifier("word")),
        };
        CstructEnum flags = MakeEnum("flags", "flags_t");

        var table = new EnumIntegerCodecTable([flags,], cStructElements);

        Assert.AreEqual("uint16", table.Get("flags").StorageType);
    }

    /// <summary>A typedef chain that refers back to itself must be rejected instead of recursing forever.</summary>
    [TestMethod]
    public void Constructor_CircularTypedefChain_Throws()
    {
        var cStructElements = new Dictionary<string, CStructElement>
        {
            ["a"] = new Typedef(new Identifier("a"), new Identifier("b")),
            ["b"] = new Typedef(new Identifier("b"), new Identifier("a")),
        };
        CstructEnum circular = MakeEnum("circular", "a");

        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new EnumIntegerCodecTable([circular,], cStructElements));
        StringAssert.Contains(exception.Message, "Circular typedef");
    }

    /// <summary>A pointer storage type can never be a valid enum backing type.</summary>
    [TestMethod]
    public void Constructor_PointerStorageType_Throws()
    {
        CstructEnum pointerBacked = MakeEnum("bad", "uint8*");

        Assert.Throws<CStructLayoutException>(
            () => new EnumIntegerCodecTable([pointerBacked,], new Dictionary<string, CStructElement>()));
    }

    /// <summary>A storage type name that never resolves to a known integral spelling must be rejected.</summary>
    [TestMethod]
    public void Constructor_UnresolvableStorageType_Throws()
    {
        CstructEnum unresolvable = MakeEnum("bad", "not_a_real_type");

        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => new EnumIntegerCodecTable([unresolvable,], new Dictionary<string, CStructElement>()));
        StringAssert.Contains(exception.Message, "must resolve to a scalar signed or unsigned");
    }

    /// <summary>A non-enum declaration in the same declaration list is simply ignored.</summary>
    [TestMethod]
    public void Constructor_NonEnumDeclarations_AreIgnored()
    {
        var typedef = new Typedef(new Identifier("alias"), new Identifier("uint8"));
        CstructEnum state = MakeEnum("state", "uint8");

        var table = new EnumIntegerCodecTable([typedef, state,], new Dictionary<string, CStructElement>());

        Assert.AreEqual("uint8", table.Get("state").StorageType);
    }

    /// <summary>Requesting a codec for a name that was never compiled is a validated failure, not a null result.</summary>
    [TestMethod]
    public void Get_UnknownEnumName_Throws()
    {
        var table = new EnumIntegerCodecTable([], new Dictionary<string, CStructElement>());

        Assert.Throws<CStructLayoutException>(() => table.Get("never_declared"));
    }

    private static CstructEnum MakeEnum(string name, string storageTypeName)
    {
        return new CstructEnum(new Identifier(name), ImmutableArray<EnumValue>.Empty, new Identifier(storageTypeName));
    }
}
