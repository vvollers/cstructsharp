namespace CStructSharp;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Structure;

/// <summary>Caches static definitions and recomputes only values affected by caller overrides.</summary>
internal sealed class LayoutVariableResolver
{
    private readonly FrozenDictionary<string, ImmutableArray<string>> definitionDependencies;
    private readonly FrozenDictionary<string, Defines> definitions;
    private readonly ExpressionEvaluator evaluator;
    private readonly FrozenSet<string> exactEnumDefinitions;
    private readonly FrozenDictionary<string, ImmutableArray<string>> reverseDependents;
    private readonly FrozenDictionary<string, Expr> staticValues;

    /// <summary>Compiles definition dependencies and resolves the immutable no-override baseline once.</summary>
    public LayoutVariableResolver(
        IEnumerable<Defines> definitions,
        ExpressionEvaluator evaluator,
        IReadOnlySet<string>? exactEnumDefinitions = null)
    {
        this.evaluator = evaluator;
        IEnumerable<string> enumDefinitions = exactEnumDefinitions is null
                                                  ? Array.Empty<string>()
                                                  : exactEnumDefinitions;
        this.exactEnumDefinitions = enumDefinitions.ToFrozenSet(StringComparer.Ordinal);
        this.definitions = definitions.ToFrozenDictionary(
            define => define.Name.Name,
            define => define,
            StringComparer.Ordinal);

        try
        {
            this.definitionDependencies = this.definitions.ToFrozenDictionary(
                entry => entry.Key,
                entry => this.evaluator.GetDependencies(entry.Value.Value).ToImmutableArray(),
                StringComparer.Ordinal);
            this.reverseDependents = this.BuildReverseDependents();
            this.staticValues = this.BuildStaticValues(this.GetTopologicallySortedDefinitions()).
                ToFrozenDictionary(StringComparer.Ordinal);
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

    /// <summary>Returns an isolated operation dictionary, reusing every unaffected static literal.</summary>
    public Dictionary<string, Expr> Create(IReadOnlyDictionary<string, Expr>? suppliedVariables)
    {
        return this.CreateCore(suppliedVariables, static expression => expression);
    }

    /// <summary>Snapshots public integer overrides directly into the operation dictionary.</summary>
    public Dictionary<string, Expr> CreateIntegers(IReadOnlyDictionary<string, int>? suppliedVariables)
    {
        return this.CreateCore(suppliedVariables, static value => new Literal(value));
    }

    /// <summary>Resolves one caller-owned variable shape without creating an intermediate adapter dictionary.</summary>
    private Dictionary<string, Expr> CreateCore<T>(
        IReadOnlyDictionary<string, T>? suppliedVariables,
        Func<T, Expr> convert)
    {
        bool hasSuppliedVariables = suppliedVariables is { Count: > 0, };
        if (!hasSuppliedVariables && this.staticValues.Count == this.definitions.Count)
        {
            return new Dictionary<string, Expr>(this.staticValues, StringComparer.Ordinal);
        }

        try
        {
            var variables = new Dictionary<string, Expr>(this.staticValues, StringComparer.Ordinal);
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
                foreach (KeyValuePair<string, T> supplied in suppliedVariables!)
                {
                    variables[supplied.Key] = convert(supplied.Value);
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

    /// <summary>Returns only definitions whose complete dependency closure is known at layout-compilation time.</summary>
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
    private FrozenDictionary<string, ImmutableArray<string>> BuildReverseDependents()
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

        return reverse.ToFrozenDictionary(
            entry => entry.Key,
            entry => entry.Value.ToImmutableArray(),
            StringComparer.Ordinal);
    }

    /// <summary>Finds supplied names and every definition that depends on them directly or transitively.</summary>
    private HashSet<string> FindInvalidatedDefinitions(IEnumerable<string> suppliedNames)
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
        var staticExpressions = new Dictionary<string, Expr>(StringComparer.Ordinal);
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
    private Dictionary<string, Expr> ResolveExpressions(Dictionary<string, Expr> expressions)
    {
        ExpressionEvaluator.ExpressionEvaluationSession session = this.evaluator.CreateSession(expressions);
        var resolvedValues = new Dictionary<string, int>(StringComparer.Ordinal);
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
            catch (Exception exception) when (this.exactEnumDefinitions.Contains(name) &&
                                              exception is not CStructLayoutException &&
                                              exception is OverflowException or InvalidOperationException)
            {
                // A definition can be valid only in a wider enum domain (for example, 1 << 63). Retain that immutable
                // expression for the enum's exact evaluator; an Int32 consumer still fails when it actually selects it.
            }
        }

        foreach (KeyValuePair<string, int> resolved in resolvedValues)
        {
            expressions[resolved.Key] = new Literal(resolved.Value);
        }

        return expressions;
    }
}
