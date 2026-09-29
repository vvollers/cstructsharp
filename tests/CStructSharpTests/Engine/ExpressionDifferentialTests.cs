namespace CStructSharp.Tests;

using System.Numerics;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>
///     The expression differential (<see cref="ExpressionDifferential"/>): the slot model evaluates every expression of
///     the repository's corpora, and a set of targeted expressions, exactly as the dictionary model does.
/// </summary>
[TestClass]
public class ExpressionDifferentialTests
{
    /// <summary>The corpora the differential runs over.</summary>
    private static readonly string[] CorpusNames = ["Parity", "Benchmarks", "Manual", "Portable", "WellKnown", "Inspector", "Fuzz"];

    /// <summary>Gets the corpus names as data rows.</summary>
    public static IEnumerable<object[]> Corpora => CorpusNames.Select(name => new object[] { name, });

    /// <summary>Gets or sets the test context, which receives the per-corpus counts.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    ///     Every expression of every layout of a corpus - array lengths, <c>@count</c> targets, selectors, definitions and
    ///     each variable on its own - gives the dictionary model's value or failure under no caller variables, the
    ///     corpus's variables, relevant and unrelated caller variables, and captured values of every kind.
    /// </summary>
    /// <param name="corpus">The corpus name.</param>
    [TestMethod]
    [DynamicData(nameof(Corpora))]
    public void CorpusExpressions_EvaluateLikeTheDictionaryModel(string corpus)
    {
        IEnumerable<EngineCorpusCase> cases = corpus switch
        {
            "Parity" => EngineCorpora.Parity.Values,
            "Benchmarks" => EngineCorpora.Benchmarks.Values,
            "Manual" => EngineCorpora.Manual.Values,
            "Portable" => EngineCorpora.Portable.Values,
            "WellKnown" => EngineCorpora.WellKnown.Values,
            "Inspector" => EngineCorpora.Inspector.Values,
            _ => EngineCorpora.Fuzz.Values.SelectMany(target => target),
        };

        var counts = new ExpressionDifferentialCounts();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (EngineCorpusCase item in cases.OrderBy(item => item.Id, StringComparer.Ordinal))
        {
            CStruct layout;
            try
            {
                layout = item.Compile();
            }
            catch (CStructLayoutException)
            {
                continue;
            }

            LayoutCompilation compilation = layout.Compilation;
            CStructCompilationOptions options = compilation.CompilationOptions;
            string variables = item.Variables is null ? string.Empty : string.Join(",", item.Variables.OrderBy(pair => pair.Key, StringComparer.Ordinal));
            string key = string.Join("\n", compilation.Source, options.MaxExpressionNestingDepth, options.MaxExpressionTokens, options.CLongWidth, options.DefaultEnumStorage, variables);
            if (seen.Add(key))
            {
                using IDisposable part = EngineGolden.Part(item.Id);
                counts.Add(ExpressionDifferential.Run(item.Id, compilation, item.Variables));
            }
        }

        this.TestContext.WriteLine(corpus + ": " + counts);
        Assert.IsGreaterThan(0, counts.Expressions, corpus + " has no expressions");
        Assert.AreEqual(counts.Expressions, counts.Native, corpus + ": every layout expression compiles to a slot program");
    }

    /// <summary>
    ///     Random expressions over fields and live and folded definitions, with random slot states (edge literals, out
    ///     of domain, wide, not a number, undefined, and live expressions that may form cycles), random caller variables,
    ///     and tight depth and work limits, evaluate as in the dictionary model. The generator is seeded, so a failure
    ///     reproduces.
    /// </summary>
    /// <param name="seed">The generator seed.</param>
    /// <param name="depth">The nesting-depth limit.</param>
    /// <param name="tokens">The token (work) limit.</param>
    [TestMethod]
    [DataRow(1, 8, 13)]
    [DataRow(2, 10, 20)]
    [DataRow(3, 16, 40)]
    [DataRow(4, 64, 1000)]
    [DataRow(5, 256, 100_000)]
    public void RandomExpressions_EvaluateLikeTheDictionaryModel(int seed, int depth, int tokens)
    {
        const string definitions = "#define D (a + 1)\n#define E (D * b)\n#define F 5\n#define G (F << 2)\n";
        string[] names = ["a", "b", "c", "D", "E", "F", "G"];
        var options = new CStructCompilationOptions { MaxExpressionNestingDepth = depth, MaxExpressionTokens = tokens, };
        LayoutCompilation compilation = new CStruct(definitions + "struct differential_root { uint8 v[a + b + c + D + E + F + G]; };", compilationOptions: options).Compilation;
        var random = new Random(seed);
        var counts = new ExpressionDifferentialCounts();
        int notNative = 0;
        for (int index = 0; index < 2000; index++)
        {
            string text = RandomExpression(random, 0, names);
            Expr parsed = LayoutParser.ParseExpression(text);
            var captures = new List<(string Name, Expr? Value)>();
            foreach (string name in names)
            {
                // Fields usually hold a value; definitions are only sometimes overwritten, as a same-named field would.
                if (random.Next(char.IsUpper(name[0]) ? 5 : 1) == 0)
                {
                    captures.Add((name, RandomState(random, names)));
                }
            }

            LayoutVariableInput input = LayoutVariableInput.FromIntegers(null);
            if (random.Next(3) == 0)
            {
                input = LayoutVariableInput.FromIntegers(names.Where(_ => random.Next(3) == 0).ToDictionary(name => name, _ => CallerValue(random), StringComparer.Ordinal));
            }

            // An expression over the compile limits is not native: both models fail to compile it, the slot model
            // through the dictionary evaluator.
            notNative += compilation.SlotTable.Compile(parsed).IsNative ? 0 : 1;
            ExpressionDifferential.Compare(text, compilation, parsed, input, [.. captures], [ExpressionFailureDomain.Read], counts);
        }

        this.TestContext.WriteLine($"seed {seed}: {counts}; {notNative} expressions over the compile limits");
        Assert.IsGreaterThan(1000, counts.Evaluations);
    }

    /// <summary>
    ///     The validation prelude reports an undefined or unusable name before any arithmetic runs, in the order the
    ///     names appear outside conditional arms; arithmetic fails only when every such name is usable.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="expected">A part of the expected failure message.</param>
    [TestMethod]
    [DataRow("1/0 + missing", "Undefined expression identifier: missing")]
    [DataRow("1/0 + text", "'text' is text, but layout expressions")]
    [DataRow("1/0 + n", "Attempted to divide by zero.")]
    [DataRow("text + missing", "'text' is text")]
    [DataRow("missing + text", "Undefined expression identifier: missing")]
    [DataRow("(n << 200) + missing", "Undefined expression identifier: missing")]
    [DataRow("wide * 0 + missing", "'wide' is 340282366920938463463374607431768211455")]
    [DataRow("0 * wide", "'wide' is 340282366920938463463374607431768211455, which is outside the 128-bit range")]
    public void ValidationPrecedence_MatchesTheDictionaryModel(string expression, string expected)
    {
        string outcome = Evaluate(string.Empty, expression, null, ("n", new Literal(1)), ("text", new NotANumberVariable("text")), ("wide", new WideValueVariable(UInt128.MaxValue)));
        StringAssert.Contains(outcome, expected);
    }

    /// <summary>
    ///     Names inside short-circuit and <c>?:</c> arms are validated only when the arm is selected, so an unselected
    ///     undefined, unusable or failing name does not fail the expression.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="expected">The expected outcome.</param>
    [TestMethod]
    [DataRow("0 && missing", "value 0")]
    [DataRow("1 || missing", "value 1")]
    [DataRow("n ? 5 : missing", "value 5")]
    [DataRow("z ? missing : 6", "value 6")]
    [DataRow("n || text", "value 1")]
    [DataRow("z && (1 / z)", "value 0")]
    [DataRow("z ? wide : n + 2", "value 3")]
    [DataRow("n && missing", "Undefined expression identifier: missing")]
    [DataRow("z || text", "'text' is text")]
    [DataRow("n ? wide : 0", "'wide' is 340282366920938463463374607431768211455")]
    public void ShortCircuitArms_AreExemptUntilSelected(string expression, string expected)
    {
        string outcome = Evaluate(string.Empty, expression, null, ("n", new Literal(1)), ("z", new Literal(0)), ("text", new NotANumberVariable("text")), ("wide", new WideValueVariable(UInt128.MaxValue)));
        StringAssert.Contains(outcome, expected);
    }

    /// <summary>
    ///     A definition beyond the 128-bit domain is an exact out-of-domain value: selecting it fails naming the
    ///     definition and its value, an unselected arm does not, and an out-of-domain literal fails naming the literal.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="expected">A part of the expected outcome.</param>
    [TestMethod]
    [DataRow("BIG - 1", "'BIG' is 170141183460469231731687303715884105728, which is outside the 128-bit range")]
    [DataRow("n ? 2 : BIG", "value 2")]
    [DataRow("SMALL + n", "value 2")]
    [DataRow("n + 170141183460469231731687303715884105728", "The literal 170141183460469231731687303715884105728 is outside")]
    [DataRow("-170141183460469231731687303715884105728 + n", "The literal 170141183460469231731687303715884105728 is outside")]
    public void OutOfDomainValues_FailWhenSelected(string expression, string expected)
    {
        const string definitions = "#define BIG (1 << 126) * 2\n#define SMALL (BIG - BIG + 1)\n";
        string outcome = Evaluate(definitions, expression, null, ("n", new Literal(1)));
        StringAssert.Contains(outcome, expected);
    }

    /// <summary>
    ///     A definition that names a field is a live expression, evaluated whenever it is selected; a caller value of
    ///     a name it depends on makes the resolver evaluate it when the operation starts, so a later capture of that
    ///     name no longer changes it (the dictionary model's rule, reproduced by delegating to the resolver).
    /// </summary>
    [TestMethod]
    public void LiveDefinitions_AndCallerOverrides_UnfoldLikeTheResolver()
    {
        const string definitions = "#define D (n * 2)\n#define E (D + m)\n#define K 4\n";
        Assert.AreEqual("value 10", Evaluate(definitions, "E", null, ("n", new Literal(3)), ("m", new Literal(4))));
        StringAssert.Contains(Evaluate(definitions, "E", null), "Undefined expression identifier: n");

        // n = 5 from the caller folds D to 10 at the start; the capture of n = 1 afterwards leaves D at 10.
        var caller = new Dictionary<string, int>(StringComparer.Ordinal) { ["n"] = 5, };
        Assert.AreEqual("value 11", Evaluate(definitions, "E", caller, ("n", new Literal(1)), ("m", new Literal(1))));
        Assert.AreEqual("value 1", Evaluate(definitions, "n", caller, ("n", new Literal(1))));

        // Overriding a definition nothing depends on only overwrites its slot.
        var constant = new Dictionary<string, int>(StringComparer.Ordinal) { ["K"] = 7, };
        Assert.AreEqual("value 21", Evaluate(definitions, "K * n", constant, ("n", new Literal(3))));
        Assert.AreEqual("value 12", Evaluate(definitions, "K * n", null, ("n", new Literal(3))));
    }

    /// <summary>
    ///     Checked 128-bit arithmetic fails at the domain's edges with the dictionary model's messages, and the
    ///     <c>int</c> consumers reject a value outside the 32-bit range.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="value">The value captured under <c>n</c>, as decimal text.</param>
    /// <param name="expected">A part of the expected 128-bit outcome.</param>
    [TestMethod]
    [DataRow("n << 126", "1", "value 85070591730234615865843651857942052864")]
    [DataRow("n << 127", "1", "the result is outside the 128-bit range that layout expressions support.")]
    [DataRow("n << 128", "1", "shift count")]
    [DataRow("n >> 127", "-170141183460469231731687303715884105728", "value -1")]
    [DataRow("n * 2", "170141183460469231731687303715884105727", "the result is outside the 128-bit range")]
    [DataRow("-n", "-170141183460469231731687303715884105728", "the result is outside the 128-bit range")]
    [DataRow("n / -1", "-170141183460469231731687303715884105728", "the result is outside the 128-bit range")]
    [DataRow("n % 0", "5", "Attempted to divide by zero.")]
    [DataRow("n + 1", "2147483647", "value 2147483648")]
    [DataRow("n - 1", "-2147483648", "value -2147483649")]
    [DataRow("~n", "0", "value -1")]
    [DataRow("!n", "0", "value 1")]
    public void ShiftsAndOverflow_AtTheBounds(string expression, string value, string expected)
    {
        var literal = new Literal(BigInteger.Parse(value, System.Globalization.CultureInfo.InvariantCulture));
        string outcome = Evaluate(string.Empty, expression, null, ("n", literal));
        StringAssert.Contains(outcome, expected);
    }

    /// <summary>
    ///     Live expressions that select each other report the cycle at the repeated name, from validation or, in a
    ///     selected arm, from evaluation; an unselected arm does not.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="expected">A part of the expected outcome.</param>
    [TestMethod]
    [DataRow("a", "Circular expression dependency detected at: a")]
    [DataRow("b + 1", "Circular expression dependency detected at: b")]
    [DataRow("k ? 0 : a", "value 0")]
    [DataRow("k ? c : 0", "Circular expression dependency detected at: c")]
    [DataRow("s", "Circular expression dependency detected at: s")]
    public void Cycles_AreReportedAtTheRepeatedName(string expression, string expected)
    {
        Expr a = LayoutParser.ParseExpression("b + 1");
        Expr b = LayoutParser.ParseExpression("a * 2");
        Expr c = LayoutParser.ParseExpression("k ? c : 1");
        Expr s = LayoutParser.ParseExpression("0 || s");
        string outcome = Evaluate(string.Empty, expression, null, ("a", a), ("b", b), ("c", c), ("s", s), ("k", new Literal(1)));
        StringAssert.Contains(outcome, expected);
    }

    /// <summary>
    ///     Chains of live definitions near <see cref="CStructCompilationOptions.MaxExpressionNestingDepth"/> and
    ///     <see cref="CStructCompilationOptions.MaxExpressionTokens"/> fail at the same link, with the same limit, as
    ///     the dictionary model: the session path counts depth and work as the dictionary session does.
    /// </summary>
    /// <param name="depth">The nesting-depth limit.</param>
    /// <param name="tokens">The token (work) limit.</param>
    [TestMethod]
    [DataRow(8, 1000)]
    [DataRow(12, 1000)]
    [DataRow(64, 12)]
    [DataRow(64, 25)]
    [DataRow(16, 40)]
    public void LiveChainsNearTheLimits_FailAtTheSameLink(int depth, int tokens)
    {
        var options = new CStructCompilationOptions { MaxExpressionNestingDepth = depth, MaxExpressionTokens = tokens, };
        var outcomes = new HashSet<string>(StringComparer.Ordinal);
        var definitions = new System.Text.StringBuilder("#define A0 (n + 1)\n");
        for (int length = 1; length <= 10; length++)
        {
            // The resolver evaluates the whole chain when an operation starts; the use site nests the top link deeper.
            string top = "A" + (length - 1);
            for (int extra = 0; extra < 4; extra++)
            {
                string nested = top;
                for (int level = 0; level < extra; level++)
                {
                    nested = "(" + nested + " + 0)";
                }

                outcomes.Add(Evaluate(definitions.ToString(), nested + " + n", null, options, ("n", new Literal(1))));
                outcomes.Add(Evaluate(definitions.ToString(), "(n ? " + nested + " : 0) + 1", null, options, ("n", new Literal(1))));
                outcomes.Add(Evaluate(definitions.ToString(), nested, new Dictionary<string, int>(StringComparer.Ordinal) { ["n"] = 2, }, options));
            }

            definitions.Append("#define A").Append(length).Append(" (A").Append(length - 1).Append(" + 1)\n");
        }

        // Each configuration reaches a limit somewhere along the chain, so both limits' paths are compared.
        Assert.IsTrue(outcomes.Any(outcome => outcome.StartsWith("value ", StringComparison.Ordinal)), string.Join("\n", outcomes));
        Assert.IsTrue(outcomes.Any(outcome => outcome.Contains("Maximum expression evaluation", StringComparison.Ordinal)), string.Join("\n", outcomes));
    }

    /// <summary>
    ///     A program that is not leaf-safe (near a limit) takes the session path even with literal values, and gives the
    ///     dictionary model's result; the literal-only work of <c>&amp;&amp;</c> jumps counts too.
    /// </summary>
    /// <param name="expression">The expression.</param>
    /// <param name="tokens">The token (work) limit.</param>
    [TestMethod]
    [DataRow("n && n", 4)]
    [DataRow("n && n", 5)]
    [DataRow("n && n", 6)]
    [DataRow("n + n + n", 6)]
    [DataRow("n + n + n", 8)]
    [DataRow("(n ? n : n) + n", 9)]
    [DataRow("(n ? n : n) + n", 12)]
    public void NearWorkLimit_WithLiterals_MatchesTheDictionaryModel(string expression, int tokens)
    {
        var options = new CStructCompilationOptions { MaxExpressionTokens = tokens, };
        _ = Evaluate(string.Empty, expression, null, options, ("n", new Literal(1)));
        _ = Evaluate(string.Empty, expression, null, options, ("n", new Literal(0)));
        _ = Evaluate(string.Empty, expression, null, options);
    }

    /// <summary>
    ///     A caller name no expression reads is ignored, and a caller value of a name an expression reads replaces its
    ///     value, as in the dictionary model.
    /// </summary>
    [TestMethod]
    public void CallerNames_WithoutASlot_AreNotObservable()
    {
        var unrelated = new Dictionary<string, int>(StringComparer.Ordinal) { ["unrelated"] = 5, ["UNRELATED"] = -1, };
        StringAssert.Contains(Evaluate(string.Empty, "n + 1", unrelated), "Undefined expression identifier: n");
        var relevant = new Dictionary<string, int>(StringComparer.Ordinal) { ["n"] = int.MinValue, ["unrelated"] = 5, };
        Assert.AreEqual("value -2147483647", Evaluate(string.Empty, "n + 1", relevant));
    }

    /// <summary>Generates the text of a random expression, fully parenthesized.</summary>
    /// <param name="random">The generator.</param>
    /// <param name="level">The current nesting level; deeper levels favor leaves.</param>
    /// <param name="names">The names a leaf may use.</param>
    /// <returns>The expression text.</returns>
    private static string RandomExpression(Random random, int level, string[] names)
    {
        string[] literals = ["0", "1", "2", "3", "7", "64", "127", "128", "255", "(-1)", "2147483647", "0x7fffffffffffffffffffffffffffffff", "170141183460469231731687303715884105728"];
        string[] binary = ["+", "-", "*", "/", "%", "<<", ">>", "&", "|", "^", "&&", "||", "==", "!=", "<", "<=", ">", ">="];
        string[] unary = ["-", "~", "!"];
        int choice = level >= 5 ? 0 : random.Next(level == 0 ? 1 : 0, 6);
        return choice switch
        {
            0 => random.Next(2) == 0 ? names[random.Next(names.Length)] : literals[random.Next(literals.Length)],
            1 => "(" + unary[random.Next(unary.Length)] + RandomExpression(random, level + 1, names) + ")",
            2 => "(" + RandomExpression(random, level + 1, names) + " ? " + RandomExpression(random, level + 1, names) + " : " + RandomExpression(random, level + 1, names) + ")",
            _ => "(" + RandomExpression(random, level + 1, names) + " " + binary[random.Next(binary.Length)] + " " + RandomExpression(random, level + 1, names) + ")",
        };
    }

    /// <summary>Picks a random value a slot can hold: an edge literal, an out-of-domain literal, an unusable value, nothing, or a live expression.</summary>
    /// <param name="random">The generator.</param>
    /// <param name="names">The names a live expression may read.</param>
    /// <returns>The value; <see langword="null"/> removes the name.</returns>
    private static Expr? RandomState(Random random, string[] names) => random.Next(12) switch
    {
        0 => null,
        1 => new WideValueVariable(UInt128.MaxValue),
        2 => new NotANumberVariable("text"),
        3 => new Literal(BigInteger.One << 127),
        4 => new Literal(Int128.MaxValue),
        5 => new Literal(Int128.MinValue),
        6 or 7 => LayoutParser.ParseExpression(RandomExpression(random, 3, names)),
        _ => new Literal(CallerValue(random)),
    };

    /// <summary>Picks a random caller value: small, or at the <c>int</c> bounds.</summary>
    /// <param name="random">The generator.</param>
    /// <returns>The value.</returns>
    private static int CallerValue(Random random) => random.Next(8) switch
    {
        0 => int.MaxValue,
        1 => int.MinValue,
        2 => -1,
        _ => random.Next(0, 9),
    };

    /// <summary>Evaluates one expression in both models with the given inputs and captures, asserting they agree.</summary>
    /// <param name="definitions">Definitions placed before the layout.</param>
    /// <param name="expression">The expression.</param>
    /// <param name="variables">Integer caller variables, or <see langword="null"/>.</param>
    /// <param name="captures">Values captured after creation.</param>
    /// <returns>The Read-domain outcome.</returns>
    private static string Evaluate(string definitions, string expression, object? variables, params (string Name, Expr? Value)[] captures)
        => Evaluate(definitions, expression, variables, null, captures);

    /// <summary>Evaluates one expression in both models with the given compilation options, inputs and captures.</summary>
    /// <param name="definitions">Definitions placed before the layout.</param>
    /// <param name="expression">The expression.</param>
    /// <param name="variables">Integer caller variables, or <see langword="null"/>.</param>
    /// <param name="options">The compilation options, or <see langword="null"/>.</param>
    /// <param name="captures">Values captured after creation.</param>
    /// <returns>The Read-domain outcome.</returns>
    private static string Evaluate(string definitions, string expression, object? variables, CStructCompilationOptions? options, params (string Name, Expr? Value)[] captures)
    {
        (LayoutCompilation compilation, Expr parsed) = Layout(definitions, expression, options, captures.Select(capture => capture.Name));
        LayoutVariableInput input = LayoutVariableInput.FromIntegers((IReadOnlyDictionary<string, int>?)variables);
        var counts = new ExpressionDifferentialCounts();
        ExpressionFailureDomain[] domains = [ExpressionFailureDomain.Read, ExpressionFailureDomain.Layout, ExpressionFailureDomain.Write];
        return ExpressionDifferential.Compare(expression, compilation, parsed, input, captures, domains, counts);
    }

    /// <summary>
    ///     Compiles definitions and a struct whose array length names every identifier of the expression and every
    ///     captured name, so each has a slot, and parses the expression on its own.
    /// </summary>
    /// <param name="definitions">Definitions placed before the struct.</param>
    /// <param name="expression">The expression.</param>
    /// <param name="options">The compilation options, or <see langword="null"/>.</param>
    /// <param name="captured">The names captures will set.</param>
    /// <returns>The compilation and the parsed expression.</returns>
    private static (LayoutCompilation Compilation, Expr Expression) Layout(string definitions, string expression, CStructCompilationOptions? options, IEnumerable<string> captured)
    {
        Expr parsed = LayoutParser.ParseExpression(expression);
        string[] names = [.. ExpressionDifferential.IdentifiersOf(parsed).Concat(captured).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];
        string length = names.Length == 0 ? "1" : string.Join(" + ", names);
        var layout = new CStruct(definitions + "struct differential_root { uint8 v[" + length + "]; };", compilationOptions: options);
        return (layout.Compilation, parsed);
    }
}
