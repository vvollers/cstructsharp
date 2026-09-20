namespace CStructSharp.Tests;

using System.Runtime.CompilerServices;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     <see cref="StructValue.Get{T}"/> / <see cref="StructValue.TryGet{T}"/> (and the union equivalents) read a
///     member or a path below it with the same checked conversion as <c>ReadValue&lt;T&gt;</c>, so application code
///     can stay typed without a second read of the input.
/// </summary>
[TestClass]
public class TypedGetTests
{
    private const string Layout = """
        struct inner { uint8 a; uint16 b; };
        union choice { uint8 small; uint16 wide; };
        struct root { uint16 kind; inner nested; inner items[2]; choice pick; uint8 *target; char name[4]; };
        """;

    private static readonly byte[] Bytes =
    [
        0x03, 0x00, // kind
        0x11, 0x22, 0x00, // nested
        0x01, 0x10, 0x00, 0x02, 0x20, 0x00, // items
        0x34, 0x12, // pick
        0x12, // target -> offset 18
        (byte)'a', (byte)'b', 0, 0, // name
        0x2A, // pointee
    ];

    private static StructValue ParseRoot()
    {
        return new CStruct(Layout, pointerSize: 1, aligned: false).Parse(Bytes, "root");
    }

    /// <summary>A member name converts to a scalar with the checked rules (widening allowed, narrowing checked).</summary>
    [TestMethod]
    public void Get_MemberName_ConvertsLikeReadValue()
    {
        StructValue root = ParseRoot();
        Assert.AreEqual((ushort)3, root.Get<ushort>("kind"));
        Assert.AreEqual(3, root.Get<int>("kind"));
        Assert.AreEqual(3L, root.Get<long>("kind"));
        Assert.AreEqual("ab\0\0", root.Get<string>("name"));
        Assert.AreEqual((byte)3, root.Get<byte>("kind"));
        CStructReadException failure = Assert.Throws<CStructReadException>(() => root.Get<sbyte>("pick.wide"));
        StringAssert.Contains(failure.Message, "pick.wide");
    }

    /// <summary>Dotted and indexed paths walk nested structs, arrays, unions, and dereferenced pointers.</summary>
    [TestMethod]
    public void Get_Path_WalksStructsArraysUnionsAndPointers()
    {
        StructValue root = ParseRoot();
        Assert.AreEqual((ushort)0x22, root.Get<ushort>("nested.b"));
        Assert.AreEqual((byte)0x02, root.Get<byte>("items[1].a"));
        Assert.AreEqual((ushort)0x20, root.Get<ushort>("items[1].b"));
        Assert.AreEqual((byte)0x34, root.Get<byte>("pick.small"));
        Assert.AreEqual((ushort)0x1234, root.Get<ushort>("pick.wide"));
        Assert.AreEqual((byte)0x2A, root.Get<byte>("target.value"));
        Assert.AreEqual(18L, root.Get<long>("target.address"));
        Assert.AreEqual('b', root.Get<char>("name[1]"));

        Inner mapped = root.Get<Inner>("items[0]");
        Assert.AreEqual((byte)1, mapped.A);
        Assert.AreEqual((ushort)0x10, mapped.B);
        Assert.IsInstanceOfType<UnionValue>(root.Get<object>("pick"));
        Assert.IsInstanceOfType<StructValue>(root.Get<StructValue>("nested"));
    }

    /// <summary>A path that selects nothing is a path error whose message names the segment and the members that exist.</summary>
    [TestMethod]
    public void Get_MissingPath_ThrowsPathExceptionNamingTheSegment()
    {
        StructValue root = ParseRoot();
        CStructPathException missing = Assert.Throws<CStructPathException>(() => root.Get<int>("nested.c"));
        StringAssert.Contains(missing.Message, "cannot select 'c' in 'nested'");
        StringAssert.Contains(missing.Message, "a, b");
        CStructPathException index = Assert.Throws<CStructPathException>(() => root.Get<int>("items[2].a"));
        StringAssert.Contains(index.Message, "index 2 is outside the 2 element(s)");
        CStructPathException scalar = Assert.Throws<CStructPathException>(() => root.Get<int>("kind.x"));
        StringAssert.Contains(scalar.Message, "has no members");
        CStructPathException pointer = Assert.Throws<CStructPathException>(() => root.Get<int>("target.next"));
        StringAssert.Contains(pointer.Message, "'value' and 'address'");
        CStructPathException malformed = Assert.Throws<CStructPathException>(() => root.Get<int>("items[x]"));
        StringAssert.Contains(malformed.Message, "invalid index");
        Assert.Throws<CStructPathException>(() => root.Get<int>(".kind"));
        Assert.Throws<CStructPathException>(() => root.Get<int>("kind."));
        Assert.Throws<ArgumentNullException>(() => root.Get<int>(null!));
    }

    /// <summary>An undereferenced pointer refuses <c>.value</c> with a message that names the option to change.</summary>
    [TestMethod]
    public void Get_UndereferencedPointerValue_ExplainsTheOption()
    {
        StructValue root = new CStruct(Layout, pointerSize: 1, aligned: false)
            .Parse(Bytes, "root", options: new ReadOptions { DereferencePointers = false });
        Assert.AreEqual(18L, root.Get<long>("target.address"));
        CStructPathException failure = Assert.Throws<CStructPathException>(() => root.Get<byte>("target.value"));
        StringAssert.Contains(failure.Message, "DereferencePointers");
    }

    /// <summary>TryGet reports false for a missing path or a lossy conversion and true otherwise; null paths still throw.</summary>
    [TestMethod]
    public void TryGet_ReturnsFalseInsteadOfThrowing()
    {
        StructValue root = ParseRoot();
        Assert.IsTrue(root.TryGet("items[1].b", out ushort b));
        Assert.AreEqual((ushort)0x20, b);
        Assert.IsFalse(root.TryGet("items[1].c", out ushort _));
        Assert.IsFalse(root.TryGet("pick.wide", out sbyte _));
        Assert.IsFalse(root.TryGet("items[9]", out Inner? _));
        Assert.Throws<ArgumentNullException>(() => root.TryGet(null!, out int _));
    }

    /// <summary>Unions expose the same typed access, and their text lists the name, selection, members, and raw length.</summary>
    [TestMethod]
    public void Union_GetAndToString()
    {
        StructValue root = ParseRoot();
        var pick = root.Get<UnionValue>("pick");
        Assert.AreEqual((ushort)0x1234, pick.Get<ushort>("wide"));
        Assert.IsTrue(pick.TryGet("small", out byte small));
        Assert.AreEqual((byte)0x34, small);
        Assert.IsFalse(pick.TryGet("missing", out byte _));
        Assert.Throws<CStructPathException>(() => pick.Get<byte>("missing"));

        Assert.AreEqual("choice { small = 52, wide = 4660; 2 raw bytes }", pick.ToString());
        Assert.AreEqual("choice { selected: small; small = 165 }", UnionValue.FromMember("choice", "small", (byte)165).ToString());
        Assert.AreEqual("choice { 2 raw bytes }", UnionValue.FromRaw("choice", [0x34, 0x12]).ToString());
    }

    /// <summary>A struct assembled by hand for writing supports Get as well.</summary>
    [TestMethod]
    public void Get_OnHandBuiltStruct_ReadsMembers()
    {
        var value = new StructValue { ["kind"] = 7, ["nested"] = new StructValue { ["a"] = 1, ["b"] = 2 } };
        Assert.AreEqual((ushort)7, value.Get<ushort>("kind"));
        Assert.AreEqual(2, value.Get<int>("nested.b"));
    }

    internal sealed class Inner : ICStructMapped<Inner>
    {
        public byte A { get; set; }

        public ushort B { get; set; }

        public static Inner ReadFrom(StructValue source)
        {
            return new Inner { A = source.Get<byte>("a"), B = source.Get<ushort>("b"), };
        }

        public static void WriteTo(Inner value, StructValue target)
        {
            target["a"] = value.A;
            target["b"] = value.B;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Inner>();
        }
    }
}
