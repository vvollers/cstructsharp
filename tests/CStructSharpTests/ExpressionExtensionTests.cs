namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>The expression forms added for header parity: <c>%</c>, <c>^</c>, the conditional operator, <c>sizeof</c>/<c>offsetof</c>, and qualified enum members.</summary>
[TestClass]
public class ExpressionExtensionTests
{
    /// <summary><c>%</c> sits with <c>*</c> and <c>/</c>; <c>^</c> sits between <c>&amp;</c> and <c>|</c>, as in C.</summary>
    [TestMethod]
    public void ModuloAndXor_FollowCPrecedence()
    {
        var layout = new CStruct("#define A 7 % 3\n#define B 1 + 7 % 3 * 2\n#define C 6 ^ 3 | 8\n#define D 6 & 3 ^ 1\nstruct root { uint8 a[A]; uint8 b[B]; uint8 c[C]; uint8 d[D]; };");
        Assert.AreEqual(1 + 3 + 13 + 3, layout.GetStructSizeInBytes("root"));

        Assert.Throws<CStructLayoutException>(() => new CStruct("#define A 7 % 0\nstruct root { uint8 a[A]; };"));
        var runtime = new CStruct("struct root { uint8 n; uint8 v[n % 4]; };");
        dynamic value = runtime.Parse(new byte[] { 6, 1, 2, }.AsSpan(), "root");
        Assert.AreEqual(2, ((IEnumerable<object?>)value.v).Count());
        Assert.Throws<CStructException>(() => new CStruct("struct root { uint8 n; uint8 v[4 % n]; };").Parse(new byte[] { 0, }.AsSpan(), "root"));
    }

    /// <summary>Only the selected arm of <c>c ? a : b</c> is evaluated, so the other arm may name unavailable identifiers.</summary>
    [TestMethod]
    public void Conditional_EvaluatesOnlyTheSelectedArm()
    {
        var layout = new CStruct("#define N 1 ? 2 : 3\nstruct root { uint8 flag; uint8 v[flag ? 2 : 1]; uint8 w[flag ? missing : 1]; };");
        Assert.AreEqual(2, new CStruct("#define N 1 ? 2 : 3\nstruct root { uint8 v[N]; };").GetStructSizeInBytes("root"));

        Assert.Throws<CStructException>(() => layout.Parse(new byte[] { 1, 10, 11, 12, }.AsSpan(), "root"));
        dynamic off = layout.Parse(new byte[] { 0, 10, 11, }.AsSpan(), "root");
        Assert.AreEqual(1, ((IEnumerable<object?>)off.v).Count());
        Assert.AreEqual(1, ((IEnumerable<object?>)off.w).Count());

        // Right-associative and lower than every binary operator.
        Assert.AreEqual(5, new CStruct("#define N 0 ? 1 : 0 ? 2 : 5\nstruct root { uint8 v[N]; };").GetStructSizeInBytes("root"));
        Assert.AreEqual(4, new CStruct("#define N 1 + 1 == 2 ? 4 : 8\nstruct root { uint8 v[N]; };").GetStructSizeInBytes("root"));
        Assert.AreEqual(9, new CStruct("enum e : uint8 { A = 2 > 1 ? 9 : 0 }; struct root { uint8 v[e.A]; };").GetStructSizeInBytes("root"));
    }

    /// <summary><c>sizeof</c> and <c>offsetof</c> fold to literals from compiled types, including types declared later.</summary>
    [TestMethod]
    public void SizeofAndOffsetof_FoldAtConstruction()
    {
        var layout = new CStruct(
            "typedef uint16 word; struct h { uint8 a; uint32 b; uint16 c; }; " +
            "struct root { uint8 s[sizeof(h)]; uint8 w[sizeof(word)]; uint8 p[sizeof(uint8*)]; uint8 o[offsetof(h, c)]; uint8 l[offsetof(later, y)]; uint8 d[sizeof(DWORD) / sizeof(uint16)]; };" +
            "struct later { uint32 x; uint8 y; };",
            pointerSize: 4);
        Assert.AreEqual(7 + 2 + 4 + 5 + 4 + 2, layout.GetStructSizeInBytes("root"));

        var aligned = new CStruct("struct h { uint8 a; uint32 b; }; struct root { uint8 s[sizeof(h)]; uint8 o[offsetof(h, b)]; };", aligned: true);
        Assert.AreEqual(8 + 4, aligned.GetStructSizeInBytes("root"));

        var runtime = new CStruct("struct e { uint32 v; }; struct root { uint32 size; e items[size / sizeof(e)]; };");
        dynamic value = runtime.Parse(new byte[] { 8, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, }.AsSpan(), "root");
        Assert.AreEqual(2, ((IEnumerable<object?>)value.items).Count());

        Assert.Throws<CStructLayoutException>(() => new CStruct("struct d { uint8 n; uint8 v[n]; }; struct root { uint8 s[sizeof(d)]; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct h { uint8 a; }; struct root { uint8 s[offsetof(h, missing)]; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 s[sizeof(missing)]; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 s[strlen(root)]; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 s[sizeof(root)]; };"));
    }

    /// <summary>Fields sized by the new expression forms take part in every operation.</summary>
    [TestMethod]
    public void Extensions_RoundTripThroughEveryOperation()
    {
        var layout = new CStruct("enum kind : uint8 { A = 1, B = 6 ^ 3 }; struct head { uint32 a; uint16 b; }; struct root { uint8 x[kind.B % 4]; uint8 y[sizeof(head) ? offsetof(head, b) : 9]; };");
        byte[] bytes = [1, 2, 3, 4, 5,];
        using var stream = new MemoryStream((byte[])bytes.Clone());
        List<DebugData> debug = layout.ParseStreamWithDebug(stream, "root").DebugData;
        stream.Position = 0;
        dynamic parsed = layout.ParseStream(stream, "root");
        stream.Position = 0;
        Assert.IsTrue(debug.Any(item => item.Path == "root.y" && item.Start == 4 && item.End == 5));
        Assert.AreEqual(1, layout.ResolveAddress(stream, "root.y"));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
        using var written = new MemoryStream();
        layout.WriteStream(written, "root", new Dictionary<string, object?> { ["x"] = new byte[] { 1, }, ["y"] = new byte[] { 2, 3, 4, 5, }, });
        CollectionAssert.AreEqual(bytes, written.ToArray());
        layout.UpdateStream(stream, "root.y[3]", (byte)9);
        Assert.AreEqual((byte)9, layout.ReadValue<byte>(stream.ToArray().AsSpan(), "root.y[3]"));
    }

    /// <summary><c>Enum.Member</c> names a member of a named enum or flag anywhere an expression is accepted; the bare name stays local to the enum.</summary>
    [TestMethod]
    public void QualifiedEnumMember_IsAConstant()
    {
        var layout = new CStruct("#define BASE 2\nenum e : uint8 { A = BASE, B = A * 3 }; flag f : uint8 { X, Y, Z };\n#define N e.B + 1\nstruct root { uint8 v[N]; uint8 w[f.Z]; uint8 k; if (k == e.A) { uint8 extra; } };");
        Assert.AreEqual(7, ((IEnumerable<object?>)((dynamic)layout.Parse(new byte[16].AsSpan(), "root")).v).Count());
        dynamic value = layout.Parse(new byte[] { 0, 0, 0, 0, 0, 0, 0, 1, 2, 3, 4, 2, 9, }.AsSpan(), "root");
        Assert.AreEqual(4, ((IEnumerable<object?>)value.w).Count());
        Assert.AreEqual((byte)9, (byte)value.extra);

        Assert.Throws<CStructReadException>(() => new CStruct("enum e : uint8 { A = 1 }; struct root { uint8 v[A]; };").Parse(new byte[4].AsSpan(), "root"));
        Assert.Throws<CStructReadException>(() => new CStruct("enum e : uint8 { A = 1 }; struct root { uint8 v[e.Missing]; };").Parse(new byte[4].AsSpan(), "root"));
    }
}
