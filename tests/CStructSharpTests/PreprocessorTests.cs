namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Introspection;

/// <summary>
///     The preprocessor lines a pasted header carries: non-integer <c>#define</c>s, conditionals, <c>#undef</c>,
///     <c>#include</c>, <c>#pragma pack</c>, and backslash line continuations.
/// </summary>
[TestClass]
public class PreprocessorTests
{
    /// <summary>Both line-ending spellings preserve directive names, values, macro text, quoted text and following declarations.</summary>
    /// <param name="newline">The physical line ending joined by a preceding backslash.</param>
    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void Continuations_PreserveEachDirectiveContext(string newline)
    {
        string join = "\\" + newline;
        var layout = new CStruct(
            "#define " + join + "COUNT " + join + "2" + newline +
            "#define TEXT \"a" + join + "b\"" + newline +
            "#define MACRO(x) left " + join + "right" + newline +
            "#pragma ignored " + join + "remainder" + newline +
            "#include <kept.h>" + newline +
            "struct " + join + "root { uint8 bytes[COUNT]; };");

        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual("ab", layout.Constants["TEXT"].Value);
        Assert.AreEqual("(x) left right", layout.Constants["MACRO"].Value);
        CollectionAssert.AreEqual(new[] { "kept.h", }, layout.Includes.ToArray());
    }

    /// <summary>Directive block comments separate tokens, while a line comment leaves an empty constant at that line's end.</summary>
    [TestMethod]
    public void DirectiveComments_PreserveTokensAndLineBoundaries()
    {
        var layout = new CStruct("#define/*name*/ COUNT/*value*/2\n#define FLAG // no value\nstruct root { uint8 bytes[COUNT]; };");
        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(LayoutConstantKind.Empty, layout.Constants["FLAG"].Kind);
        Assert.IsNull(layout.Constants["FLAG"].Value);

        // An unfinished comment is a syntax error, not a directive value or an implicit line end.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct("#define /* unfinished"));
        StringAssert.Contains(failure.Message, "expected the end of the block comment");
    }

    /// <summary>Directive trivia matches ordinary Unicode whitespace without consuming a significant physical line ending.</summary>
    [TestMethod]
    public void Directives_AcceptNonBreakingSpaceWithoutCrossingLines()
    {
        var layout = new CStruct("#define\u00a0COUNT\u00a02\nstruct root { uint8 data[COUNT]; };");
        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("#define\nCOUNT 2\nstruct root { uint8 data; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("#define\rCOUNT 2\nstruct root { uint8 data; };"));
    }

    /// <summary>Text, byte, bare, and macro defines are published as constants and never enter layout expressions.</summary>
    [TestMethod]
    public void Defines_PublishEveryConstantKind()
    {
        var layout = new CStruct(
            "#define MAGIC \"CD\\x30\\\"1\"\n" +
            "#define RAW b\"ab\\x00c\"\n" +
            "#define FLAG\n" +
            "#define SZ(x) ((x) + 1)\n" +
            "#define COUNT 2 + 1\n" +
            "#define DYN COUNT + N\n" +
            "struct root { uint8 v[COUNT]; };");

        Assert.AreEqual(3, layout.GetStructSizeInBytes("root"));
        IReadOnlyDictionary<string, LayoutConstant> constants = layout.Constants;
        Assert.AreEqual(LayoutConstantKind.Text, constants["MAGIC"].Kind);
        Assert.AreEqual("CD0\"1", constants["MAGIC"].Value);
        Assert.AreEqual(LayoutConstantKind.Bytes, constants["RAW"].Kind);
        CollectionAssert.AreEqual(new byte[] { 0x61, 0x62, 0x00, 0x63, }, (byte[])constants["RAW"].Value!);
        Assert.AreEqual(LayoutConstantKind.Empty, constants["FLAG"].Kind);
        Assert.IsNull(constants["FLAG"].Value);
        Assert.AreEqual(LayoutConstantKind.Macro, constants["SZ"].Kind);
        Assert.AreEqual("(x) ((x) + 1)", constants["SZ"].Value);
        Assert.AreEqual(LayoutConstantKind.Integer, constants["COUNT"].Kind);
        Assert.AreEqual(new BigInteger(3), constants["COUNT"].Value);
        Assert.AreEqual(LayoutConstantKind.Expression, constants["DYN"].Kind);

        // A byte constant is copied on every read so the layout stays immutable.
        ((byte[])constants["RAW"].Value!)[0] = 0xFF;
        Assert.AreEqual((byte)0x61, ((byte[])constants["RAW"].Value!)[0]);

        Assert.Throws<CStructLayoutException>(() => new CStruct("#define MAGIC \"open\nstruct root { uint8 a; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("#define MAGIC \"x\"\nstruct root { uint8 v[MAGIC]; };"));
    }

    /// <summary>A text constant shares the global namespace with other declarations.</summary>
    [TestMethod]
    public void Defines_CollideWithOtherGlobals()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("#define root \"x\"\nstruct root { uint8 a; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("#define A 1\n#define A \"x\"\nstruct root { uint8 a; };"));
    }

    /// <summary><c>#ifdef</c>/<c>#ifndef</c>/<c>#else</c>/<c>#endif</c> select declarations by defined names, including caller-supplied ones.</summary>
    [TestMethod]
    public void Conditionals_SelectDeclarations()
    {
        const string source = """
                              #ifdef WIDE
                              struct root { uint32 a; };
                              #else
                              struct root { uint8 a; };
                              #endif
                              #ifndef WIDE
                              #define N 1
                              #else
                              #define N 4
                              #endif
                              struct tail { uint8 v[N]; };
                              """;
        Assert.AreEqual(1, new CStruct(source).GetStructSizeInBytes("root"));
        Assert.AreEqual(1, new CStruct(source).GetStructSizeInBytes("tail"));

        var wide = new CStruct(source, compilationOptions: new CStructCompilationOptions { Defined = new HashSet<string> { "WIDE", }, });
        Assert.AreEqual(4, wide.GetStructSizeInBytes("root"));
        Assert.AreEqual(4, wide.GetStructSizeInBytes("tail"));

        // A define earlier in the same source counts, a false branch is not parsed at all, and nesting is tracked.
        const string nested = """
                              #define A
                              #ifdef A
                              #ifdef B
                              this is never parsed
                              #else
                              struct root { uint16 a; };
                              #endif
                              #else
                              also never parsed
                              #endif
                              """;
        Assert.AreEqual(2, new CStruct(nested).GetStructSizeInBytes("root"));

        Assert.Throws<CStructLayoutException>(() => new CStruct("#ifdef X\nstruct root { uint8 a; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("#endif\nstruct root { uint8 a; };"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("#ifdef X\n#else\n#else\n#endif\nstruct root { uint8 a; };"));
    }

    /// <summary><c>Defined</c> is part of the compiled-layout cache key.</summary>
    [TestMethod]
    public void Defined_IsPartOfTheCacheKey()
    {
        const string source = "#ifdef WIDE\nstruct root { uint32 a; };\n#else\nstruct root { uint8 a; };\n#endif";
        CStruct narrow = CStruct.GetOrCompile(source);
        CStruct wide = CStruct.GetOrCompile(source, compilationOptions: new CStructCompilationOptions { Defined = new HashSet<string> { "WIDE", }, });
        Assert.AreEqual(1, narrow.GetStructSizeInBytes("root"));
        Assert.AreEqual(4, wide.GetStructSizeInBytes("root"));
    }

    /// <summary><c>#undef</c> removes a constant for the rest of the source, so it may be defined again.</summary>
    [TestMethod]
    public void Undef_RemovesTheConstant()
    {
        var layout = new CStruct("#define N 4\n#undef N\n#define N 2\nstruct root { uint8 v[N]; };");
        Assert.AreEqual(2, layout.GetStructSizeInBytes("root"));
        Assert.IsFalse(new CStruct("#define N 4\n#undef N\nstruct root { uint8 a; };").Constants.ContainsKey("N"));

        // With N removed, `v[N]` is an ordinary caller variable again.
        var undefined = new CStruct("#define N 4\n#undef N\nstruct root { uint8 v[N]; };");
        Assert.Throws<CStructReadException>(() => undefined.Parse(new byte[4].AsSpan(), "root"));
        Assert.AreEqual(3, ((IEnumerable<object?>)((dynamic)undefined.Parse(new byte[4].AsSpan(), "root", new Dictionary<string, int> { ["N"] = 3, })).v).Count());
    }

    /// <summary><c>#include</c> is recorded verbatim and never resolved.</summary>
    [TestMethod]
    public void Include_IsRecorded()
    {
        var layout = new CStruct("#include <stdint.h>\n#include \"local/types.h\"\nstruct root { uint8 a; };");
        CollectionAssert.AreEqual(new[] { "stdint.h", "local/types.h", }, layout.Includes.ToArray());
        Assert.IsEmpty(new CStruct("struct root { uint8 a; };").Includes);
        Assert.Throws<CStructLayoutException>(() => new CStruct("#include stdint.h\nstruct root { uint8 a; };"));
    }

    /// <summary><c>#pragma pack</c> clamps the alignment of the composites that follow it, exactly like a composite <c>@align(N)</c>.</summary>
    [TestMethod]
    public void PragmaPack_ClampsFollowingComposites()
    {
        const string source = """
                              struct natural { uint8 a; uint32 b; };
                              #pragma pack(push, 1)
                              struct packed { uint8 a; uint32 b; };
                              #pragma pack(push, 2)
                              struct two { uint8 a; uint32 b; };
                              #pragma pack(pop)
                              struct still_packed { uint8 a; uint32 b; };
                              #pragma pack(pop)
                              struct natural_again { uint8 a; uint32 b; };
                              #pragma pack(2)
                              struct set { uint8 a; uint32 b; uint64 c; };
                              #pragma pack()
                              struct reset { uint8 a; uint32 b; };
                              #pragma once
                              struct explicit_override { uint8 a; uint32 b @align(4); };
                              """;
        var layout = new CStruct(source, aligned: true);
        Assert.AreEqual(8, layout.GetStructSizeInBytes("natural"));
        Assert.AreEqual(5, layout.GetStructSizeInBytes("packed"));
        Assert.AreEqual(6, layout.GetStructSizeInBytes("two"));
        Assert.AreEqual(5, layout.GetStructSizeInBytes("still_packed"));
        Assert.AreEqual(8, layout.GetStructSizeInBytes("natural_again"));
        Assert.AreEqual(14, layout.GetStructSizeInBytes("set"));
        Assert.AreEqual(8, layout.GetStructSizeInBytes("reset"));
        Assert.AreEqual(8, layout.GetStructSizeInBytes("explicit_override"));

        // Packed placement ignores alignment already, so the pragma changes nothing there.
        Assert.AreEqual(5, new CStruct(source).GetStructSizeInBytes("natural"));
        Assert.Throws<CStructLayoutException>(() => new CStruct("#pragma pack(3)\nstruct root { uint8 a; uint32 b; };", aligned: true));
    }

    /// <summary>A pack-clamped struct behaves like an <c>@align</c>-clamped one in every operation.</summary>
    [TestMethod]
    public void PragmaPack_RoundTripsThroughEveryOperation()
    {
        var layout = new CStruct("#pragma pack(1)\nstruct root { uint8 a; uint32 b; };", aligned: true);
        byte[] bytes = [1, 2, 0, 0, 0,];
        using var stream = new MemoryStream((byte[])bytes.Clone());

        IReadOnlyList<DebugData> debug = layout.ParseWithDebug(stream, "root").Debug;
        stream.Position = 0;
        dynamic parsed = layout.Parse(stream, "root");
        stream.Position = 0;
        Assert.IsTrue(debug.Any(item => item.Path == "root.b" && item.Start == 1 && item.End == 5));
        Assert.AreEqual(1, layout.ResolveAddress(stream, "root.b"));
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));

        using var written = new MemoryStream();
        layout.Write(written, "root", new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = 2U, });
        CollectionAssert.AreEqual(bytes, written.ToArray());

        layout.Update(stream, "root.b", 3U);
        CollectionAssert.AreEqual(new byte[] { 1, 3, 0, 0, 0, }, stream.ToArray());
        Assert.AreEqual(3U, layout.ReadValue<uint>(stream.ToArray().AsSpan(), "root.b"));
    }

    /// <summary>A backslash before a line end joins the lines, inside a define and inside a declaration alike.</summary>
    [TestMethod]
    public void LineContinuation_JoinsLines()
    {
        var layout = new CStruct("#define A 1 + \\\n 2\n#define MAGIC \"ab\\\ncd\"\nstruct \\\r\nroot { uint8 v[A]; \\\n uint8 tail; };");
        Assert.AreEqual(4, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual("abcd", layout.Constants["MAGIC"].Value);
        Assert.AreEqual("(x) ((x) + 2)", new CStruct("#define SZ(x) ((x) + \\\n 2)\nstruct root { uint8 a; };").Constants["SZ"].Value);
    }
}
