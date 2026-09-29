namespace CStructSharp.Tests;

using System.Numerics;
using System.Text;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     Compares the slot model (<see cref="SlotTable"/>, <see cref="VariableSlots"/>, <see cref="ProgramExpression"/>)
///     with the dictionary model (<see cref="LayoutVariableResolver"/> and <see cref="LayoutExpressionEvaluator"/>) over
///     every expression of a compiled layout: the initial states must stand for the same dictionary, and every
///     evaluation must give the same value or the same failure (type, message, <see cref="CStructException"/> fields and
///     inner exception). The dictionary model's outcomes are the golden reference (<see cref="EngineGolden"/>): a
///     recording run compares both models and records them, and an ordinary run checks the slot model against the
///     recorded outcomes; a comparing run (<see cref="EngineGolden.ComparesInterpreter"/>) does both.
/// </summary>
internal static class ExpressionDifferential
{
    /// <summary>The context the evaluations name in their failure messages.</summary>
    public const string Context = "the differential expression";

    /// <summary>A caller variable name no layout uses.</summary>
    private const string UnrelatedName = "zz_unrelated_caller_name";

    /// <summary>The number of names of one expression that are varied one at a time; the others vary together.</summary>
    private const int SingleNameLimit = 4;

    /// <summary>The caller values every scenario applies: small values, the signs, and the <c>int</c> bounds.</summary>
    private static readonly int[] CallerValues = [0, 1, -1, 3, 255, int.MaxValue, int.MinValue];

    /// <summary>
    ///     The values a capture can leave under a name: in-domain literals up to the 128-bit bounds, out-of-domain
    ///     literals (definitions and enum members), wide values, non-numbers, and no value (<see langword="null"/>).
    /// </summary>
    private static readonly Expr?[] CapturedValues =
    [
        new Literal(0), new Literal(1), new Literal(-1), new Literal(2), new Literal(3), new Literal(7), new Literal(8), new Literal(64),
        new Literal(127), new Literal(128), new Literal(255), new Literal(1000), new Literal(65535),
        new Literal(int.MaxValue), new Literal(int.MinValue), new Literal((Int128)int.MaxValue + 1), new Literal((Int128)int.MinValue - 1),
        new Literal(long.MaxValue), new Literal(long.MinValue), new Literal((Int128)ulong.MaxValue), new Literal(Int128.One << 64),
        new Literal(Int128.MaxValue), new Literal(Int128.MinValue),
        new Literal(BigInteger.One << 127), new Literal(-(BigInteger.One << 127) - 1),
        new WideValueVariable(UInt128.One << 127), new WideValueVariable(UInt128.MaxValue), new WideValueVariable(BigInteger.Pow(2, 200)),
        new NotANumberVariable("text"), new NotANumberVariable("an array"),
        null,
    ];

    /// <summary>The failure domains; every expression is compared in all three without caller variables.</summary>
    private static readonly ExpressionFailureDomain[] Domains = [ExpressionFailureDomain.Read, ExpressionFailureDomain.Layout, ExpressionFailureDomain.Write];

    /// <summary>
    ///     Runs every scenario over every expression of <paramref name="compilation"/> and fails on the first difference.
    /// </summary>
    /// <param name="id">The layout's name in failure messages.</param>
    /// <param name="compilation">The compiled layout.</param>
    /// <param name="variables">The corpus's own caller variables, or <see langword="null"/>.</param>
    /// <returns>What was compared.</returns>
    public static ExpressionDifferentialCounts Run(string id, LayoutCompilation compilation, IReadOnlyDictionary<string, int>? variables)
    {
        SlotTable table = compilation.SlotTable;
        var counts = new ExpressionDifferentialCounts { Layouts = 1, };

        // The compiled model's dictionaries enumerate in an order that varies between runs (reference and randomized
        // string hashes), so the expressions are compared in the order of their invariant text; the golden outcomes of
        // the layout are hashed in that order.
        List<Expr> expressions = [.. EngineGolden.Invariant(() => CollectExpressions(compilation).OrderBy(expression => expression.ToString(), StringComparer.Ordinal).ToList())];
        foreach (Expr expression in expressions)
        {
            ProgramExpression program = table.Compile(expression);
            counts.Expressions++;
            counts.Native += program.IsNative ? 1 : 0;
            counts.LeafSafe += program.IsLeafSafe ? 1 : 0;
            string[] names = ReachableNames(compilation, expression).Where(name => table.TryGetSlot(name, out _)).Order(StringComparer.Ordinal).ToArray();
            string label = id + " :: " + expression;

            // No caller variables, in every domain.
            Compare(label + " [no variables]", compilation, expression, LayoutVariableInput.FromIntegers(null), null, Domains, counts);
            if (variables is not null)
            {
                Compare(label + " [corpus variables]", compilation, expression, LayoutVariableInput.FromIntegers(variables), null, [ExpressionFailureDomain.Read], counts);
            }

            // Caller variables: every reachable name together, each alone, unrelated names alone and with the relevant ones.
            foreach (int value in CallerValues)
            {
                if (names.Length > 0)
                {
                    Compare(label + " [all = " + value + "]", compilation, expression, Integers(names, value), null, [ExpressionFailureDomain.Read], counts);
                }

                foreach (string name in names.Take(SingleNameLimit))
                {
                    Compare(label + " [" + name + " = " + value + "]", compilation, expression, Integers([name], value), null, [ExpressionFailureDomain.Read], counts);
                }
            }

            Compare(label + " [unrelated]", compilation, expression, Integers([UnrelatedName, "__other"], 5), null, [ExpressionFailureDomain.Read], counts);
            Compare(label + " [unrelated and all]", compilation, expression, Integers([.. names, UnrelatedName], 2), null, [ExpressionFailureDomain.Read], counts);

            // Captured values: every reachable name holding it, each alone (the others holding 3), and after caller values.
            foreach (Expr? captured in CapturedValues)
            {
                string shown = captured?.ToString() ?? "undefined";
                if (names.Length == 0)
                {
                    continue;
                }

                Compare(label + " [captured all " + shown + "]", compilation, expression, LayoutVariableInput.FromIntegers(null), names.Select(name => (name, captured)).ToArray(), [ExpressionFailureDomain.Read], counts);
                Compare(label + " [caller all 5, captured " + names[0] + " " + shown + "]", compilation, expression, Integers(names, 5), [(names[0], captured)], [ExpressionFailureDomain.Read], counts);
                foreach (string name in names.Take(SingleNameLimit))
                {
                    (string, Expr?)[] captures = names.Select(other => (other, other == name ? captured : new Literal(3))).ToArray();
                    Compare(label + " [captured " + name + " " + shown + "]", compilation, expression, LayoutVariableInput.FromIntegers(null), captures, [ExpressionFailureDomain.Read], counts);
                }
            }

            if (names.Length == 0)
            {
                continue;
            }

            // Live expressions under the names (the session path): a constant one; a ring in which each name reads the
            // next (a cycle); and each name alone reading the first name, which holds 3 (a value) or itself (a cycle).
            Expr constant = new BinaryOp(BinaryOperatorType.Add, new Literal(3), new Literal(4));
            Compare(label + " [live constant]", compilation, expression, LayoutVariableInput.FromIntegers(null), names.Select(name => (name, (Expr?)constant)).ToArray(), [ExpressionFailureDomain.Read], counts);
            (string, Expr?)[] ring = names.Select((name, index) => (name, (Expr?)Successor(names[(index + 1) % names.Length]))).ToArray();
            Compare(label + " [live ring]", compilation, expression, LayoutVariableInput.FromIntegers(null), ring, [ExpressionFailureDomain.Read], counts);
            foreach (string name in names.Take(SingleNameLimit))
            {
                (string, Expr?)[] captures = names.Select(other => (other, (Expr?)(other == name ? Successor(names[0]) : new Literal(3)))).ToArray();
                Compare(label + " [live " + name + "]", compilation, expression, LayoutVariableInput.FromIntegers(null), captures, [ExpressionFailureDomain.Read], counts);
            }
        }

        return counts;
    }

    /// <summary>
    ///     Creates the slot model's state for one scenario, applies the captures, evaluates <paramref name="expression"/>
    ///     (128-bit and <c>int</c> consumers) in each domain, and checks the creation, the initial state and the
    ///     evaluations against the golden reference under <paramref name="label"/>. When the run compares with the
    ///     interpreter (<see cref="EngineGolden.ComparesInterpreter"/>), the dictionary model runs beside it: its state and
    ///     every evaluation must be the same.
    /// </summary>
    /// <param name="label">The scenario's name in failure messages, and its golden key.</param>
    /// <param name="compilation">The compiled layout.</param>
    /// <param name="expression">The expression.</param>
    /// <param name="input">The caller variables.</param>
    /// <param name="captures">The values captured after creation (<see langword="null"/> removes the name), or <see langword="null"/>.</param>
    /// <param name="domains">The domains to evaluate in.</param>
    /// <param name="counts">The counters to add to.</param>
    /// <returns>The outcome of the first domain's 128-bit evaluation, or of the creation when it failed.</returns>
    public static string Compare(
        string label,
        LayoutCompilation compilation,
        Expr expression,
        LayoutVariableInput input,
        (string Name, Expr? Value)[]? captures,
        ExpressionFailureDomain[] domains,
        ExpressionDifferentialCounts counts)
        => EngineGolden.Invariant(() => CompareInvariantly(label, compilation, expression, input, captures, domains, counts));

    /// <summary>The body of <see cref="Compare"/>, run under the invariant culture.</summary>
    /// <param name="label">The scenario's name in failure messages, and its golden key.</param>
    /// <param name="compilation">The compiled layout.</param>
    /// <param name="expression">The expression.</param>
    /// <param name="input">The caller variables.</param>
    /// <param name="captures">The values captured after creation, or <see langword="null"/>.</param>
    /// <param name="domains">The domains to evaluate in.</param>
    /// <param name="counts">The counters to add to.</param>
    /// <returns>The outcome of the first domain's 128-bit evaluation, or of the creation when it failed.</returns>
    private static string CompareInvariantly(
        string label,
        LayoutCompilation compilation,
        Expr expression,
        LayoutVariableInput input,
        (string Name, Expr? Value)[]? captures,
        ExpressionFailureDomain[] domains,
        ExpressionDifferentialCounts counts)
    {
        SlotTable table = compilation.SlotTable;
        Dictionary<string, Expr>? dictionary = null;
        string? expectedCreation = EngineGolden.ComparesInterpreter ? Outcome(() => dictionary = input.Resolve(compilation.LayoutVariableResolver)) : null;
        VariableSlots slots = default;
        bool created = false;
        string actualCreation = Outcome(() =>
        {
            slots = VariableSlots.Create(table, input);
            created = true;
            return null;
        });
        try
        {
            if (expectedCreation is not null)
            {
                Assert.AreEqual(dictionary is null, !created, label + ": creation " + expectedCreation + " vs " + actualCreation);
                if (dictionary is null)
                {
                    Assert.AreEqual(expectedCreation, actualCreation, label + ": creation failure");
                }
            }

            if (!created)
            {
                counts.Evaluations++;
                EngineGolden.Check(label, "creation = " + actualCreation);
                return actualCreation;
            }

            if (dictionary is not null)
            {
                AssertSameState(label, table, dictionary, slots);
            }

            var outcome = new StringBuilder("state =");
            foreach ((string name, Expr value) in slots.ToDictionary().OrderBy(pair => pair.Key, StringComparer.Ordinal))
            {
                outcome.Append(' ').Append(name).Append(": ").Append(value.GetType().Name).Append(' ').Append(value).Append(';');
            }

            foreach ((string name, Expr? value) in captures ?? [])
            {
                Assert.IsTrue(table.TryGetSlot(name, out int slot), label + ": " + name + " has no slot");
                if (value is null)
                {
                    _ = dictionary?.Remove(name);
                    slots.Set(slot, SlotValue.Undefined);
                }
                else
                {
                    if (dictionary is not null)
                    {
                        dictionary[name] = value;
                    }

                    slots.Set(slot, table.ToSlotValue(value));
                }
            }

            ProgramExpression program = table.Compile(expression);
            LayoutExpressionEvaluator evaluator = compilation.LayoutExpressionEvaluator;
            string? first = null;
            foreach (ExpressionFailureDomain domain in domains)
            {
                VariableSlots current = slots;
                string actual = Outcome(() => current.Evaluate(program, Context, domain));
                string actualInt = Outcome(() => current.EvaluateInt32(program, Context, domain));
                if (dictionary is not null)
                {
                    Assert.AreEqual(Outcome(() => evaluator.Evaluate(expression, dictionary, Context, domain)), actual, label + " (" + domain + ")");
                    Assert.AreEqual(Outcome(() => evaluator.EvaluateInt32(expression, dictionary, Context, domain)), actualInt, label + " (" + domain + ", int)");
                }

                outcome.Append('\n').Append(domain).Append(" = ").Append(actual).Append('\n').Append(domain).Append(" int = ").Append(actualInt);
                first ??= actual;
                counts.Evaluations += 2;
                counts.Failures += actualInt.StartsWith("value ", StringComparison.Ordinal) ? 0 : 1;
            }

            EngineGolden.Check(label, outcome.ToString());
            return first!;
        }
        finally
        {
            if (created)
            {
                slots.Dispose();
            }
        }
    }

    /// <summary>Renders the result of a call: its value, or its failure with every field a caller can observe.</summary>
    /// <param name="call">The call.</param>
    /// <returns>The rendering.</returns>
    public static string Outcome(Func<object?> call)
    {
        try
        {
            return "value " + call();
        }
        catch (Exception exception) when (exception is not UnitTestAssertException)
        {
            return Describe(exception);
        }
    }

    /// <summary>
    ///     Returns the names an expression can reach: the identifiers it names and, through definitions, every name
    ///     their expressions name.
    /// </summary>
    /// <param name="compilation">The compiled layout.</param>
    /// <param name="expression">The expression.</param>
    /// <returns>The names.</returns>
    public static HashSet<string> ReachableNames(LayoutCompilation compilation, Expr expression)
    {
        LayoutVariableResolver resolver = compilation.LayoutVariableResolver;
        var names = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(IdentifiersOf(expression));
        while (pending.Count > 0)
        {
            string name = pending.Pop();
            if (names.Add(name) && resolver.Definitions.ContainsKey(name))
            {
                foreach (string dependency in resolver.GetDefinitionDependencies(name))
                {
                    pending.Push(dependency);
                }
            }
        }

        return names;
    }

    /// <summary>
    ///     Collects every expression an operation of the layout can evaluate: array dimensions (including pointer
    ///     <c>@count</c> targets), conditional selectors, definitions, and each slot's name on its own (which evaluates
    ///     that variable, live definitions included).
    /// </summary>
    /// <param name="compilation">The compiled layout.</param>
    /// <returns>The distinct expressions.</returns>
    public static List<Expr> CollectExpressions(LayoutCompilation compilation)
    {
        var seen = new HashSet<Expr>(ReferenceEqualityComparer.Instance);
        var expressions = new List<Expr>();

        // Adds an expression once.
        void Add(Expr? expression)
        {
            if (expression is not null && seen.Add(expression))
            {
                expressions.Add(expression);
            }
        }

        // Adds the expressions one compiled field evaluates.
        void AddField(CompiledField field)
        {
            foreach (CompiledArrayDimension dimension in field.Array.Dimensions)
            {
                Add(dimension.CountExpression);
            }

            foreach (CompiledArrayDimension dimension in field.PointerElements?.Dimensions ?? [])
            {
                Add(dimension.CountExpression);
            }

            foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
            {
                Add(branch.Group.Selector);
            }
        }

        var composites = new HashSet<CompiledCompositeType>(ReferenceEqualityComparer.Instance);
        foreach (CompiledTypeSymbol symbol in compilation.CompiledModel.Composites.Values.Concat(compilation.CompiledModel.Symbols.Values.Select(reference => reference.Symbol)))
        {
            if (symbol.Definition is CompiledCompositeType composite && composites.Add(composite))
            {
                foreach (CompiledField field in composite.Fields)
                {
                    AddField(field);
                }
            }
        }

        foreach (CompiledField field in compilation.CompiledModel.RootFields.Values)
        {
            AddField(field);
        }

        foreach (Defines definition in compilation.LayoutVariableResolver.Definitions.Values)
        {
            Add(definition.Value);
        }

        SlotTable table = compilation.SlotTable;
        for (int slot = 0; slot < table.Count; slot++)
        {
            Add(new Identifier(table.GetName(slot)));
        }

        return expressions;
    }

    /// <summary>
    ///     Asserts that the slots stand for the dictionary: every name with a slot has the same entry (or none), and every
    ///     caller variable without a slot is kept beside the slots with its value.
    /// </summary>
    /// <param name="label">The scenario's name in failure messages.</param>
    /// <param name="table">The layout's table, which the slots belong to.</param>
    /// <param name="dictionary">The dictionary model's state.</param>
    /// <param name="slots">The slot model's state.</param>
    public static void AssertSameState(string label, SlotTable table, Dictionary<string, Expr> dictionary, VariableSlots slots)
    {
        Assert.AreSame(table, slots.Table, label + ": the slots belong to another table");
        Dictionary<string, Expr> view = slots.ToDictionary();
        foreach ((string name, Expr expected) in dictionary)
        {
            Assert.IsTrue(view.TryGetValue(name, out Expr? actual), label + ": " + name + " is missing from the slots");
            Assert.AreEqual(expected, actual, label + ": state of " + name);
        }

        foreach (string name in view.Keys)
        {
            Assert.IsTrue(dictionary.ContainsKey(name), label + ": " + name + " is only in the slots");
        }
    }

    /// <summary>Returns an integer input that sets each name to one value.</summary>
    /// <param name="names">The names.</param>
    /// <param name="value">The value.</param>
    /// <returns>The input.</returns>
    private static LayoutVariableInput Integers(IEnumerable<string> names, int value)
        => LayoutVariableInput.FromIntegers(names.Distinct(StringComparer.Ordinal).ToDictionary(name => name, _ => value, StringComparer.Ordinal));

    /// <summary>Returns the live expression <c>name + 1</c>.</summary>
    /// <param name="name">The name it reads.</param>
    /// <returns>The expression.</returns>
    private static Expr Successor(string name) => new BinaryOp(BinaryOperatorType.Add, new Identifier(name), new Literal(1));

    /// <summary>Renders a failure and its inner failures.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The rendering.</returns>
    private static string Describe(Exception exception)
    {
        var text = new StringBuilder(exception.GetType().FullName).Append(": ").Append(exception.Message);
        if (exception is CStructException failure)
        {
            text.Append(" {code=").Append(failure.Code).Append(", offset=").Append(failure.Offset).Append(", path=").Append(failure.Path)
                .Append(", member=").Append(failure.Member).Append(", memberType=").Append(failure.MemberType).Append('}');
        }

        if (exception.InnerException is { } inner)
        {
            text.Append(" <- ").Append(Describe(inner));
        }

        return text.ToString();
    }

    /// <summary>Lists the identifiers an expression tree names.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The names, possibly repeated.</returns>
    public static IEnumerable<string> IdentifiersOf(Expr expression)
    {
        var pending = new Stack<Expr>();
        pending.Push(expression);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
            case Identifier identifier:
                yield return identifier.Name;
                break;
            case UnaryOp unary:
                pending.Push(unary.Expr);
                break;
            case BinaryOp binary:
                pending.Push(binary.Left);
                pending.Push(binary.Right);
                break;
            case ConditionalExpr conditional:
                pending.Push(conditional.Condition);
                pending.Push(conditional.WhenTrue);
                pending.Push(conditional.WhenFalse);
                break;
            case Call call:
                pending.Push(call.Expr);
                foreach (Expr argument in call.Arguments)
                {
                    pending.Push(argument);
                }

                break;
            }
        }
    }
}
