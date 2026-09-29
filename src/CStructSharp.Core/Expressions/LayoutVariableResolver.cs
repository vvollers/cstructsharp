namespace CStructSharp.Expressions;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>Caches static definitions and recomputes only values affected by caller overrides.</summary>
internal sealed class LayoutVariableResolver
{
    // Plain collections used read-only after construction: FrozenDictionary/FrozenSet construction dominated small
    // layout compilation for key sets of a handful of names.
    private readonly Dictionary<string, ImmutableArray<string>> definitionDependencies;
    private readonly Dictionary<string, Defines> definitions;
    private readonly ExpressionEvaluator evaluator;
    private readonly HashSet<string> exactEnumDefinitions;
    private readonly Dictionary<string, ImmutableArray<string>> reverseDependents;
    private readonly Dictionary<string, Expr> staticValues;

    /// <summary>Compiles definition dependencies and resolves the immutable no-override baseline once.</summary>
    /// <param name="definitions">The layout's <c>#define</c> and constant definitions; names must be unique.</param>
    /// <param name="evaluator">The evaluator that finds dependencies and reduces expressions.</param>
    /// <param name="exactEnumDefinitions">
    ///     Definition names that are enum members, which keep their expression rather than an exact literal when they
    ///     overflow the 128-bit domain; <see langword="null"/> for none.
    /// </param>
    /// <exception cref="CStructLayoutException">
    ///     The definitions form a dependency cycle or a static expression cannot be resolved.
    /// </exception>
    public LayoutVariableResolver(
        IEnumerable<Defines> definitions,
        ExpressionEvaluator evaluator,
        IReadOnlyCollection<string>? exactEnumDefinitions = null)
    {
        this.evaluator = evaluator;
        IEnumerable<string> enumDefinitions = exactEnumDefinitions is null
                                                  ? Array.Empty<string>()
                                                  : exactEnumDefinitions;
        this.exactEnumDefinitions = new HashSet<string>(enumDefinitions, StringComparer.Ordinal);
        this.definitions = definitions.ToDictionary(
            define => define.Name.Name,
            define => define,
            StringComparer.Ordinal);

        try
        {
            this.definitionDependencies = this.definitions.ToDictionary(
                entry => entry.Key,
                entry => this.evaluator.GetDependencies(entry.Value.Value).ToImmutableArray(),
                StringComparer.Ordinal);
            this.reverseDependents = this.BuildReverseDependents();
            this.staticValues = this.BuildStaticValues(this.GetTopologicallySortedDefinitions()).
                ToDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
        }
        catch (CStructLayoutException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedExpressionFailure(exception))
        {
            throw new CStructLayoutException(
                "Layout expression could not be resolved: " + exception.Message,
                exception);
        }
    }

    /// <summary>Gets the layout's definitions (<c>#define</c>s and qualified enum members) by name, as written.</summary>
    public IReadOnlyDictionary<string, Defines> Definitions => this.definitions;

    /// <summary>Returns the names a definition's expression reads directly, including names that are not definitions.</summary>
    /// <param name="name">A name of <see cref="Definitions"/>.</param>
    /// <returns>The direct dependencies.</returns>
    public ImmutableArray<string> GetDefinitionDependencies(string name) => this.definitionDependencies[name];

    /// <summary>Snapshots public integer overrides directly into the operation dictionary.</summary>
    /// <param name="suppliedVariables">
    ///     The caller's integer values, which replace definitions of the same name, or <see langword="null"/> for none.
    /// </param>
    /// <returns>A new dictionary for one operation, re-resolving each definition an override affects.</returns>
    /// <exception cref="CStructLayoutException">An affected expression cannot be resolved.</exception>
    public Dictionary<string, Expr> CreateIntegers(IReadOnlyDictionary<string, int>? suppliedVariables)
    {
        bool hasSuppliedVariables = suppliedVariables is { Count: > 0, };
        if (!hasSuppliedVariables && this.staticValues.Count == this.definitions.Count)
        {
            return new LayoutVariables(this.staticValues);
        }

        try
        {
            var variables = new LayoutVariables(this.staticValues);
            foreach (KeyValuePair<string, Defines> definition in this.definitions)
            {
                if (!this.staticValues.ContainsKey(definition.Key))
                {
                    variables.Add(definition.Key, definition.Value.Value);
                }
            }

            HashSet<string> invalidated = hasSuppliedVariables
                                              ? this.FindInvalidatedDefinitions(suppliedVariables!.Keys)
                                              : new HashSet<string>(StringComparer.Ordinal);
            foreach (string name in invalidated)
            {
                variables.Remove(name);
                if (!suppliedVariables!.ContainsKey(name) &&
                    this.definitions.TryGetValue(name, out Defines? define))
                {
                    variables.Add(name, define.Value);
                }
            }

            if (hasSuppliedVariables)
            {
                foreach (KeyValuePair<string, int> supplied in suppliedVariables!)
                {
                    variables[supplied.Key] = new Literal(supplied.Value);
                }
            }

            return this.ResolveExpressions(variables);
        }
        catch (CStructLayoutException)
        {
            throw;
        }
        catch (Exception exception) when (IsExpectedExpressionFailure(exception))
        {
            throw new CStructLayoutException(
                "Layout expression could not be resolved: " + exception.Message,
                exception);
        }
    }

    /// <summary>
    ///     Evaluates a definition that overflowed the 128-bit domain as an exact integer, so the constant is still
    ///     published with its value; a definition that fails exactly too (division by zero, ...) keeps its expression.
    /// </summary>
    private Expr ResolveExactOrKeep(Expr expression, IReadOnlyDictionary<string, Expr> expressions)
    {
        try
        {
            BigInteger exact = this.evaluator.EvaluateExact(expression, expressions, 128);
            return new Literal(exact);
        }
        catch (Exception exception) when (IsExpectedExpressionFailure(exception))
        {
            return expression;
        }
    }

    /// <summary>Returns only definitions whose complete dependency closure is known at layout-compilation time.</summary>
    /// <returns>The shared static values, keyed by definition name; callers must not mutate them.</returns>
    public IReadOnlyDictionary<string, Expr> CreateStatic()
    {
        return this.staticValues;
    }

    /// <summary>Recognizes deterministic expression-domain failures that public layout operations normalize.</summary>
    private static bool IsExpectedExpressionFailure(Exception exception)
    {
        return exception is InvalidOperationException or ArithmeticException or
               KeyNotFoundException or NotSupportedException;
    }

    /// <summary>Builds reverse dependency edges once so override invalidation is a small graph walk.</summary>
    private Dictionary<string, ImmutableArray<string>> BuildReverseDependents()
    {
        var reverse = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, ImmutableArray<string>> definition in this.definitionDependencies)
        {
            foreach (string dependency in definition.Value)
            {
                if (!reverse.TryGetValue(dependency, out HashSet<string>? dependents))
                {
                    dependents = new HashSet<string>(StringComparer.Ordinal);
                    reverse.Add(dependency, dependents);
                }

                dependents.Add(definition.Key);
            }
        }

        return reverse.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.ToImmutableArray(),
            StringComparer.Ordinal);
    }

    /// <summary>
    ///     Finds supplied names and every definition that depends on them directly or transitively: the names a caller
    ///     override removes from the static state, so their definitions are resolved again for that operation.
    /// </summary>
    /// <param name="suppliedNames">The names a caller overrides.</param>
    /// <returns>The supplied names and their transitive dependents.</returns>
    public HashSet<string> FindInvalidatedDefinitions(IEnumerable<string> suppliedNames)
    {
        var invalidated = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        foreach (string name in suppliedNames)
        {
            if (invalidated.Add(name))
            {
                pending.Enqueue(name);
            }
        }

        while (pending.Count > 0)
        {
            string changed = pending.Dequeue();
            if (!this.reverseDependents.TryGetValue(changed, out ImmutableArray<string> dependents))
            {
                continue;
            }

            foreach (string dependent in dependents)
            {
                if (invalidated.Add(dependent))
                {
                    pending.Enqueue(dependent);
                }
            }
        }

        return invalidated;
    }

    /// <summary>Orders definitions after their definition dependencies and rejects cycles before any operation starts.</summary>
    private IReadOnlyList<string> GetTopologicallySortedDefinitions()
    {
        var dependencyCounts = this.definitionDependencies.ToDictionary(
            entry => entry.Key,
            entry => entry.Value.Count(this.definitions.ContainsKey),
            StringComparer.Ordinal);
        var pending = new Queue<string>(
            dependencyCounts.Where(entry => entry.Value == 0).Select(entry => entry.Key));
        var ordered = new List<string>(this.definitions.Count);

        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            ordered.Add(name);
            if (!this.reverseDependents.TryGetValue(name, out ImmutableArray<string> dependents))
            {
                continue;
            }

            foreach (string dependent in dependents)
            {
                if (!dependencyCounts.TryGetValue(dependent, out int count))
                {
                    continue;
                }

                count--;
                dependencyCounts[dependent] = count;
                if (count == 0)
                {
                    pending.Enqueue(dependent);
                }
            }
        }

        if (ordered.Count != this.definitions.Count)
        {
            string cycleName = dependencyCounts.First(entry => entry.Value > 0).Key;
            throw new CStructLayoutException("Circular expression dependency detected at: " + cycleName);
        }

        return ordered;
    }

    /// <summary>Evaluates only definitions whose complete dependency closure is layout-static.</summary>
    private Dictionary<string, Expr> BuildStaticValues(IReadOnlyList<string> orderedDefinitions)
    {
        var staticExpressions = new LayoutVariables();
        foreach (string name in orderedDefinitions)
        {
            if (this.definitionDependencies[name].All(staticExpressions.ContainsKey))
            {
                staticExpressions.Add(name, this.definitions[name].Value);
            }
        }

        return this.ResolveExpressions(staticExpressions);
    }

    /// <summary>Reduces every non-literal name through one shared work/cycle/cache session.</summary>
    private LayoutVariables ResolveExpressions(LayoutVariables expressions)
    {
        ExpressionEvaluator.ExpressionEvaluationSession session = this.evaluator.CreateSession(expressions);
        var resolvedValues = new Dictionary<string, Int128>(StringComparer.Ordinal);
        foreach (string name in expressions.Keys.ToArray())
        {
            if (expressions[name] is Literal)
            {
                continue;
            }

            try
            {
                resolvedValues.Add(name, session.Evaluate(new Identifier(name)));
            }
            catch (Exception exception) when (this.definitions.ContainsKey(name) &&
                                              exception is not CStructLayoutException &&
                                              exception is OverflowException or InvalidOperationException)
            {
                // A definition can be valid only beyond the 128-bit expression domain (`1 << 127`, a 128-bit mask):
                // the exact value is kept for enum members and the constants view, and an expression that actually
                // selects it fails with the name and the value.
                if (!this.exactEnumDefinitions.Contains(name))
                {
                    expressions[name] = this.ResolveExactOrKeep(expressions[name], expressions);
                }
            }
            catch (KeyNotFoundException) when (this.definitions.ContainsKey(name))
            {
                // A definition naming something this operation did not supply (`#define MAGIC SOMENAME`, a header
                // constant the caller never needs) stays an expression; only an expression that actually selects it
                // fails, with the same message, as a C compiler ignores an unused macro. A supplied variable is
                // still checked here, because nothing later would.
            }
        }

        foreach (KeyValuePair<string, Int128> resolved in resolvedValues)
        {
            expressions[resolved.Key] = new Literal(resolved.Value);
        }

        return expressions;
    }
}
