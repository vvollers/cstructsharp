namespace CStructSharp.Tests;

using System.Numerics;
using System.Runtime.CompilerServices;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     <c>flag</c> declarations (bitmask enums), enum/flag-typed bitfields, and anonymous enums whose members become
///     constants - the three enum shapes dissect definitions use that plain C enums do not cover.
/// </summary>
[TestClass]
public class FlagDeclarationTests
{
    private const string Layout = "flag access : uint16 { READ, WRITE, EXEC, RW = 3, HIDDEN = 0x100 }; struct root { access mode; access modes[2]; };";

    [Flags]
    internal enum Access : ushort
    {
        Read = 1,
        Write = 2,
        Exec = 4,
        Hidden = 0x100,
    }

    /// <summary>Omitted flag values are the next unused bit, and a read decomposes the value into member names.</summary>
    [TestMethod]
    public void Flag_ReadsDecomposedNames()
    {
        var layout = new CStruct(Layout);
        Assert.AreEqual(6, layout.GetStructSizeInBytes("root"));

        dynamic value = layout.Parse(new byte[] { 0x05, 0x01, 0x03, 0x00, 0x00, 0x00, }.AsSpan(), "root");
        var mode = (FlagValueResult)value.mode;
        Assert.AreEqual(new BigInteger(0x105), mode.Value);
        Assert.IsNull(mode.Name);
        CollectionAssert.AreEqual(new[] { "READ", "EXEC", "HIDDEN", }, mode.Names.ToArray());
        Assert.AreEqual(0UL, mode.Remainder);
        Assert.IsTrue(mode.Has("EXEC"));
        Assert.IsFalse(mode.Has("WRITE"));
        Assert.AreEqual("READ|EXEC|HIDDEN", mode.ToString());
        Assert.AreEqual("uint16", mode.StorageType);

        var first = (FlagValueResult)value.modes[0];
        Assert.AreEqual("RW", first.Name);
        CollectionAssert.AreEqual(new[] { "READ", "WRITE", "RW", }, first.Names.ToArray());
        var none = (FlagValueResult)value.modes[1];
        Assert.IsEmpty(none.Names);
        Assert.AreEqual("0", none.ToString());

        dynamic unknown = layout.Parse(new byte[] { 0x0A, 0x80, 0, 0, 0, 0, }.AsSpan(), "root");
        var partial = (FlagValueResult)unknown.mode;
        CollectionAssert.AreEqual(new[] { "WRITE", }, partial.Names.ToArray());
        Assert.AreEqual(0x8008UL, partial.Remainder);
        Assert.AreEqual("WRITE|0x8008", partial.ToString());
    }

    /// <summary>A flag is written from a result, a member name, a <c>|</c>-joined string, a name sequence, or a number.</summary>
    [TestMethod]
    public void Flag_WritesFromEveryInputShape()
    {
        var layout = new CStruct(Layout);
        dynamic parsed = layout.Parse(new byte[] { 0x05, 0x01, 0x03, 0x00, 0x00, 0x00, }.AsSpan(), "root");
        CollectionAssert.AreEqual(new byte[] { 0x05, 0x01, 0x03, 0x00, 0x00, 0x00, }, layout.Serialize("root", parsed));

        byte[] Write(object mode)
        {
            return layout.Serialize("root", new Dictionary<string, object?> { ["mode"] = mode, ["modes"] = new object[] { 0, 0, }, });
        }

        CollectionAssert.AreEqual(new byte[] { 0x01, 0, 0, 0, 0, 0, }, Write("READ"));
        CollectionAssert.AreEqual(new byte[] { 0x05, 0x01, 0, 0, 0, 0, }, Write("READ|EXEC | HIDDEN"));
        CollectionAssert.AreEqual(new byte[] { 0x06, 0, 0, 0, 0, 0, }, Write(new[] { "WRITE", "EXEC", }));
        CollectionAssert.AreEqual(new byte[] { 0x03, 0, 0, 0, 0, 0, }, Write(3));
        Assert.Throws<CStructWriteException>(() => Write("READ|NOPE"));

        using var stream = new MemoryStream(new byte[6]);
        layout.Update(stream, "root.mode", "HIDDEN");
        CollectionAssert.AreEqual(new byte[] { 0, 1, 0, 0, 0, 0, }, stream.ToArray());
    }

    /// <summary>A flag maps to a <c>[Flags]</c> CLR enum in a typed read, and the JSON-facing helpers see the names.</summary>
    [TestMethod]
    public void Flag_TypedReadMapsToClrEnum()
    {
        var layout = new CStruct(Layout);
        Root typed = layout.ReadValue<Root>(new byte[] { 0x05, 0x01, 0x03, 0x00, 0x00, 0x00, }.AsSpan(), "root");
        Assert.AreEqual(Access.Read | Access.Exec | Access.Hidden, typed.Mode);
        Assert.AreEqual(Access.Read | Access.Write, typed.Modes[0]);
    }

    /// <summary>A flag field takes part in debug, address, update, and typed-read operations like an enum field.</summary>
    [TestMethod]
    public void Flag_RoundTripsThroughEveryOperation()
    {
        var layout = new CStruct(Layout);
        byte[] bytes = [0x05, 0x01, 0x03, 0x00, 0x00, 0x00,];
        using var stream = new MemoryStream((byte[])bytes.Clone());
        (dynamic _, IReadOnlyList<DebugData> debug) = layout.ParseWithDebug(stream, "root");
        stream.Position = 0;
        Assert.IsTrue(debug.Any(item => item.Path == "root.mode" && item.Start == 0 && item.End == 2));
        Assert.AreEqual(2, layout.ResolveAddress(stream, "root.modes[0]"));
        layout.Update(stream, "root.modes[1]", "EXEC|HIDDEN");
        CollectionAssert.AreEqual(new byte[] { 0x05, 0x01, 0x03, 0x00, 0x04, 0x01, }, stream.ToArray());
        var updated = (FlagValueResult)layout.ReadValue<EnumValueResult>(stream.ToArray().AsSpan(), "root.modes[1]");
        CollectionAssert.AreEqual(new[] { "EXEC", "HIDDEN", }, updated.Names.ToArray());
    }

    /// <summary>Explicit flag values may be any expression; a plain enum keeps its plus-one rule.</summary>
    [TestMethod]
    public void Flag_AutoValuesFollowTheHighestBit()
    {
        var layout = new CStruct("flag f : uint8 { A, B = 0x10, C, D = 1 << 1, E }; struct root { f v; };");
        dynamic value = layout.Parse(new byte[] { 0xFF, }.AsSpan(), "root");
        var result = (FlagValueResult)value.v;
        CollectionAssert.AreEqual(new[] { "A", "B", "C", "D", "E", }, result.Names.ToArray());

        byte[] a = layout.Serialize("root", new Dictionary<string, object?> { ["v"] = "A", });
        byte[] c = layout.Serialize("root", new Dictionary<string, object?> { ["v"] = "C", });
        byte[] e = layout.Serialize("root", new Dictionary<string, object?> { ["v"] = "E", });
        Assert.AreEqual(0x01, a[0]);
        Assert.AreEqual(0x20, c[0]);
        Assert.AreEqual(0x40, e[0]);

        Assert.Throws<CStructLayoutException>(() => new CStruct("flag f : uint8 { A = 0x80, B }; struct root { f v; };"));
    }

    /// <summary>An enum or flag can be bitfield storage; its bits live in the backing type and read back as enum values.</summary>
    [TestMethod]
    public void EnumBitfield_StoresBitsInTheBackingType()
    {
        var layout = new CStruct("enum kind : uint16 { NONE, CODE, DATA, CONST }; flag opts : uint16 { A, B, C }; struct root { kind type : 2; kind name_type : 3; opts o : 3; uint16 rest : 8; };");
        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));

        // type = 2 (bits 0-1), name_type = 3 (bits 2-4), o = A|C = 5 (bits 5-7), rest = 0x5A (bits 8-15).
        byte[] bytes = [0b1010_1110, 0x5A,];
        dynamic value = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual("DATA", ((EnumValueResult)value.type).Name);
        Assert.AreEqual("CONST", ((EnumValueResult)value.name_type).Name);
        CollectionAssert.AreEqual(new[] { "A", "C", }, ((FlagValueResult)value.o).Names.ToArray());
        Assert.AreEqual(0x5A, (int)value.rest);

        CollectionAssert.AreEqual(bytes, layout.Serialize("root", value));
        byte[] written = layout.Serialize(
            "root",
            new Dictionary<string, object?> { ["type"] = "CODE", ["name_type"] = 1, ["o"] = "B", ["rest"] = 0xFF, });
        CollectionAssert.AreEqual(new byte[] { 0b0100_0101, 0xFF, }, written);

        using var stream = new MemoryStream((byte[])bytes.Clone());
        layout.Update(stream, "root.type", "CONST");
        Assert.AreEqual(0b1010_1111, stream.ToArray()[0]);
        Assert.AreEqual("CONST", layout.ReadValue<EnumValueResult>(stream.ToArray().AsSpan(), "root.type").Name);

        (dynamic _, IReadOnlyList<DebugData> debug) = layout.ParseWithDebug(new MemoryStream(bytes), "root");
        Assert.IsTrue(debug.Any(item => item.Path == "root.type"));
    }

    /// <summary>An enum bitfield resolves to its storage unit's address like any other bitfield.</summary>
    [TestMethod]
    public void EnumBitfield_IsAddressable()
    {
        const string source = "enum kind : uint16 { NONE, CODE }; struct root { uint8 head; kind type : 2; kind other : 3; uint8 tail; };";

        // SysV packing (GCC -fpack-struct): the five bits take one byte, so tail follows at offset 2.
        var layout = new CStruct(source);
        using var stream = new MemoryStream(new byte[] { 9, 0x0D, 7, });
        Assert.AreEqual(1, layout.ResolveAddress(stream, "root.type"));
        Assert.AreEqual(1, layout.ResolveAddress(stream, "root.other"));
        Assert.AreEqual(2, layout.ResolveAddress(stream, "root.tail"));
        Assert.AreEqual("CODE", layout.ReadValue<EnumValueResult>(stream.ToArray().AsSpan(), "root.type").Name);
        Assert.AreEqual(3, layout.ReadValue<EnumValueResult>(stream.ToArray().AsSpan(), "root.other").Value);

        // MSVC packing keeps the whole uint16 unit, so tail follows at offset 3.
        var msvc = new CStruct(source, compilationOptions: new CStructCompilationOptions { BitfieldPacking = BitfieldPacking.Msvc, });
        using var msvcStream = new MemoryStream(new byte[] { 9, 0x0D, 0x00, 7, });
        Assert.AreEqual(1, msvc.ResolveAddress(msvcStream, "root.other"));
        Assert.AreEqual(3, msvc.ResolveAddress(msvcStream, "root.tail"));
        Assert.AreEqual("CODE", msvc.ReadValue<EnumValueResult>(msvcStream.ToArray().AsSpan(), "root.type").Name);
    }

    /// <summary>An anonymous enum declares constants, not a type; a plain one counts up and a flag one uses the next bit.</summary>
    [TestMethod]
    public void AnonymousEnum_MembersBecomeConstants()
    {
        var layout = new CStruct("enum { ZERO, ONE, FIVE = 5, SIX }; flag { F1, F2, F4 = 4, F8 }; struct root { uint8 a[SIX]; uint8 b[F8]; };");
        Assert.AreEqual(6 + 8, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(new BigInteger(1), layout.Constants["ONE"].Value);
        Assert.AreEqual(new BigInteger(2), layout.Constants["F2"].Value);
        Assert.AreEqual(new BigInteger(8), layout.Constants["F8"].Value);

        Assert.Throws<CStructLayoutException>(() => new CStruct("enum { A }; enum { A }; struct root { uint8 v; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("#define N 2\nflag { A = N, B }; struct root { uint8 v; };"));
    }

    internal sealed class Root : ICStructMapped<Root>
    {
        public Access Mode { get; set; }

        public Access[] Modes { get; set; } = [];

        public static Root ReadFrom(StructValue source)
        {
            return new Root { Mode = source.Get<Access>("mode"), Modes = source.Get<Access[]>("modes"), };
        }

        public static void WriteTo(Root value, StructValue target)
        {
            target["mode"] = value.Mode;
            target["modes"] = value.Modes;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Root>();
        }
    }
}
