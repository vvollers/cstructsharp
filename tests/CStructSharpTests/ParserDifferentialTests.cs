namespace CStructSharp.Tests;

using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CStructSharp.Diagnostics;
using CStructSharp.Parsing;
using CStructSharp.Syntax;
using CStructSharp.Tests.Reference;
using Pidgin;

/// <summary>
///     Differential oracle for the hand-written <see cref="LayoutParser"/>: every layout in the repository's
///     corpus - benchmark fixtures, language contracts, compiler-fixture baselines, explorer demos, documentation
///     snippets, and every layout-looking string literal in this test project - plus thousands of deterministic
///     mutations of them, is parsed by both the new parser and the frozen Pidgin grammar it replaced. Everything the
///     reference accepts, the current parser must accept with a structurally identical tree; the reference grammar
///     is a frozen subset, so it may reject sources the extended language now accepts.
/// </summary>
[TestClass]
public class ParserDifferentialTests
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

    /// <summary>
    ///     Every corpus layout parses identically through the hand-written parser and the reference grammar.
    /// </summary>
    /// <remarks>
    ///     A mismatch names the corpus entry and shows both trees (or the differing verdicts). The block-comment
    ///     exceptions cover documented grammar corrections: lone comment stars and empty alignment arguments.
    /// </remarks>
    [TestMethod]
    public void Corpus_ParsesIdenticallyThroughBothParsers()
    {
        IReadOnlyList<(string Id, string Source)> corpus = LoadCorpus();
        Assert.IsGreaterThan(400, corpus.Count, "corpus size");

        var failures = new List<string>();
        int accepted = 0;
        foreach ((string id, string source) in corpus)
        {
            string? failure = Compare(id, source, out bool wasAccepted);
            if (failure is not null)
            {
                failures.Add(failure);
            }

            if (wasAccepted)
            {
                accepted++;
            }
        }

        Assert.IsGreaterThan(300, accepted, "accepted corpus entries");
        Assert.IsEmpty(failures, string.Join(Environment.NewLine + Environment.NewLine, failures.Take(10)));
    }

    /// <summary>
    ///     Deterministic character-level mutations of the corpus (flip, replace, insert, delete, swap, duplicate a
    ///     range) keep both parsers in agreement on accept/reject and on the produced tree.
    /// </summary>
    /// <remarks>
    ///     This is where token-boundary quirks show up: keyword prefixes, pointer stars glued to names, signs in
    ///     literals, comments in odd places. The seed is fixed so a failure reproduces.
    /// </remarks>
    [TestMethod]
    public void MutatedCorpus_ParsesIdenticallyThroughBothParsers()
    {
        IReadOnlyList<(string Id, string Source)> corpus = LoadCorpus();
        var random = new Random(0x1E12);
        var failures = new List<string>();
        int budget = TotalMutationBudget;
        int perSeed = Math.Max(1, Math.Min(MutationsPerSeedCap, TotalMutationBudget / corpus.Count));
        int accepted = 0;
        int rejected = 0;
        foreach ((string id, string source) in corpus)
        {
            for (int iteration = 0; iteration < perSeed && budget > 0; iteration++, budget--)
            {
                string mutated = Mutate(source, random);
                string? failure = Compare($"{id}#{iteration}", mutated, out bool wasAccepted);
                if (failure is not null)
                {
                    failures.Add(failure);
                }

                if (wasAccepted)
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
        Assert.IsEmpty(failures, string.Join(Environment.NewLine + Environment.NewLine, failures.Take(10)));
    }

    /// <summary>
    ///     Hand-picked spellings that exercise the reference grammar's token conventions one by one.
    /// </summary>
    /// <remarks>
    ///     Each spelling documents a convention the rewrite had to reproduce: prefix keyword matching, trivia after
    ///     tokens and before literals, whitespace-only skipping after an enum <c>=</c>, unary minus versus literal
    ///     sign, optional/required semicolons, and the union typedef that swallows its own semicolon.
    /// </remarks>
    [TestMethod]
    public void TokenConventions_MatchTheReferenceGrammar()
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

        var failures = new List<string>();
        foreach (string spelling in spellings)
        {
            string? failure = Compare(spelling, spelling, out _);
            if (failure is not null)
            {
                failures.Add(failure);
            }
        }

        Assert.IsEmpty(failures, string.Join(Environment.NewLine + Environment.NewLine, failures));
    }

    /// <summary>
    ///     A documented deviation: a block comment may contain a lone <c>*</c>.
    /// </summary>
    /// <remarks>
    ///     The reference grammar's block-comment terminator committed to <c>*</c> and then failed on the next
    ///     character; the documented grammar (<c>block-comment = "/*", { block-comment-character }, "*/"</c>) never
    ///     had that restriction, so the rewrite follows the documentation.
    /// </remarks>
    [TestMethod]
    public void BlockComments_MayContainLoneStars()
    {
        const string layout = "/* a * b ** c */ struct root { uint8 a; /* pointer *p */ };";
        Assert.IsFalse(ReferenceAccepts(layout));
        IReadOnlyList<CStructElement> elements = CStructDefinitionParser.ParseLayout(layout);
        Assert.HasCount(1, elements);
        _ = new CStruct(layout);
    }

    /// <summary>The old parser backtracks from empty alignment into an offset call; alignment requires an expression.</summary>
    [TestMethod]
    public void EmptyAlignment_IsRejectedInsteadOfBecomingAnOffsetCall()
    {
        const string layout = "struct Root8 { uint *p @align( ); };";
        Assert.IsTrue(ReferenceAccepts(layout));
        Assert.Throws<CStructLayoutException>(() => new CStruct(layout));
        Assert.IsNull(Compare("empty-alignment", layout, out bool accepted));
        Assert.IsFalse(accepted);
    }

    /// <summary>The frozen call grammar accepts numeric sizeof arguments, but the documented type-only grammar rejects them.</summary>
    /// <param name="source">A sizeof call whose argument is a numeric literal instead of a type name.</param>
    [TestMethod]
    [DataRow("struct root { uint8 bytes[sizeof(1)]; };")]
    [DataRow("struct root {\n uint8 bytes[sizeof(0x10)]; };")]
    public void NumericSizeof_IsRejectedAtTheTypeArgumentBoundary(string source)
    {
        Assert.IsTrue(ReferenceAccepts(source));

        // The current parser must reject the literal precisely where a type name is required.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => CStructDefinitionParser.ParseLayout(source));
        Assert.IsTrue(RejectsNumericSizeofArgument(source, failure.Message));
        Assert.IsFalse(RejectsNumericSizeofArgument(source, "unrelated syntax failure"));
        Assert.IsFalse(RejectsNumericSizeofArgument(source, failure.Message.Replace("a type or field name", "an expression", StringComparison.Ordinal)));
        Assert.IsNull(Compare("numeric-sizeof", source, out bool accepted));
        Assert.IsFalse(accepted);
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
                () => CStructDefinitionParser.ParseLayout(source), source);
            Assert.AreEqual(LayoutParser.SyntaxErrorPrefix + detail, exception.Message, source);
            Assert.AreEqual(exception.Message, Assert.Throws<CStructLayoutException>(() => new CStruct(source)).Message, source);
        }

        CStructLayoutException duplicate = Assert.Throws<CStructLayoutException>(
            () => CStructDefinitionParser.ParseLayout("struct root { uint8 x; switch (x) { case 1: { } case 1: { } } };"));
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
        Assert.Throws<ArgumentNullException>(() => CStructDefinitionParser.ParseLayout(null!));
        Assert.Throws<ArgumentNullException>(() => CStructDefinitionParser.ParseElement(null!));
        Assert.Throws<ArgumentNullException>(() => CStructDefinitionParser.ParseExpression(null!));
        Assert.Throws<ArgumentNullException>(() => CStructDefinitionParser.ParseLiteral(null!));
        Assert.Throws<ArgumentNullException>(() => CStructDefinitionParser.ParseDigits(null!, 10));
        Assert.Throws<ArgumentNullException>(() => CStructDefinitionParser.ParseEnumValue(null!));
        Assert.Throws<ArgumentNullException>(() => CStructDefinitionParser.ParseEnumValues(null!));
        Assert.Throws<ArgumentNullException>(() => CStructDefinitionParser.ParseEnumValuesInBrackets(null!));
        Assert.Throws<ArgumentNullException>(() => CStructDefinitionParser.ParseFieldGroup(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => CStructDefinitionParser.ParseLiteral("1", 3));

        Assert.AreEqual("root", CStructDefinitionParser.ParseElement(" /*c*/ struct root { uint8 a; }; ").Name.Name);
        Assert.IsInstanceOfType<Syntax.BinaryOp>(CStructDefinitionParser.ParseExpression(" /*c*/ (1) + x "));
        Assert.AreEqual("Blue", CStructDefinitionParser.ParseEnumValue("  Blue =4 ").Name.Name);
        Assert.HasCount(2, CStructDefinitionParser.ParseEnumValues(" A, B "));
        Assert.HasCount(0, CStructDefinitionParser.ParseEnumValuesInBrackets("{ }"));
        Assert.HasCount(2, CStructDefinitionParser.ParseFieldGroup("uint8 a, b; "));
        Assert.AreEqual("12", CStructDefinitionParser.ParseDigits("1_2x", 10));

        AssertFails(() => CStructDefinitionParser.ParseElement("uint8 x;"), "unexpected 'u' at line 1, column 1; expected a struct, union, typedef, enum, or #define declaration.");
        AssertFails(() => CStructDefinitionParser.ParseElement("struct root { uint8 a; }; x"), "unexpected 'x' at line 1, column 27; expected the end of the layout.");
        AssertFails(() => CStructDefinitionParser.ParseExpression("1 + 2 3"), "unexpected '3' at line 1, column 7; expected the end of the layout.");
        AssertFails(() => CStructDefinitionParser.ParseLiteral("x"), "unexpected 'x' at line 1, column 1; expected an integer literal.");
        AssertFails(() => CStructDefinitionParser.ParseLiteral("\t", 16), "unexpected '\\t' at line 1, column 1; expected an integer literal.");
        AssertFails(() => CStructDefinitionParser.ParseDigits("x", 10), "unexpected 'x' at line 1, column 1; expected a digit.");
        AssertFails(() => CStructDefinitionParser.ParseDigits("\n", 2), "unexpected '\\n' at line 1, column 1; expected a digit.");
        AssertFails(() => CStructDefinitionParser.ParseDigits("\r1", 8), "unexpected '\\r' at line 1, column 1; expected a digit.");
        AssertFails(() => CStructDefinitionParser.ParseEnumValues("A, B }"), "unexpected '}' at line 1, column 6; expected the end of the layout.");
        AssertFails(() => CStructDefinitionParser.ParseEnumValuesInBrackets("{ A } ;"), "unexpected ';' at line 1, column 7; expected the end of the layout.");
        AssertFails(() => CStructDefinitionParser.ParseFieldGroup("uint8 a; uint8 b;"), "unexpected 'u' at line 1, column 10; expected the end of the layout.");
        AssertFails(() => CStructDefinitionParser.ParseLayout("struct root { enum ; };"), "unexpected ';' at line 1, column 20; expected a field type.");
        AssertFails(() => CStructDefinitionParser.ParseLayout("struct root { const ; };"), "unexpected ';' at line 1, column 21; expected an identifier.");
    }

    private static void AssertFails(Action parse, string detail)
    {
        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(parse);
        Assert.AreEqual(LayoutParser.SyntaxErrorPrefix + detail, exception.Message);
    }

    /// <summary>Compares parser trees or verdicts, accounting only for the documented frozen-reference differences.</summary>
    /// <param name="id">The diagnostic identifier of this corpus input.</param>
    /// <param name="source">The exact source supplied to both parsers.</param>
    /// <param name="accepted">Whether the current parser accepted the declaration.</param>
    /// <returns>A mismatch explanation, or null when the input agrees with the comparison contract.</returns>
    private static string? Compare(string id, string source, out bool accepted)
    {
        accepted = false;
        string? referenceDump;
        string? referenceError;
        try
        {
            referenceDump = Dump(PidginReferenceParser.Parser.ParseOrThrow(source).ToArray());
            referenceError = null;
        }
        catch (Exception exception) when (exception is ParseException or FormatException or OverflowException or
                                          InvalidOperationException or ArgumentException or CStructLayoutException)
        {
            referenceDump = null;
            referenceError = exception.GetType().Name + ": " + exception.Message;
        }

        string? candidateDump;
        string? candidateError;
        try
        {
            candidateDump = Dump(CStructDefinitionParser.ParseLayout(source));
            candidateError = null;
        }
        catch (CStructLayoutException exception)
        {
            candidateDump = null;
            candidateError = exception.Message;
            Assert.IsTrue(
                exception.Message.StartsWith(LayoutParser.SyntaxErrorPrefix, StringComparison.Ordinal) ||
                exception.Message == "Duplicate switch case.",
                $"[{id}] unexpected message: {exception.Message}");
        }

        if (referenceDump is null && candidateDump is null)
        {
            return null;
        }

        if (referenceDump is null && HasLoneStarInsideBlockComment(source))
        {
            accepted = true;
            return null;
        }

        // The reference's Try(AlignmentOverride) backtracks and accepts @align() as an offset call.
        // Empty alignment is invalid in the public language; the current parser commits to that diagnostic.
        // Only exempt this rejected spelling, never an accepted candidate or another syntax diagnostic.
        if (referenceDump is not null && candidateDump is null &&
            candidateError?.EndsWith("expected an expression.", StringComparison.Ordinal) == true &&
            Regex.IsMatch(source, @"@align\s*\(\s*\)"))
        {
            return null;
        }

        // The frozen reference grammar matches a keyword as a bare prefix (`structroot` parses as `struct root`),
        // reads a function-like macro's parameter list as a call expression, and skips any trivia - comments and
        // newlines included - between a `#define` name and its value; the parser follows C on all three (a directive
        // ends at its line), so those sources are compared for acceptance only where the divergence is the token or
        // line boundary.
        if (referenceDump is not null &&
            (HasGluedKeyword(source) || HasFunctionLikeMacro(source) || RejectsLineAfterDefine(source, candidateError)))
        {
            accepted = candidateDump is not null;
            return null;
        }

        // The reference reads a sizeof/offsetof argument as an ordinary call expression; the parser reads it as a
        // type spelling (words and pointer stars, so `sizeof(unsigned int)` works) and rejects an operator inside
        // the parentheses at parse time, where evaluation would reject it anyway.
        if (referenceDump is not null && candidateDump is null && HasOperatorInsideTypeArgument(source))
        {
            return null;
        }

        // A numeric sizeof argument is also an expression, not the type-spelling required by the current grammar.
        // Match its exact rejection location and diagnostic; a different failure must still reach the mismatch report.
        if (referenceDump is not null && candidateDump is null && RejectsNumericSizeofArgument(source, candidateError))
        {
            return null;
        }

        // One-way oracle since the dissect-parity work: the frozen reference grammar defines a subset of the
        // language, so a source it rejects may legitimately be accepted by the current parser (typedef declarator
        // lists, top-level anonymous composites, preprocessor lines, inline unions, ...). What must never happen is
        // the reverse, or a different tree for a source both accept.
        if (referenceDump is null && candidateDump is not null)
        {
            accepted = true;
            return null;
        }

        if (referenceDump is null || candidateDump is null)
        {
            return $"[{id}] verdict mismatch{Environment.NewLine}source: {Escape(source)}{Environment.NewLine}" +
                   $"reference: {referenceError ?? "accepted"}{Environment.NewLine}candidate: {candidateError ?? "accepted"}";
        }

        accepted = true;
        if (!string.Equals(referenceDump, candidateDump, StringComparison.Ordinal))
        {
            return $"[{id}] tree mismatch{Environment.NewLine}source: {Escape(source)}{Environment.NewLine}" +
                   $"reference: {referenceDump}{Environment.NewLine}candidate: {candidateDump}";
        }

        return null;
    }

    private static bool ReferenceAccepts(string source)
    {
        try
        {
            _ = PidginReferenceParser.Parser.ParseOrThrow(source).ToArray();
            return true;
        }
        catch (Exception exception) when (exception is ParseException or FormatException or OverflowException or
                                          InvalidOperationException or ArgumentException or CStructLayoutException)
        {
            return false;
        }
    }

    /// <summary>Matches only a numeric sizeof argument rejected exactly at the literal's first character.</summary>
    /// <param name="source">The source being compared with the frozen expression-call grammar.</param>
    /// <param name="candidateError">The current parser's rejection diagnostic.</param>
    /// <returns>Whether the failure is precisely the documented type-only argument restriction.</returns>
    private static bool RejectsNumericSizeofArgument(string source, string? candidateError)
    {
        foreach (Match match in Regex.Matches(source, @"\bsizeof\s*\(\s*(?<number>[0-9][0-9a-fA-FxXbBoO_uUlL]*)\s*\)"))
        {
            int start = match.Groups["number"].Index;
            int line = 1;
            int column = 1;
            for (int index = 0; index < start; index++)
            {
                if (source[index] == '\n')
                {
                    line++;
                    column = 1;
                }
                else
                {
                    column++;
                }
            }

            string expected = LayoutParser.SyntaxErrorPrefix + $"unexpected '{source[start]}' at line {line}, column {column}; expected a type or field name.";
            if (candidateError == expected)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>True when a sizeof/offsetof argument contains an expression operator rather than only a type spelling.</summary>
    private static bool HasOperatorInsideTypeArgument(string source)
    {
        return Regex.IsMatch(source, @"\b(?:sizeof|offsetof)\s*\([^()]*[|&^+\-/%<>!~=?:,][^()]*\)");
    }

    private static bool HasGluedKeyword(string source)
    {
        foreach (string keyword in new[] { "struct", "union", "enum", "typedef", "flag", "#define", "#undef", "#ifdef", "#ifndef", })
        {
            int index = 0;
            while ((index = source.IndexOf(keyword, index, StringComparison.Ordinal)) >= 0)
            {
                int after = index + keyword.Length;
                bool startsToken = index == 0 || !(char.IsLetterOrDigit(source[index - 1]) || source[index - 1] == '_');
                if (startsToken && after < source.Length && (char.IsLetterOrDigit(source[after]) || source[after] == '_'))
                {
                    return true;
                }

                index = after;
            }
        }

        return false;
    }

    /// <summary>True when the source defines a function-like macro (<c>#define NAME(</c>).</summary>
    private static bool HasFunctionLikeMacro(string source)
    {
        int index = 0;
        while ((index = source.IndexOf("#define", index, StringComparison.Ordinal)) >= 0)
        {
            int cursor = index + "#define".Length;
            while (cursor < source.Length && source[cursor] is ' ' or '\t')
            {
                cursor++;
            }

            while (cursor < source.Length && (char.IsLetterOrDigit(source[cursor]) || source[cursor] == '_'))
            {
                cursor++;
            }

            if (cursor < source.Length && source[cursor] == '(')
            {
                return true;
            }

            index = cursor;
        }

        return false;
    }

    /// <summary>
    ///     True when the candidate's syntax diagnostic points at the line after a <c>#define</c> directive: the
    ///     reference read that line as the macro's name or value (<c>#define\n COUNT 2</c>, <c>#define COUNT\n 2</c>),
    ///     the line-scoped parser did not.
    /// </summary>
    /// <param name="source">The exact mutated source being compared.</param>
    /// <param name="candidateError">The candidate's syntax diagnostic, or null when accepted.</param>
    /// <returns>Whether the mismatch is specifically the documented physical directive-line boundary.</returns>
    private static bool RejectsLineAfterDefine(string source, string? candidateError)
    {
        if (candidateError is null)
        {
            return false;
        }

        Match position = Regex.Match(candidateError, @" at line (?<line>\d+), column (?<column>\d+)");
        if (!position.Success)
        {
            return false;
        }

        string[] lines = source.Split('\n');
        int errorLine = int.Parse(position.Groups["line"].Value, CultureInfo.InvariantCulture) - 1;
        if (errorLine >= 0 && errorLine < lines.Length)
        {
            // Diagnostics count LF lines, but a standalone CR also ends a physical directive.
            int column = int.Parse(position.Groups["column"].Value, CultureInfo.InvariantCulture) - 1;
            string prefix = lines[errorLine][..Math.Min(column, lines[errorLine].Length)];
            int carriageReturn = prefix.LastIndexOf('\r');
            if (carriageReturn >= 0 && Regex.IsMatch(prefix[..carriageReturn], @"(?:^|\r)[^\S\r\n]*#\s*define\b[^\r\n]*(?:\r[^\S\r\n]*)*$"))
            {
                return true;
            }
        }

        if (candidateError.EndsWith("expected an identifier on the #define line.", StringComparison.Ordinal) &&
            errorLine >= 0 && errorLine < lines.Length)
        {
            // The directive whose name is missing may be the line's first token or a later one on the same
            // physical line (`#define A 1 #define\r\n B 2`), and a line comment may sit between the directive and
            // the line end (`#define // c\n COUNT`); either way the reference took the name from the next line and
            // the line-scoped parser did not.
            int column = int.Parse(position.Groups["column"].Value, CultureInfo.InvariantCulture) - 1;
            string prefix = lines[errorLine][..Math.Min(column, lines[errorLine].Length)];
            if (Regex.IsMatch(prefix, @"#\s*define[^\S\r\n]*(?://[^\r\n]*)?$"))
            {
                return true;
            }
        }

        for (int line = Math.Min(errorLine, lines.Length) - 1; line >= 0; line--)
        {
            if (lines[line].Trim().Length == 0)
            {
                continue;
            }

            return Regex.IsMatch(lines[line], @"^\s*#\s*define\b");
        }

        return false;
    }

    /// <summary>True when a <c>/* */</c> comment contains a <c>*</c> that is not immediately followed by <c>/</c>.</summary>
    private static bool HasLoneStarInsideBlockComment(string source)
    {
        int index = 0;
        while (index < source.Length)
        {
            if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '/')
            {
                int newline = source.IndexOf('\n', index);
                index = newline < 0 ? source.Length : newline + 1;
                continue;
            }

            if (index + 1 < source.Length && source[index] == '/' && source[index + 1] == '*')
            {
                int close = source.IndexOf("*/", index + 2, StringComparison.Ordinal);
                int end = close < 0 ? source.Length : close;
                for (int inner = index + 2; inner < end; inner++)
                {
                    if (source[inner] == '*')
                    {
                        return true;
                    }
                }

                index = close < 0 ? source.Length : close + 2;
                continue;
            }

            index++;
        }

        return false;
    }

    private static string Escape(string source)
    {
        return JsonSerializer.Serialize(source.Length > 400 ? source[..400] + "…" : source);
    }

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

    private static char RandomInterestingChar(Random random)
    {
        return random.Next(3) == 0
                   ? (char)random.Next(32, 127)
                   : InterestingChars[random.Next(InterestingChars.Length)];
    }

    private static IReadOnlyList<(string Id, string Source)> LoadCorpus()
    {
        string root = FindRepositoryRoot();
        var corpus = new List<(string Id, string Source)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

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

        foreach (string file in Directory.GetFiles(Path.Combine(root, "tests", "CStructSharpTests"), "*.cs").Order(StringComparer.Ordinal))
        {
            if (Path.GetFileName(file) == nameof(ParserDifferentialTests) + ".cs")
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

    private static string FindRepositoryRoot()
    {
        string? directory = AppContext.BaseDirectory;
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory, "CStructSharp.sln")))
            {
                return directory;
            }

            directory = Path.GetDirectoryName(directory);
        }

        throw new DirectoryNotFoundException("CStructSharp.sln not found above " + AppContext.BaseDirectory);
    }

    // Structural dump: every property the compiler reads, including the predicates and branch groups attached to
    // conditional members and the exact/projected values of literals.
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

    private sealed class DumpWriter
    {
        private readonly Dictionary<ConditionalGroup, int> groups = new(ReferenceEqualityComparer.Instance);

        public StringBuilder Builder { get; } = new();

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

                this.Builder.Append("])");
                this.FieldSuffix(composite);
                break;
            case SwitchCaseValidation validation:
                this.Builder.Append("switch-validation(tags=[");
                foreach (Expr tag in validation.Tags)
                {
                    this.Expr(tag);
                    this.Builder.Append(',');
                }

                this.Builder.Append("])");
                this.FieldSuffix(validation);
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

        private void FieldSuffix(Field field)
        {
            this.Builder.Append("{cond=");
            this.Expr(field.Condition);
            this.Builder.Append(",branches=[");
            foreach (ConditionalBranch branch in field.BranchConditions)
            {
                if (!this.groups.TryGetValue(branch.Group, out int groupId))
                {
                    groupId = this.groups.Count;
                    this.groups.Add(branch.Group, groupId);
                    this.Builder.Append("g").Append(groupId).Append("(sel=");
                    this.Expr(branch.Group.Selector);
                    this.Builder.Append(",labels=");
                    if (branch.Group.CaseLabels is null)
                    {
                        this.Builder.Append("null");
                    }
                    else
                    {
                        this.Builder.Append('[');
                        foreach (Expr label in branch.Group.CaseLabels)
                        {
                            this.Expr(label);
                            this.Builder.Append(',');
                        }

                        this.Builder.Append(']');
                    }

                    this.Builder.Append(')');
                }

                this.Builder.Append("g").Append(groupId).Append('#').Append(branch.Arm).Append(',');
            }

            this.Builder.Append("]}");
        }

        private void Identifier(Identifier identifier)
        {
            this.Builder.Append('`').Append(identifier.Name).Append('`');
            if (identifier.PointerDepth != 0)
            {
                this.Builder.Append('*').Append(identifier.PointerDepth);
            }
        }

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
