namespace CStructSharp.Tests;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CStructSharp.Diagnostics;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>
///     Robustness and regression checks for the hand-written <see cref="LayoutParser"/> over the repository's corpus -
///     benchmark fixtures, language contracts, compiler-fixture baselines, explorer demos, documentation snippets, and
///     every layout-looking string literal in this test project - plus thousands of deterministic mutations of them.
///     Every input either parses or fails with a syntax diagnostic; nothing escapes with another exception.
/// </summary>
[TestClass]
public class ParserCorpusTests
{
    private const int MutationsPerSeedCap = 12;
    private const int TotalMutationBudget = 8000;

    private static readonly JsonDocumentOptions DeepJson = new() { MaxDepth = 1024, };

    private static readonly string[] TriviaTokens = [" ", "  ", "\n", "\r\n", "\t", "/* c */", "/**/", "// c\n", " // c\n",];

    private static readonly string[] InterestingTokens =
    [
        " ", "\n", "/*", "*/", "//", "*", "<", ">", "(", ")", "[", "]", "{", "}", ";", ",", ":", "@", "@align(", "#define ",
        "struct ", "union ", "enum ", "typedef ", "const ", "if (", "else ", "switch (", "case ", "default:", "-", "+", "!",
        "~", "0x", "0b", "0o", "_", "1", "=", "==", "&", "&&", "|", "||", "<<", ">>", "u", "L",
    ];

    private static readonly char[] InterestingChars = [' ', '\n', '\r', '\t', '*', '<', '>', '(', ')', '[', ']', '{', '}', ';', ',', ':', '@', '#', '/', '-', '+', '!', '~', '_', '=', '&', '|', '0', '1', '9', 'x', 'b', 'o', 'u', 'L', 'a', 'A', 'é', ' ',];

    /// <summary>Every corpus layout parses, or fails with a syntax diagnostic; most of the corpus is valid.</summary>
    [TestMethod]
    public void Corpus_ParsesOrReportsASyntaxError()
    {
        IReadOnlyList<(string Id, string Source)> corpus = LoadCorpus();
        Assert.IsGreaterThan(400, corpus.Count, "corpus size");

        int accepted = corpus.Count(entry => ParsesOrFailsCleanly(entry.Id, entry.Source));
        Assert.IsGreaterThan(300, accepted, "accepted corpus entries");
    }

    /// <summary>
    ///     Deterministic character-level mutations of the corpus (flip, replace, insert, delete, swap, duplicate a
    ///     range) either parse or fail with a syntax diagnostic: no other exception escapes the parser.
    /// </summary>
    /// <remarks>
    ///     This is where token-boundary quirks show up: keyword prefixes, pointer stars glued to names, signs in
    ///     literals, comments in odd places. The seed is fixed so a failure reproduces.
    /// </remarks>
    [TestMethod]
    public void MutatedCorpus_ParsesOrReportsASyntaxError()
    {
        IReadOnlyList<(string Id, string Source)> corpus = LoadCorpus();
        var random = new Random(0x1E12);
        int budget = TotalMutationBudget;
        int perSeed = Math.Max(1, Math.Min(MutationsPerSeedCap, TotalMutationBudget / corpus.Count));
        int accepted = 0;
        int rejected = 0;
        foreach ((string id, string source) in corpus)
        {
            for (int iteration = 0; iteration < perSeed && budget > 0; iteration++, budget--)
            {
                if (ParsesOrFailsCleanly($"{id}#{iteration}", Mutate(source, random)))
                {
                    accepted++;
                }
                else
                {
                    rejected++;
                }
            }
        }

        Assert.IsGreaterThan(100, accepted, "accepted mutations");
        Assert.IsGreaterThan(100, rejected, "rejected mutations");
    }

    /// <summary>
    ///     Hand-picked spellings that exercise the token conventions one by one keep their parse trees (or their
    ///     rejection): prefix keywords, trivia around tokens and literals, enum member forms, optional and required
    ///     semicolons, and comments inside every construct.
    /// </summary>
    [TestMethod]
    public void TokenConventions_KeepTheirTrees()
    {
        string[] spellings =
        [
            "structroot { uint8 a; };",
            "struct root { uint8 a; }",
            "struct root { uint8 a; } ;",
            "enum e { A, B }",
            "enum e { A, B };",
            "enum e { A = /*c*/ 1, B };",
            "enum e { A = /*c*/ B, C };",
            "enum e { A /*c*/ = 1 };",
            "enum e { };",
            "enum e { A, };",
            "enumx { A };",
            "typedef union tag { uint8 a; }; alias;",
            "typedef union tag { uint8 a; } alias;",
            "typedef struct tag { uint8 a; }; alias;",
            "typedef struct alias;",
            "typedef union foo;",
            "typedefint x;",
            "typedef uint32 < le;",
            "typedef uint8 * * pp;",
            "#defineX 1\nstruct root { uint8 a[X]; };",
            "#define A 1 #define B 2",
            "#define A (1)(2)",
            "struct root { uint8 a[+5]; };",
            "struct root { uint8 a[-5]; };",
            "struct root { uint8 a[- 5]; };",
            "struct root { uint8 a[1 + +5]; };",
            "struct root { uint8 a[0x]; };",
            "struct root { uint8 a[0b12]; };",
            "struct root { uint8 a[0o9]; };",
            "struct root { uint8 a[08]; };",
            "struct root { uint8 a[1_]; };",
            "struct root { uint8 a[_1]; };",
            "struct root { uint8 a[0X1f]; };",
            "struct root { uint8 a[0B1]; };",
            "struct root { uint8 a[0O7]; };",
            "struct root { uint8 a[1ULL]; };",
            "struct root { uint8 a[1u2]; };",
            "struct root { uint8 a[f(1)(2)]; };",
            "struct root { uint8 a[f()]; };",
            "struct root { uint8 a[f(1,)]; };",
            "struct root { uint8 a[(1)(2)]; };",
            "struct root { uint8 a[!-~1]; };",
            "struct root { uint8 a[1 & & 1]; };",
            "struct root { uint8 a[1 <<< 1]; };",
            "struct root { uint8 a[1 < < 1]; };",
            "struct root { uint8 a[1 <= 1 >= 0 == 1 != 0]; };",
            "struct root { uint8 a[1 || 0 && 1 | 0 & 1]; };",
            "struct root { uint8 a[a << 1 >> 1 + 1 - 1 * 1 / 1]; };",
            "struct root { uint8 a[1 +]; };",
            "struct root { uint8 a[]; uint8 b[][2]; };",
            "struct root { uint8 a[2][]; };",
            "struct root { uint8 a[2][3]; };",
            "struct root { uint8 a[ ]; };",
            "struct root { uint8 a[/*c*/]; };",
            "struct root { uint8*a; };",
            "struct root { uint8 *a; };",
            "struct root { uint8* a; };",
            "struct root { uint8 * a; };",
            "struct root { uint8 * * a; };",
            "struct root { uint8 a, *b, c[2], d:2, :3, e @4, f @align(2); };",
            "struct root { uint8 a, ; };",
            "struct root { uint8 a,, b; };",
            "struct root { uint8 :3; };",
            "struct root { uint8 flag:1, :3, other:4; };",
            "struct root { uint8; };",
            "struct root { foo; };",
            "struct root { const uint8 a; };",
            "struct root { constant a; };",
            "struct root { uint8 const; };",
            "struct root { uint8 * const a; };",
            "struct root { const volatile restrict uint8 a; };",
            "struct root { struct child value; };",
            "struct root { structure_t value; };",
            "struct root { enumerated value; };",
            "struct root { enum color value; };",
            "struct root { union u value; };",
            "struct root { union { uint8 a; } u; };",
            "struct root { struct { uint8 a; } inner; };",
            "struct root { struct { uint8 a; }; };",
            "struct root { struct @align(4) { uint8 a; } inner; };",
            "struct root { struct @align(4) child value; };",
            "struct root { struct{uint8 a;}inner; };",
            "struct root { struct { uint8 a } inner; };",
            "struct root { iface_t x; if (x) { uint8 a; } };",
            "struct root { uint8 x; if (x) { uint8 a; } else { uint8 b; } };",
            "struct root { uint8 x; if(x){uint8 a;}else{uint8 b;} };",
            "struct root { uint8 x; if (x) { uint8 a; } elsewhere_t b; };",
            "struct root { uint8 x; if (x) { uint8 a } };",
            "struct root { uint8 x; if x { uint8 a; } };",
            "struct root { uint8 x; switch (x) { case 1: { uint8 a; } case 2: { uint8 b; } default: { uint8 c; } } };",
            "struct root { uint8 x; switch (x) { case1: { uint8 a; } } };",
            "struct root { uint8 x; switch (x) { case 1: { uint8 a; } case 1: { uint8 b; } } };",
            "struct root { uint8 x; switch (x) { } };",
            "struct root { uint8 x; switch (x) { default: { } } };",
            "struct root { uint8 x; switch (x) { case 1: { if (x) { uint8 a; } } } };",
            "struct root { uint8 x; if (x) { switch (x) { case 1: { uint8 a; } } } };",
            "struct root { uint8 x; if (x) { if (x) { uint8 a; } else { uint8 b; } } };",
            "struct root @align(8) { uint8 a; };",
            "struct root @align (8) { uint8 a; };",
            "struct root @alignment { uint8 a; };",
            "struct root { uint8 a @alignment; };",
            "struct root { uint8 a @align(1 +); };",
            "struct root { uint8 a @; };",
            "struct root { uint8 a @4 @align(2); };",
            "union root { uint8 a; uint16 b; }",
            "union root { uint8 a; if (a) { uint8 b; } };",
            "union root @align(4) { uint8 a; };",
            "typedef struct { uint8 a; } t;",
            "typedef struct @align(4) { uint8 a; } t;",
            "typedef struct tag @align(4) { uint8 a; } t;",
            "typedef struct tag { uint8 a; }",
            "typedef union { uint8 a; } t;",
            "typedef union { uint8 a; if (a) { uint8 b; } } t;",
            "typedef struct tag alias;",
            "typedef structX Y;",
            "typedef unionfoo;",
            "typedef struct { uint8 a; } ;",
            "// c \r struct root { uint8 a; };",
            "// c \n struct root { uint8 a; };",
            "/* c */ struct root { uint8 a; };",
            "/* c struct root { uint8 a; };",
            "/*/ struct root { uint8 a; };",
            "/**/struct root { uint8 a; };",
            "struct root { uint8 a; } // trailing",
            "struct root { uint8 a; } /* trailing */",
            "struct root { uint8 a; } trailing",
            "struct/*c*/root{uint8/*c*/a/*c*/;};",
            "struct root { uint8 a; };struct other { root r; };",
            "struct root { uint8 a; }; struct other { root r; };",
            "struct résumé { uint8 ä; };",
            "struct root { uint8 a; }; \0",
            string.Empty,
            "   ",
            "struct",
            "struct root",
            "struct root {",
            "struct root { uint8 a;",
            "struct root { uint8 a; }; struct",
            "struct root { uint8 a[- (1)]; };",
            "struct root { uint8 a[- x]; };",
            "struct root { uint8 a[! (x)]; };",
            "struct root { uint8 a[~ x]; };",
            "struct root { uint8 a[0x1UL]; };",
            "struct root { uint8 a[0b1u]; };",
            "struct root { uint8 a[0o7L]; };",
            "struct root { uint8 a[0x@]; };",
            "struct root { uint8 a[0xg]; };",
            "struct root { uint8 a[0x_]; };",
            "struct root { uint8 a[0b_1]; };",
            "struct root { uint8 a[1 | 0]; };",
            "struct root { uint8 a[1 & 0]; };",
            "struct root { uint8 a[1 |0]; };",
            "struct root { uint8 a[1|0]; };",
            "struct root { uint8 a[1 ||0]; };",
            "struct root { uint8 a[1&&0]; };",
            "struct root { uint8 a[1 = 0]; };",
            "struct root { uint8 a[1 ! 0]; };",
            "struct root { uint8 a[1 <=> 0]; };",
            "struct root { uint8 a[1 >= 0 <= 1 > 0 < 1]; };",
            "struct root { uint8 a[é]; };",
            "struct root { uint8 €; };",
            "struct root { uint8 a€; };",
            "struct éa { uint8 a; };",
            "struct root { uint8 a; };\u007F",
            "struct root { uint8 a; } /",
            "struct root { uint8 a; } /x",
            "#define é 1",
            "typedef union tag { uint8 a; if (a) { uint8 b; } } alias;",
            "typedef struct tag { uint8 a; if (a) { uint8 b; } } alias;",
            "typedef union { uint8 a; if (a) { uint8 b; } } alias;",
            "typedef uint8 name;",
            "typedef uint8 * name;",
            "struct root { a * b c; };",
            "struct root { * uint8 a; };",
            "struct root { uint8 * * a; uint8 b; };",
            "struct root { enum ; };",
            "struct root { const ; };",
            "struct root { volatile restrict uint8 a; };",
            "struct root { uint8 a, restrict b; };",
            "enum e { A = 1 /*c*/ , B };",
            "enum e { A /*c*/ , /*c*/ B /*c*/ };",
            "enum e : /*c*/ uint16 /*c*/ { A };",
            "struct /*c*/ root /*c*/ @align(2) /*c*/ { };",
            "struct root { struct /*c*/ @align(2) /*c*/ { uint8 a; } /*c*/ n /*c*/ ; };",
            "#define /*c*/ X /*c*/ 1 /*c*/",
            "typedef /*c*/ struct /*c*/ { uint8 a; } /*c*/ t /*c*/ ;",
            "typedef /*c*/ struct /*c*/ tag /*c*/ @align(2) /*c*/ { uint8 a; } /*c*/ ; /*c*/ t;",
            "typedef /*c*/ uint8 /*c*/ * /*c*/ * /*c*/ p /*c*/ ;",
            "enum /*c*/ e /*c*/ { /*c*/ } /*c*/ ;",
            "struct root { uint8 x; if /*c*/ (x) /*c*/ { } /*c*/ else /*c*/ { } };",
            "struct root { uint8 x; switch /*c*/ (x) /*c*/ { case /*c*/ 1 /*c*/ : /*c*/ { } default /*c*/ : /*c*/ { } } };",
            "struct root { uint8 a /*c*/ [ /*c*/ 2 /*c*/ ] /*c*/ : /*c*/ 2 /*c*/ @ /*c*/ 4 /*c*/ , /*c*/ b ; };",
        ];

        string path = Path.Combine(TestFixtures.RepositoryRoot, "tests", "CStructSharpTests", "ParserTokenConventions.json");
        using JsonDocument expected = JsonDocument.Parse(File.ReadAllText(path), DeepJson);
        Assert.HasCount(spellings.Length, expected.RootElement.EnumerateObject().ToArray(), "every spelling has an expected tree");
        foreach (string spelling in spellings)
        {
            Assert.AreEqual(expected.RootElement.GetProperty(spelling).GetString(), TryDump(spelling), spelling);
        }
    }

    /// <summary>A block comment may contain a lone <c>*</c>, as the documented comment grammar allows.</summary>
    [TestMethod]
    public void BlockComments_MayContainLoneStars()
    {
        const string layout = "/* a * b ** c */ struct root { uint8 a; /* pointer *p */ };";
        IReadOnlyList<CStructElement> elements = LayoutParser.ParseLayout(layout);
        Assert.HasCount(1, elements);
        _ = new CStruct(layout);
    }

    /// <summary>An alignment annotation requires an expression; an empty one is rejected, whatever comments it contains.</summary>
    /// <param name="source">An empty alignment annotation, possibly containing comment trivia.</param>
    [TestMethod]
    [DataRow("struct Root8 { uint *p @align( ); };")]
    [DataRow("enum moDe : uint8 { A=1, B=2 }; struct r_oot { mode values[2] @align// \t\n(); uint8 tail; };")]
    [DataRow("struct root { uint8 value @align /* before */ ( /* inside */ ); };")]
    [DataRow("struct root { uint8 value @align(// inside\r\n); };")]
    public void EmptyAlignment_IsRejected(string source)
    {
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLayout(source));
        StringAssert.EndsWith(failure.Message, "expected an expression.");
    }

    /// <summary>A valueless define after a declaration ends at its physical line.</summary>
    /// <param name="newline">The LF or CRLF directive terminator.</param>
    [TestMethod]
    [DataRow("\n")]
    [DataRow("\r\n")]
    public void DefineAfterDeclaration_DoesNotConsumeTheNextLine(string newline)
    {
        string source = "struct item { byte value; }; #define item " + newline + "1";

        // The directive creates an empty constant, leaving the next line's number as invalid top-level input.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLayout(source));
        Assert.AreEqual(LayoutParser.SyntaxErrorPrefix + "unexpected '1' at line 2, column 1; expected the end of the layout.", failure.Message);
    }

    /// <summary>Multiplication inside a type-only call must not join both identifiers into a different pointer type.</summary>
    /// <param name="source">A layout whose call argument contains an infix multiplication expression.</param>
    [TestMethod]
    [DataRow("typedef uint8 ho; struct root { uint8 raw[sizeof(h*o)]; };")]
    [DataRow("typedef uint8 ho; struct root { uint8 raw[sizeof(h * o)]; };")]
    [DataRow("struct h { uint8 value; }; struct root { uint8 raw[offsetof(h*o, value)]; };")]
    public void TypeArgument_RejectsMultiplicationInsteadOfJoiningNames(string source)
    {
        // The grammar permits type words followed by pointer stars, never another word after a star.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLayout(source));
        StringAssert.Contains(failure.Message, "unexpected 'o'");
        StringAssert.Contains(failure.Message, "expected ')'");
    }

    /// <summary>Multiword type names and trailing pointer stars remain valid type-only arguments.</summary>
    [TestMethod]
    public void TypeArgument_PreservesMultiwordPointerSpellings()
    {
        var layout = new CStruct("struct root { uint8 raw[sizeof(unsigned int ** /*end*/ )]; };", pointerSize: 4);
        Assert.AreEqual(4, layout.GetStructSizeInBytes("root"));
    }

    /// <summary>A numeric sizeof argument is rejected exactly where a type name is required.</summary>
    /// <param name="source">A sizeof call whose argument is a numeric literal instead of a type name.</param>
    /// <param name="expected">The diagnostic after the syntax-error prefix.</param>
    [TestMethod]
    [DataRow("struct root { uint8 bytes[sizeof(1)]; };", "unexpected '1' at line 1, column 34; expected a type or field name.")]
    [DataRow("struct root {\n uint8 bytes[sizeof(0x10)]; };", "unexpected '0' at line 2, column 21; expected a type or field name.")]
    public void NumericSizeof_IsRejectedAtTheTypeArgumentBoundary(string source, string expected)
    {
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => LayoutParser.ParseLayout(source));
        Assert.AreEqual(LayoutParser.SyntaxErrorPrefix + expected, failure.Message);
    }

    /// <summary>
    ///     Syntax errors name the exact line and column of the first unexpected character, what was found, and
    ///     what was expected.
    /// </summary>
    /// <remarks>
    ///     Lines count from one at every <c>\n</c>; columns count characters from one. The end of the text and
    ///     control characters are described in words so a message never contains an invisible character.
    /// </remarks>
    [TestMethod]
    public void SyntaxErrors_NameLineColumnFoundAndExpected()
    {
        (string Source, string Detail)[] cases =
        [
            ("struct root {\n  uint8 a\n};", "unexpected '}' at line 3, column 1; expected ';'."),
            ("struct root { uint8 a; } trailing", "unexpected end of input at line 1, column 34; expected ';'."),
            ("struct root { uint8 a; } trailing garbage", "unexpected 'g' at line 1, column 35; expected ';'."),
            ("struct root { uint8 a;", "unexpected end of input at line 1, column 23; expected '}'."),
            ("struct", "unexpected end of input at line 1, column 7; expected an identifier."),
            ("struct root { uint8 a[1 +]; };", "unexpected ']' at line 1, column 26; expected an expression."),
            ("struct root { uint8 a; } \u0001", "unexpected '\\u0001' at line 1, column 26; expected the end of the layout."),
            ("struct root { uint8 a; }\t\r\n\n\t;;", "unexpected ';' at line 3, column 3; expected the end of the layout."),
            ("/* open", "unexpected end of input at line 1, column 8; expected the end of the block comment."),
            ("enum e { A = }", "unexpected '}' at line 1, column 14; expected an expression."),
            ("struct root { uint8 a, ; };", "A declarator must have a name, a bit width, or both."),
            ("struct root { uint8 a[][2]; };", "An unsized array dimension ([]) is allowed only as the sole dimension of a one-dimensional array."),
        ];

        foreach ((string source, string detail) in cases)
        {
            CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
                () => LayoutParser.ParseLayout(source), source);
            Assert.AreEqual(LayoutParser.SyntaxErrorPrefix + detail, exception.Message, source);
            Assert.AreEqual(exception.Message, Assert.Throws<CStructLayoutException>(() => new CStruct(source)).Message, source);
        }

        CStructLayoutException duplicate = Assert.Throws<CStructLayoutException>(
            () => LayoutParser.ParseLayout("struct root { uint8 x; switch (x) { case 1: { } case 1: { } } };"));
        Assert.AreEqual("Duplicate switch case.", duplicate.Message);
    }

    /// <summary>
    ///     The production-level entry points reject null, require the whole text to be consumed, and name what
    ///     they expected.
    /// </summary>
    /// <remarks>
    ///     These entry points exist for tests and tooling; each is exercised once on a valid text with leading
    ///     trivia, once with trailing garbage, and once with nothing usable at all.
    /// </remarks>
    [TestMethod]
    public void EntryPoints_ValidateInputAndConsumeEverything()
    {
        Assert.Throws<ArgumentNullException>(() => LayoutParser.ParseLayout(null!));
        Assert.Throws<ArgumentNullException>(() => LayoutParser.ParseElement(null!));
        Assert.Throws<ArgumentNullException>(() => LayoutParser.ParseExpression(null!));
        Assert.Throws<ArgumentNullException>(() => LayoutParser.ParseLiteral(null!));
        Assert.Throws<ArgumentNullException>(() => LayoutParser.ParseDigits(null!, 10));
        Assert.Throws<ArgumentNullException>(() => LayoutParser.ParseEnumValue(null!));
        Assert.Throws<ArgumentNullException>(() => LayoutParser.ParseEnumValues(null!));
        Assert.Throws<ArgumentNullException>(() => LayoutParser.ParseEnumValuesInBrackets(null!));
        Assert.Throws<ArgumentNullException>(() => LayoutParser.ParseFieldGroup(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => LayoutParser.ParseLiteral("1", 3));

        Assert.AreEqual("root", LayoutParser.ParseElement(" /*c*/ struct root { uint8 a; }; ").Name.Name);
        Assert.IsInstanceOfType<Syntax.BinaryOp>(LayoutParser.ParseExpression(" /*c*/ (1) + x "));
        Assert.AreEqual("Blue", LayoutParser.ParseEnumValue("  Blue =4 ").Name.Name);
        Assert.HasCount(2, LayoutParser.ParseEnumValues(" A, B "));
        Assert.HasCount(0, LayoutParser.ParseEnumValuesInBrackets("{ }"));
        Assert.HasCount(2, LayoutParser.ParseFieldGroup("uint8 a, b; "));
        Assert.AreEqual("12", LayoutParser.ParseDigits("1_2x", 10));

        AssertFails(() => LayoutParser.ParseElement("uint8 x;"), "unexpected 'u' at line 1, column 1; expected a struct, union, typedef, enum, or #define declaration.");
        AssertFails(() => LayoutParser.ParseElement("struct root { uint8 a; }; x"), "unexpected 'x' at line 1, column 27; expected the end of the layout.");
        AssertFails(() => LayoutParser.ParseExpression("1 + 2 3"), "unexpected '3' at line 1, column 7; expected the end of the layout.");
        AssertFails(() => LayoutParser.ParseLiteral("x"), "unexpected 'x' at line 1, column 1; expected an integer literal.");
        AssertFails(() => LayoutParser.ParseLiteral("\t", 16), "unexpected '\\t' at line 1, column 1; expected an integer literal.");
        AssertFails(() => LayoutParser.ParseDigits("x", 10), "unexpected 'x' at line 1, column 1; expected a digit.");
        AssertFails(() => LayoutParser.ParseDigits("\n", 2), "unexpected '\\n' at line 1, column 1; expected a digit.");
        AssertFails(() => LayoutParser.ParseDigits("\r1", 8), "unexpected '\\r' at line 1, column 1; expected a digit.");
        AssertFails(() => LayoutParser.ParseEnumValues("A, B }"), "unexpected '}' at line 1, column 6; expected the end of the layout.");
        AssertFails(() => LayoutParser.ParseEnumValuesInBrackets("{ A } ;"), "unexpected ';' at line 1, column 7; expected the end of the layout.");
        AssertFails(() => LayoutParser.ParseFieldGroup("uint8 a; uint8 b;"), "unexpected 'u' at line 1, column 10; expected the end of the layout.");
        AssertFails(() => LayoutParser.ParseLayout("struct root { enum ; };"), "unexpected ';' at line 1, column 20; expected a field type.");
        AssertFails(() => LayoutParser.ParseLayout("struct root { const ; };"), "unexpected ';' at line 1, column 21; expected an identifier.");
    }

    /// <summary>Asserts that a parse fails with exactly the syntax-error prefix followed by <paramref name="detail"/>.</summary>
    private static void AssertFails(Action parse, string detail)
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(parse);
        Assert.AreEqual(LayoutParser.SyntaxErrorPrefix + detail, exception.Message);
    }

    /// <summary>Parses one input and checks that a rejection is a syntax diagnostic, never another exception.</summary>
    /// <param name="id">The diagnostic identifier of this input.</param>
    /// <param name="source">The layout text.</param>
    /// <returns>Whether the parser accepted the input.</returns>
    private static bool ParsesOrFailsCleanly(string id, string source)
    {
        try
        {
            _ = LayoutParser.ParseLayout(source);
            return true;
        }
        catch (CStructLayoutException exception)
        {
            Assert.IsTrue(
                exception.Message.StartsWith(LayoutParser.SyntaxErrorPrefix, StringComparison.Ordinal) ||
                exception.Message == "Duplicate switch case.",
                $"[{id}] unexpected message for {Escape(source)}: {exception.Message}");
            return false;
        }
    }

    /// <summary>Returns the parse tree of an input, or null when the parser rejects it.</summary>
    /// <param name="source">The layout text.</param>
    /// <returns>The dumped tree, or null.</returns>
    private static string? TryDump(string source)
    {
        try
        {
            return Dump(LayoutParser.ParseLayout(source));
        }
        catch (CStructLayoutException)
        {
            return null;
        }
    }

    /// <summary>Renders a source as a JSON string, cut at 400 characters, for failure messages.</summary>
    private static string Escape(string source)
    {
        return JsonSerializer.Serialize(source.Length > 400 ? source[..400] + "…" : source);
    }

    /// <summary>Applies one to six random character-level edits (flip, replace, insert, delete, swap, duplicate, token or trivia insertion).</summary>
    private static string Mutate(string basis, Random random)
    {
        var chars = new List<char>(basis);
        int operations = 1 + random.Next(6);
        for (int operation = 0; operation < operations; operation++)
        {
            switch (random.Next(9))
            {
            case 0 when chars.Count > 0:
                int flipIndex = random.Next(chars.Count);
                chars[flipIndex] = (char)(chars[flipIndex] ^ (1 << random.Next(7)));
                break;
            case 1 when chars.Count > 0:
                chars[random.Next(chars.Count)] = RandomInterestingChar(random);
                break;
            case 2:
                chars.Insert(random.Next(chars.Count + 1), RandomInterestingChar(random));
                break;
            case 3 when chars.Count > 1:
                chars.RemoveAt(random.Next(chars.Count));
                break;
            case 4 when chars.Count > 1:
                int first = random.Next(chars.Count);
                int second = random.Next(chars.Count);
                (chars[first], chars[second]) = (chars[second], chars[first]);
                break;
            case 5 when chars.Count > 0:
                int source = random.Next(chars.Count);
                int count = Math.Min(1 + random.Next(8), chars.Count - source);
                chars.InsertRange(random.Next(chars.Count + 1), chars.GetRange(source, count));
                break;
            case 6 when chars.Count > 0:
                string token = InterestingTokens[random.Next(InterestingTokens.Length)];
                chars.InsertRange(random.Next(chars.Count + 1), token);
                break;
            case 7:
                // Trivia insertion is the benign mutation: it keeps most inputs valid and probes every token boundary.
                string trivia = TriviaTokens[random.Next(TriviaTokens.Length)];
                chars.InsertRange(random.Next(chars.Count + 1), trivia);
                break;
            case 8 when chars.Count > 1:
                int spaceIndex = random.Next(chars.Count);
                if (char.IsWhiteSpace(chars[spaceIndex]))
                {
                    chars.RemoveAt(spaceIndex);
                }

                break;
            }
        }

        return new string(chars.ToArray());
    }

    /// <summary>Returns a printable ASCII character, or more often one of the characters that matter to the grammar.</summary>
    private static char RandomInterestingChar(Random random)
    {
        return random.Next(3) == 0
                   ? (char)random.Next(32, 127)
                   : InterestingChars[random.Next(InterestingChars.Length)];
    }

    /// <summary>Collects every distinct layout in the repository's fixtures, contracts, demos, documentation and test literals.</summary>
    private static IReadOnlyList<(string Id, string Source)> LoadCorpus()
    {
        string root = TestFixtures.RepositoryRoot;
        var corpus = new List<(string Id, string Source)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        // Adds a source once, under the identifier of its first occurrence.
        void Add(string id, string? source)
        {
            if (source is not null && seen.Add(source))
            {
                corpus.Add((id, source));
            }
        }

        foreach (string file in Directory.GetFiles(Path.Combine(root, "benchmarks", "fixtures", "cases"), "*.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file), DeepJson);
            Add("fixture:" + Path.GetFileNameWithoutExtension(file), document.RootElement.GetProperty("definition").GetString());
        }

        using (JsonDocument portable = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts", "language", "portable-v1.json"))))
        {
            foreach (JsonElement example in portable.RootElement.GetProperty("layoutExamples").EnumerateArray())
            {
                Add("portable:" + example.GetProperty("id").GetString(), example.GetProperty("definition").GetString());
            }
        }

        using (JsonDocument manual = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "contracts", "language", "manual-fixtures-v1.json"))))
        {
            foreach (JsonElement pair in manual.RootElement.GetProperty("featurePairs").EnumerateArray())
            {
                AddDefinitions("manual:" + pair.GetProperty("id").GetString(), pair, Add);
            }
        }

        foreach (string file in Directory.GetFiles(Path.Combine(root, "contracts", "quality", "compiler-fixtures", "baselines"), "*.json").Order(StringComparer.Ordinal))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(file), DeepJson);
            AddDefinitions("compiler-fixture:" + Path.GetFileNameWithoutExtension(file), document.RootElement, Add);
        }

        // Only committed sources: the corpus, and with it the seeded mutation stream, must be the same on every
        // machine. The generated demo catalog restates the test literals below, and a docs checkout may carry
        // node_modules, the built site, and the exported recipes.
        foreach (string file in Directory.GetFiles(Path.Combine(root, "docs"), "*.md", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (relative.Contains("/node_modules/", StringComparison.Ordinal) ||
                relative.StartsWith("docs/_site/", StringComparison.Ordinal) ||
                relative.StartsWith("docs/examples/recipes/", StringComparison.Ordinal))
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (Match match in Regex.Matches(text, "```[a-zA-Z]*\\r?\\n(.*?)```", RegexOptions.Singleline))
            {
                string block = match.Groups[1].Value;
                if (LooksLikeLayout(block))
                {
                    Add("doc:" + Path.GetRelativePath(root, file) + ":" + match.Index, block);
                }
            }
        }

        // Every test source in the feature folders, not the build output under bin/ and obj/.
        string testsRoot = Path.Combine(root, "tests", "CStructSharpTests");
        foreach (string file in Directory.GetFiles(testsRoot, "*.cs", SearchOption.AllDirectories).Order(StringComparer.Ordinal))
        {
            string first = Path.GetRelativePath(testsRoot, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)[0];
            if (Path.GetFileName(file) == nameof(ParserCorpusTests) + ".cs" || first is "bin" or "obj")
            {
                continue;
            }

            string text = File.ReadAllText(file);
            foreach (string literal in ExtractStringLiterals(text))
            {
                if (LooksLikeLayout(literal))
                {
                    Add("test-literal:" + Path.GetFileName(file), literal);
                }
            }
        }

        return corpus;
    }

    /// <summary>Adds every layout definition found anywhere in a JSON document.</summary>
    private static void AddDefinitions(string id, JsonElement element, Action<string, string?> add)
    {
        switch (element.ValueKind)
        {
        case JsonValueKind.Object:
            foreach (JsonProperty property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String &&
                    property.Name is "definition" or "layout" or "source" &&
                    LooksLikeLayout(property.Value.GetString()!))
                {
                    add(id + "/" + property.Name, property.Value.GetString());
                }
                else
                {
                    AddDefinitions(id + "/" + property.Name, property.Value, add);
                }
            }

            break;
        case JsonValueKind.Array:
            int index = 0;
            foreach (JsonElement item in element.EnumerateArray())
            {
                AddDefinitions(id + "[" + index++ + "]", item, add);
            }

            break;
        }
    }

    /// <summary>Returns whether a string literal plausibly holds a layout rather than other text.</summary>
    private static bool LooksLikeLayout(string text)
    {
        return text.Contains("struct ", StringComparison.Ordinal) ||
               text.Contains("union ", StringComparison.Ordinal) ||
               text.Contains("enum ", StringComparison.Ordinal) ||
               text.Contains("typedef ", StringComparison.Ordinal) ||
               text.Contains("#define ", StringComparison.Ordinal);
    }

    /// <summary>Extracts regular and verbatim (non-interpolated, non-raw) C# string literals from source text.</summary>
    private static IEnumerable<string> ExtractStringLiterals(string text)
    {
        var results = new List<string>();
        int index = 0;
        while (index < text.Length)
        {
            char current = text[index];
            if (current == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                int newline = text.IndexOf('\n', index);
                index = newline < 0 ? text.Length : newline + 1;
                continue;
            }

            if (current == '\'')
            {
                // Character literal: skip to its closing quote, honoring escapes.
                index++;
                while (index < text.Length && text[index] != '\'')
                {
                    index += text[index] == '\\' ? 2 : 1;
                }

                index++;
                continue;
            }

            if (current == '$' || (current == '@' && index + 1 < text.Length && text[index + 1] == '$'))
            {
                // Interpolated strings are skipped: their braces are format holes, not layout text.
                int quote = text.IndexOf('"', index);
                if (quote < 0)
                {
                    break;
                }

                bool verbatimInterpolated = text.AsSpan(index, quote - index).Contains('@');
                index = SkipString(text, quote, verbatimInterpolated, out _);
                continue;
            }

            if (current == '@' && index + 1 < text.Length && text[index + 1] == '"')
            {
                index = SkipString(text, index + 1, verbatim: true, out string? verbatimValue);
                if (verbatimValue is not null)
                {
                    results.Add(verbatimValue);
                }

                continue;
            }

            if (current == '"')
            {
                if (index + 2 < text.Length && text[index + 1] == '"' && text[index + 2] == '"')
                {
                    // Raw string literal: skip to the closing triple quote.
                    int close = text.IndexOf("\"\"\"", index + 3, StringComparison.Ordinal);
                    index = close < 0 ? text.Length : close + 3;
                    continue;
                }

                index = SkipString(text, index, verbatim: false, out string? value);
                if (value is not null)
                {
                    results.Add(value);
                }

                continue;
            }

            index++;
        }

        return results;
    }

    /// <summary>Reads one C# string literal starting at its opening quote and returns the index after it.</summary>
    private static int SkipString(string text, int openingQuote, bool verbatim, out string? value)
    {
        var builder = new StringBuilder();
        int index = openingQuote + 1;
        while (index < text.Length)
        {
            char current = text[index];
            if (verbatim)
            {
                if (current == '"')
                {
                    if (index + 1 < text.Length && text[index + 1] == '"')
                    {
                        builder.Append('"');
                        index += 2;
                        continue;
                    }

                    value = builder.ToString();
                    return index + 1;
                }

                builder.Append(current);
                index++;
                continue;
            }

            if (current == '"')
            {
                value = builder.ToString();
                return index + 1;
            }

            if (current == '\n')
            {
                break;
            }

            if (current == '\\' && index + 1 < text.Length)
            {
                char escaped = text[index + 1];
                index += 2;
                switch (escaped)
                {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case '0':
                    builder.Append('\0');
                    break;
                case '\\':
                    builder.Append('\\');
                    break;
                case '"':
                    builder.Append('"');
                    break;
                case '\'':
                    builder.Append('\'');
                    break;
                case 'u' when index + 4 <= text.Length:
                    builder.Append((char)Convert.ToInt32(text.Substring(index, 4), 16));
                    index += 4;
                    break;
                default:
                    value = null;
                    return index;
                }

                continue;
            }

            builder.Append(current);
            index++;
        }

        value = null;
        return index;
    }

    /// <summary>
    ///     Renders parsed declarations as a canonical text tree for exact comparison: every property the compiler reads,
    ///     including each body's if/switch groups, the arms conditional members sit in, and the exact and projected values
    ///     of literals.
    /// </summary>
    private static string Dump(IReadOnlyList<CStructElement> elements)
    {
        var writer = new DumpWriter();
        writer.Builder.Append('[');
        foreach (CStructElement element in elements)
        {
            writer.Element(element);
            writer.Builder.Append(';');
        }

        writer.Builder.Append(']');
        return writer.Builder.ToString();
    }

    /// <summary>Writes the canonical text tree of parsed declarations.</summary>
    private sealed class DumpWriter
    {
        private readonly Dictionary<ConditionalGroup, int> groups = new(ReferenceEqualityComparer.Instance);

        public StringBuilder Builder { get; } = new();

        /// <summary>Writes one declaration and everything it contains.</summary>
        public void Element(CStructElement element)
        {
            switch (element)
            {
            case Struct composite:
                this.Builder.Append(composite.IsUnion ? "union(" : "struct(");
                this.Identifier(composite.Name);
                this.Builder.Append(",align=");
                this.Expr(composite.CompositeAlignmentOverrideExpression);
                this.Builder.Append(",fields=[");
                foreach (Field field in composite.Fields)
                {
                    this.Element(field);
                    this.Builder.Append(';');
                }

                this.Builder.Append("],groups=[");
                foreach (ConditionalGroup group in composite.Groups)
                {
                    this.Group(group);
                    this.Builder.Append(',');
                }

                this.Builder.Append("])");
                this.FieldSuffix(composite);
                break;
            case Field field:
                this.Builder.Append("field(type=");
                this.Identifier(field.Type);
                this.Builder.Append(",name=");
                this.Identifier(field.Name);
                this.Builder.Append(",array=[");
                foreach (Expr dimension in field.ArrayCount)
                {
                    this.Expr(dimension);
                    this.Builder.Append(',');
                }

                this.Builder.Append("],bits=");
                this.Expr(field.BitSizeExpression);
                this.Builder.Append(",ptr=").Append(field.PointerDepth);
                this.Builder.Append(",hint=").Append(field.TypeKeywordHint ?? "null");
                this.Builder.Append(",align=");
                this.Expr(field.AlignmentOverrideExpression);
                this.Builder.Append(",offset=");
                this.Expr(field.OffsetAssertionExpression);
                this.Builder.Append(')');
                this.FieldSuffix(field);
                break;
            case Typedef typedef:
                this.Builder.Append("typedef(");
                this.Identifier(typedef.Name);
                this.Builder.Append(",type=");
                this.Identifier(typedef.Type);
                this.Builder.Append(",struct=");
                if (typedef.Struct is null)
                {
                    this.Builder.Append("null");
                }
                else
                {
                    this.Element(typedef.Struct);
                }

                this.Builder.Append(')');
                break;
            case Syntax.Defines defines:
                this.Builder.Append("define(");
                this.Identifier(defines.Name);
                this.Builder.Append('=');
                this.Expr(defines.Value);
                this.Builder.Append(')');
                break;
            case Syntax.Enum enumeration:
                this.Builder.Append("enum(");
                this.Identifier(enumeration.Name);
                this.Builder.Append(",type=");
                this.Identifier(enumeration.Type);
                this.Builder.Append(",values=[");
                foreach (EnumValue value in enumeration.DeclaredValues)
                {
                    this.Identifier(value.Name);
                    this.Builder.Append('=');
                    this.Expr(value.Value);
                    this.Builder.Append(',');
                }

                this.Builder.Append("])");
                break;
            default:
                this.Builder.Append(element.GetType().Name);
                break;
            }
        }

        /// <summary>Writes the arms a field sits in, outermost first.</summary>
        private void FieldSuffix(Field field)
        {
            this.Builder.Append("{branches=[");
            foreach (ConditionalBranch branch in field.BranchConditions)
            {
                this.Group(branch.Group);
                this.Builder.Append('#').Append(branch.Arm).Append(',');
            }

            this.Builder.Append("]}");
        }

        /// <summary>Writes a group's id, preceded by its selector and labels where it first appears.</summary>
        private void Group(ConditionalGroup group)
        {
            if (!this.groups.TryGetValue(group, out int groupId))
            {
                groupId = this.groups.Count;
                this.groups.Add(group, groupId);
                this.Builder.Append("g").Append(groupId).Append("(sel=");
                this.Expr(group.Selector);
                this.Builder.Append(",labels=");
                if (group.CaseLabels is null)
                {
                    this.Builder.Append("null");
                }
                else
                {
                    this.Builder.Append('[');
                    foreach (Expr label in group.CaseLabels)
                    {
                        this.Expr(label);
                        this.Builder.Append(',');
                    }

                    this.Builder.Append(']');
                }

                this.Builder.Append(')');
            }

            this.Builder.Append("g").Append(groupId);
        }

        /// <summary>Writes an identifier with its pointer stars.</summary>
        private void Identifier(Identifier identifier)
        {
            this.Builder.Append('`').Append(identifier.Name).Append('`');
            if (identifier.PointerDepth != 0)
            {
                this.Builder.Append('*').Append(identifier.PointerDepth);
            }
        }

        /// <summary>Writes an expression tree, or <c>null</c>.</summary>
        private void Expr(Expr? expression)
        {
            switch (expression)
            {
            case null:
                this.Builder.Append("null");
                break;
            case NoneExpr:
                this.Builder.Append("none");
                break;
            case Literal literal:
                this.Builder.Append("lit(").Append(literal.ExactValue).Append('|').Append(literal.Int32Projection).Append(')');
                break;
            case Identifier identifier:
                this.Identifier(identifier);
                break;
            case BinaryOp binary:
                this.Builder.Append("bin(").Append(binary.Type).Append(',');
                this.Expr(binary.Left);
                this.Builder.Append(',');
                this.Expr(binary.Right);
                this.Builder.Append(')');
                break;
            case UnaryOp unary:
                this.Builder.Append("un(").Append(unary.Type).Append(',');
                this.Expr(unary.Expr);
                this.Builder.Append(')');
                break;
            case Call call:
                this.Builder.Append("call(");
                this.Expr(call.Expr);
                this.Builder.Append(",[");
                foreach (Expr argument in call.Arguments)
                {
                    this.Expr(argument);
                    this.Builder.Append(',');
                }

                this.Builder.Append("])");
                break;
            default:
                this.Builder.Append(expression.GetType().Name);
                break;
            }
        }
    }
}
