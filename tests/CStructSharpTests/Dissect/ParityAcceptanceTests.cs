namespace CStructSharp.Tests.Dissect;

using CStructSharp.Diagnostics;

/// <summary>
///     The dissect acceptance table: one row per language form a dissect.cstruct definition uses that the layout
///     language accepts, or deliberately rejects (<c>docs/guides/migrating-from-dissect.md</c>), each checked by
///     compiling it and, where a form is only rejected when used, by reading with it.
/// </summary>
[TestClass]
public class ParityAcceptanceTests
{
    private static readonly IReadOnlyList<Row> Rows =
    [
        //// Spellings, typedef forms, top-level declarations, preprocessor, enum member names
        new("DWORD alias built in", "struct r { DWORD a; WORD b; BYTE c; ULONG d; USHORT e; CHAR f[2]; WCHAR g; QWORD h; LONG i; ULONGLONG j; };", true),
        new("GNU/IDA aliases built in", "struct r { u8 a; __u16 b; u32 c; __u64 d; wchar_t e; _BYTE f; _DWORD g; uchar h; ushort i; };", true),
        new("MSVC __int spellings", "struct r { __int8 a; unsigned __int16 b; __int32 c; unsigned __int64 d; INT8 e; UINT64 f; };", true),
        new("uleb128/ileb128 spellings", "struct r { uleb128 a; ileb128 b; };", true),
        new("typedef struct _X {..} X, *PX;", "typedef struct _X { uint8 a; } X, *PX; struct r { X x; PX p; };", true),
        new("typedef struct NAME {..}; (no alias)", "typedef struct NAME { uint8 a; }; struct r { NAME n; };", true),
        new("tag usable after typedef struct _X {..} X;", "typedef struct _X { uint8 a; } X; struct r { X x; _X y; };", true),
        new("typedef struct X {..} X; (tag equals alias)", "typedef struct X { uint8 a; } X; struct r { X x; };", true),
        new("typedef struct tag alias;", "struct tag { uint8 a; }; typedef struct tag alias; struct r { alias v; };", true),
        new("typedef T name[N];", "typedef uint32 quad[4]; struct r { quad v; };", true),
        new("top-level struct { } name;", "struct { uint32 tv_sec; uint32 tv_usec; } timeval; struct r { timeval t; };", true),
        new("top-level struct X { } var;", "struct timeval { uint32 s; } header; struct r { timeval t; };", true),
        new("forward declaration", "struct node; struct node { uint32 v; node *next; };", true),
        new("enum member starting with a digit", "enum E : uint16 { 32BIT_MACHINE = 1 }; struct r { E e; };", true),
        new("#define string constant", "#define MAGIC \"CD001\"\nstruct r { uint8 a; };", true),
        new("#define bytes constant", "#define MAGIC b\"CD001\"\nstruct r { uint8 a; };", true),
        new("#define without value", "#define FLAG\nstruct r { uint8 a; };", true),
        new("#define function-like macro kept opaque", "#define SZ(x) ((x)+1)\nstruct r { uint8 a; };", true),
        new("#ifdef/#else/#endif", "#define X 1\n#ifdef X\nstruct r { uint8 a; };\n#else\nstruct r { uint16 a; };\n#endif", true),
        new("#ifndef skips a false branch", "#define X 1\n#ifndef X\nthis is not parsed\n#endif\nstruct r { uint8 a; };", true),
        new("#undef", "#define X 1\n#undef X\nstruct r { uint8 a; };", true),
        new("#include recorded", "#include <stdint.h>\nstruct r { uint8 a; };", true),
        new("backslash line continuation", "#define A 1 + \\\n 2\nstruct r { uint8 v[A]; };", true),

        //// Inline unions
        new("inline anonymous union in struct", "struct r { uint32 a; union { uint32 x; uint16 y; }; uint8 z; };", true),
        new("inline named union in struct", "struct r { uint32 a; union { uint32 x; uint16 y; } u; uint8 z; };", true),
        new("inline struct inside union", "union u { struct { uint32 a; uint32 b; } s; uint64 q; };", true),
        new("anonymous struct inside anonymous union (NTFS)", "struct r { uint32 a; union { struct { uint16 EaSize; uint16 _; }; uint32 ReparsePointTag; }; uint8 z; };", true),

        //// Arrays, enums, expressions, primitives
        new("[EOF] read-to-end array", "struct r { uint32 magic; char data[EOF]; };", true),
        new("zero-terminated uint16 array", "struct r { uint16 x[]; };", true),
        new("zero-terminated struct array", "struct e { uint16 a; }; struct r { e items[]; };", true),
        new("flag declaration", "flag F : uint32 { A = 1, B = 2 }; struct r { F f; };", true),
        new("enum as bitfield storage", "enum E : uint16 { A = 1 }; struct r { E type : 2; E name : 3; };", true),
        new("anonymous enum members become constants", "enum { A = 3, B }; struct r { uint8 v[B]; };", true),
        new("qualified enum member in expression", "enum E : uint8 { N = 3 }; struct r { uint8 v[E.N]; };", true),
        new("modulo operator", "#define N 7 % 3\nstruct r { uint8 v[N]; };", true),
        new("xor operator", "#define N 7 ^ 2\nstruct r { uint8 v[N]; };", true),
        new("ternary operator", "#define N 1 ? 2 : 3\nstruct r { uint8 v[N]; };", true),
        new("sizeof(type) in expression", "struct h { uint32 a; }; struct r { uint8 v[sizeof(h)]; };", true),
        new("offsetof(type, field) in expression", "struct h { uint32 a; uint16 b; }; struct r { uint8 v[offsetof(h, b)]; };", true),
        new("int48/uint48", "struct r { int48 a; uint48 b; };", true),
        new("int128/uint128", "struct r { int128 a; uint128 b; OWORD c; };", true),
        new("float16", "struct r { float16 a; };", true),
        new("void pointer", "struct r { void *p; };", true),
        new("function pointer as opaque pointer storage", "struct r { uint8 (*callback)(uint8); };", true),
        new("#pragma pack mapped to composite alignment", "#pragma pack(push, 1)\nstruct r { uint8 a; uint32 b; };\n#pragma pack(pop)", true),

        //// Dotted nested references
        new("dotted nested field in expression", "struct h { uint8 n; }; struct r { h hdr; uint8 v[hdr.n]; };", true),

        //// Forms found by the corpus sweep
        new("repeated `_` padding fields", "struct r { uint16 _; uint16 _; uint8 v; };", true),
        new("typedef enum with tag and alias", "typedef enum _E : uint8 { A = 1 } E, *PE; struct r { E e; _E f; PE p; };", true),
        new("tagged inline union promoted (MSVC/dissect)", "struct r { uint32 a; union u_tag { uint32 x; uint16 y; }; uint8 z; };", true),
        new("tagged inline struct member", "struct r { struct gen_tag { int32 pid; } gen; uint8 z; };", true),
        new("enum members separated by line breaks", "flag F : uint16 {\n NORMAL = 0\n FLUSH = 1\n}; struct r { F f; };", true),
        new("digit-only enum member names", "enum C : uint8 { 0 = 0x30, 1 = 0x31 }; struct r { C c; };", true),
        new("enum without backing type is 32-bit", "enum E { A = 0x80000000 }; enum S { N = -1 }; struct r { E e; S s; };", true),
        new("#define with non-expression text", "#define MAGIC ADSEGMENTEDFILE \u0000\nstruct r { uint8 a; };", true),
        new("#define naming an unused unknown constant", "#define MAGIC SOMENAME\nstruct r { uint8 a; };", true, CompileOnly: false),

        //// Deliberately still rejected
        new("duplicate field name", "struct r { uint16 a; uint16 a; };", false),
        new("duplicate enum member name", "enum E : uint8 { A = 1, A = 2 }; struct r { E e; };", false),
        new("negative literal array count", "struct r { uint8 v[-1]; };", false),
        new("dynamic union member", "union u { uint8 n; uint8 v[n]; };", false),
        new("void as a value field", "struct r { void v; };", false),
        new("later field referenced before it is read", "struct r { uint8 v[len]; uint8 len; };", false, CompileOnly: false),
    ];

    /// <summary>Every row compiles (or is rejected) exactly as the table says.</summary>
    [TestMethod]
    public void AcceptanceTable_MatchesEveryRow()
    {
        var failures = new List<string>();
        foreach (Row row in Rows)
        {
            string? error = null;
            bool accepted;
            try
            {
                var layout = new CStruct(row.Layout);
                accepted = true;
                if (!row.CompileOnly)
                {
                    // A construction-time acceptance that fails at first use counts as rejected.
                    _ = layout.Parse(new byte[64].AsSpan());
                }
            }
            catch (CStructException exception)
            {
                accepted = false;
                error = exception.Message;
            }

            if (accepted != row.ExpectAccepted)
            {
                failures.Add($"{row.Label}: expected {(row.ExpectAccepted ? "accept" : "reject")}, got {(accepted ? "accept" : "reject")}{(error is null ? string.Empty : " (" + error + ")")}");
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    /// <summary>One language form and its expected outcome.</summary>
    /// <param name="Label">What the form is.</param>
    /// <param name="Layout">A layout that uses the form.</param>
    /// <param name="ExpectAccepted">Whether the form is accepted.</param>
    /// <param name="CompileOnly">Whether compiling decides; otherwise a read with the layout must also succeed.</param>
    private sealed record Row(string Label, string Layout, bool ExpectAccepted, bool CompileOnly = true);
}
