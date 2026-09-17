namespace CStructSharp.Tests.Dissect;

using CStructSharp.Diagnostics;

/// <summary>
///     The dissect-parity acceptance table: one row per language form the parity work
///     (<c>docs/guides/migrating-from-dissect.md</c>) accepts or deliberately keeps rejecting, tagged with
///     the phase that delivers it. Rows up to <see cref="ImplementedThroughPhase"/> are asserted; later rows are
///     listed as pending so the table doubles as the plan's progress meter.
/// </summary>
[TestClass]
public class ParityAcceptanceTests
{
    /// <summary>Raise this as each plan phase lands; rows tagged with a later phase are reported, not asserted.</summary>
    private const int ImplementedThroughPhase = 5;

    private static readonly IReadOnlyList<Row> Rows =
    [
        //// Phase 1: spellings, typedef forms, top-level declarations, preprocessor, enum member names
        new(1, "DWORD alias built in", "struct r { DWORD a; WORD b; BYTE c; ULONG d; USHORT e; CHAR f[2]; WCHAR g; QWORD h; LONG i; ULONGLONG j; };", true),
        new(1, "GNU/IDA aliases built in", "struct r { u8 a; __u16 b; u32 c; __u64 d; wchar_t e; _BYTE f; _DWORD g; uchar h; ushort i; };", true),
        new(1, "MSVC __int spellings", "struct r { __int8 a; unsigned __int16 b; __int32 c; unsigned __int64 d; INT8 e; UINT64 f; };", true),
        new(1, "uleb128/ileb128 spellings", "struct r { uleb128 a; ileb128 b; };", true),
        new(1, "typedef struct _X {..} X, *PX;", "typedef struct _X { uint8 a; } X, *PX; struct r { X x; PX p; };", true),
        new(1, "typedef struct NAME {..}; (no alias)", "typedef struct NAME { uint8 a; }; struct r { NAME n; };", true),
        new(1, "tag usable after typedef struct _X {..} X;", "typedef struct _X { uint8 a; } X; struct r { X x; _X y; };", true),
        new(1, "typedef struct X {..} X; (tag equals alias)", "typedef struct X { uint8 a; } X; struct r { X x; };", true),
        new(1, "typedef struct tag alias;", "struct tag { uint8 a; }; typedef struct tag alias; struct r { alias v; };", true),
        new(1, "typedef T name[N];", "typedef uint32 quad[4]; struct r { quad v; };", true),
        new(1, "top-level struct { } name;", "struct { uint32 tv_sec; uint32 tv_usec; } timeval; struct r { timeval t; };", true),
        new(1, "top-level struct X { } var;", "struct timeval { uint32 s; } header; struct r { timeval t; };", true),
        new(1, "forward declaration", "struct node; struct node { uint32 v; node *next; };", true),
        new(1, "enum member starting with a digit", "enum E : uint16 { 32BIT_MACHINE = 1 }; struct r { E e; };", true),
        new(1, "#define string constant", "#define MAGIC \"CD001\"\nstruct r { uint8 a; };", true),
        new(1, "#define bytes constant", "#define MAGIC b\"CD001\"\nstruct r { uint8 a; };", true),
        new(1, "#define without value", "#define FLAG\nstruct r { uint8 a; };", true),
        new(1, "#define function-like macro kept opaque", "#define SZ(x) ((x)+1)\nstruct r { uint8 a; };", true),
        new(1, "#ifdef/#else/#endif", "#define X 1\n#ifdef X\nstruct r { uint8 a; };\n#else\nstruct r { uint16 a; };\n#endif", true),
        new(1, "#ifndef skips a false branch", "#define X 1\n#ifndef X\nthis is not parsed\n#endif\nstruct r { uint8 a; };", true),
        new(1, "#undef", "#define X 1\n#undef X\nstruct r { uint8 a; };", true),
        new(1, "#include recorded", "#include <stdint.h>\nstruct r { uint8 a; };", true),
        new(1, "backslash line continuation", "#define A 1 + \\\n 2\nstruct r { uint8 v[A]; };", true),

        //// Phase 2: inline unions
        new(2, "inline anonymous union in struct", "struct r { uint32 a; union { uint32 x; uint16 y; }; uint8 z; };", true),
        new(2, "inline named union in struct", "struct r { uint32 a; union { uint32 x; uint16 y; } u; uint8 z; };", true),
        new(2, "inline struct inside union", "union u { struct { uint32 a; uint32 b; } s; uint64 q; };", true),
        new(2, "anonymous struct inside anonymous union (NTFS)", "struct r { uint32 a; union { struct { uint16 EaSize; uint16 _; }; uint32 ReparsePointTag; }; uint8 z; };", true),

        //// Phase 3: arrays, enums, expressions, primitives
        new(3, "[EOF] read-to-end array", "struct r { uint32 magic; char data[EOF]; };", true),
        new(3, "zero-terminated uint16 array", "struct r { uint16 x[]; };", true),
        new(3, "zero-terminated struct array", "struct e { uint16 a; }; struct r { e items[]; };", true),
        new(3, "flag declaration", "flag F : uint32 { A = 1, B = 2 }; struct r { F f; };", true),
        new(3, "enum as bitfield storage", "enum E : uint16 { A = 1 }; struct r { E type : 2; E name : 3; };", true),
        new(3, "anonymous enum members become constants", "enum { A = 3, B }; struct r { uint8 v[B]; };", true),
        new(3, "qualified enum member in expression", "enum E : uint8 { N = 3 }; struct r { uint8 v[E.N]; };", true),
        new(3, "modulo operator", "#define N 7 % 3\nstruct r { uint8 v[N]; };", true),
        new(3, "xor operator", "#define N 7 ^ 2\nstruct r { uint8 v[N]; };", true),
        new(3, "ternary operator", "#define N 1 ? 2 : 3\nstruct r { uint8 v[N]; };", true),
        new(3, "sizeof(type) in expression", "struct h { uint32 a; }; struct r { uint8 v[sizeof(h)]; };", true),
        new(3, "offsetof(type, field) in expression", "struct h { uint32 a; uint16 b; }; struct r { uint8 v[offsetof(h, b)]; };", true),
        new(3, "int48/uint48", "struct r { int48 a; uint48 b; };", true),
        new(3, "int128/uint128", "struct r { int128 a; uint128 b; OWORD c; };", true),
        new(3, "float16", "struct r { float16 a; };", true),
        new(3, "void pointer", "struct r { void *p; };", true),
        new(3, "function pointer as opaque pointer storage", "struct r { uint8 (*callback)(uint8); };", true),
        new(3, "#pragma pack mapped to composite alignment", "#pragma pack(push, 1)\nstruct r { uint8 a; uint32 b; };\n#pragma pack(pop)", true),

        //// Phase 5: dotted nested references
        new(5, "dotted nested field in expression", "struct h { uint8 n; }; struct r { h hdr; uint8 v[hdr.n]; };", true),

        //// Phase 3 additions found by the corpus sweep
        new(3, "repeated `_` padding fields", "struct r { uint16 _; uint16 _; uint8 v; };", true),
        new(3, "typedef enum with tag and alias", "typedef enum _E : uint8 { A = 1 } E, *PE; struct r { E e; _E f; PE p; };", true),
        new(3, "tagged inline union promoted (MSVC/dissect)", "struct r { uint32 a; union u_tag { uint32 x; uint16 y; }; uint8 z; };", true),
        new(3, "tagged inline struct member", "struct r { struct gen_tag { int32 pid; } gen; uint8 z; };", true),
        new(3, "enum members separated by line breaks", "flag F : uint16 {\n NORMAL = 0\n FLUSH = 1\n}; struct r { F f; };", true),
        new(3, "digit-only enum member names", "enum C : uint8 { 0 = 0x30, 1 = 0x31 }; struct r { C c; };", true),
        new(3, "enum without backing type is 32-bit", "enum E { A = 0x80000000 }; enum S { N = -1 }; struct r { E e; S s; };", true),
        new(3, "#define with non-expression text", "#define MAGIC ADSEGMENTEDFILE \u0000\nstruct r { uint8 a; };", true),
        new(3, "#define naming an unused unknown constant", "#define MAGIC SOMENAME\nstruct r { uint8 a; };", true, CompileOnly: false),

        //// Deliberately still rejected
        new(0, "duplicate field name", "struct r { uint16 a; uint16 a; };", false),
        new(0, "duplicate enum member name", "enum E : uint8 { A = 1, A = 2 }; struct r { E e; };", false),
        new(0, "negative literal array count", "struct r { uint8 v[-1]; };", false),
        new(0, "dynamic union member", "union u { uint8 n; uint8 v[n]; };", false),
        new(0, "void as a value field", "struct r { void v; };", false),
        new(0, "later field referenced before it is read", "struct r { uint8 v[len]; uint8 len; };", false, CompileOnly: false),
    ];

    /// <summary>Every row whose phase has been implemented compiles (or is rejected) exactly as the table says.</summary>
    [TestMethod]
    public void AcceptanceTable_MatchesImplementedPhases()
    {
        var failures = new List<string>();
        var pending = new List<string>();
        foreach (Row row in Rows)
        {
            if (row.Phase > ImplementedThroughPhase)
            {
                pending.Add($"P{row.Phase} {row.Label}");
                continue;
            }

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

        Console.WriteLine($"parity table: {Rows.Count - pending.Count} rows asserted, {pending.Count} pending");
        foreach (string item in pending)
        {
            Console.WriteLine("  pending: " + item);
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine, failures));
    }

    private sealed record Row(int Phase, string Label, string Layout, bool ExpectAccepted, bool CompileOnly = true);
}
