namespace CStructSharp.Compilation.Programs;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     The per-layout table of layout-variable slots. Every name a layout expression can read gets one slot: the
///     identifiers the layout's expressions name (dotted names expanded, as capture decides it), every definition and
///     qualified enum member, and every name a definition reads. An operation then holds its variables as an array
///     indexed by slot instead of a dictionary keyed by name.
/// </summary>
/// <remarks>
///     <para>
///         A name without a slot cannot be observed by any expression of the layout: a caller variable of that name is
///         ignored, and a qualified publication target (<c>hdr.n</c>) that no expression spells needs no slot either.
///         The operation's qualified-prefix entry has no slot, because no expression can spell its name.
///     </para>
///     <para>
///         The table is immutable after construction and is shared by every thread. It precomputes the operation's
///         initial state without caller variables (exactly what <see cref="LayoutVariableResolver.CreateIntegers"/>
///         returns for none) and, per slot, the definitions that depend on it, which decide whether a caller variable
///         can simply overwrite its slot. The only state it gains later is its caches (expression programs and
///         <see cref="ReadPrograms"/>), which are thread-safe and hold values derived from the immutable parts.
///     </para>
/// </remarks>
internal sealed class SlotTable
{
    private static readonly int[] NoSlots = [];

    private readonly int[][] dependentDefinitions;
    private readonly ExpressionEvaluator evaluator;
    private readonly string[] names;
    private readonly ConditionalWeakTable<Expr, ProgramExpression> programs = new();
    private readonly LayoutVariableResolver resolver;
    private readonly Dictionary<string, int> slots;
    private readonly SlotValue[]? staticState;
    private ReadProgramCache? readPrograms;

    /// <summary>Assigns the slots, then records dependents and the static state, which compile against them.</summary>
    /// <param name="names">The distinct names, in slot order.</param>
    /// <param name="resolver">The layout's definition resolver.</param>
    /// <param name="evaluator">The layout's bounded evaluator.</param>
    private SlotTable(string[] names, LayoutVariableResolver resolver, ExpressionEvaluator evaluator)
    {
        this.names = names;
        this.resolver = resolver;
        this.evaluator = evaluator;
        this.slots = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        for (int slot = 0; slot < names.Length; slot++)
        {
            this.slots.Add(names[slot], slot);
        }

        this.dependentDefinitions = new int[names.Length][];
        for (int slot = 0; slot < names.Length; slot++)
        {
            HashSet<string> invalidated = resolver.FindInvalidatedDefinitions([names[slot]]);
            invalidated.Remove(names[slot]);
            this.dependentDefinitions[slot] = invalidated.Count == 0
                                                  ? NoSlots
                                                  : invalidated.Select(name => this.slots[name]).OrderBy(slot => slot).ToArray();
        }

        this.staticState = this.ResolveStaticState();
    }

    /// <summary>Gets the number of slots.</summary>
    public int Count => this.names.Length;

    /// <summary>Gets the evaluator whose limits and compiled programs the slot programs share.</summary>
    public ExpressionEvaluator Evaluator => this.evaluator;

    /// <summary>
    ///     Gets a value indicating whether the state without caller variables resolved when the table was built. When it
    ///     did not (the resolver fails for every operation, such as a definition exceeding the work limit), every
    ///     operation initializes through the resolver and fails as it does today.
    /// </summary>
    public bool HasStaticState => this.staticState is not null;

    /// <summary>
    ///     Gets the read programs compiled against this table (<see cref="LayoutCompilation.GetReadProgram"/>), created
    ///     on first access: a program's slots are this table's, so its cache lives with the table.
    /// </summary>
    public ReadProgramCache ReadPrograms
        => Volatile.Read(ref this.readPrograms) ?? Interlocked.CompareExchange(ref this.readPrograms, new ReadProgramCache(this), null) ?? this.readPrograms!;

    /// <summary>Builds the table of one compiled layout.</summary>
    /// <param name="referencedNames">The names the layout's expressions read, with dotted names expanded.</param>
    /// <param name="resolver">The layout's definition resolver.</param>
    /// <param name="evaluator">The layout's bounded evaluator.</param>
    /// <returns>The table; slots are numbered in ordinal name order, so equal layouts number them equally.</returns>
    public static SlotTable Create(IEnumerable<string> referencedNames, LayoutVariableResolver resolver, ExpressionEvaluator evaluator)
    {
        var names = new HashSet<string>(referencedNames, StringComparer.Ordinal);
        foreach (string definition in resolver.Definitions.Keys)
        {
            names.Add(definition);
            foreach (string dependency in resolver.GetDefinitionDependencies(definition))
            {
                names.Add(dependency);
            }
        }

        string[] ordered = [.. names,];
        Array.Sort(ordered, StringComparer.Ordinal);
        return new SlotTable(ordered, resolver, evaluator);
    }

    /// <summary>Finds a name's slot.</summary>
    /// <param name="name">The name.</param>
    /// <param name="slot">The slot, or -1.</param>
    /// <returns>Whether the name has a slot.</returns>
    public bool TryGetSlot(string name, out int slot)
    {
        if (this.slots.TryGetValue(name, out slot))
        {
            return true;
        }

        slot = -1;
        return false;
    }

    /// <summary>Returns the name of a slot.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The name.</returns>
    public string GetName(int slot) => this.names[slot];

    /// <summary>
    ///     Returns the definitions that depend on a slot's name, directly or through other definitions
    ///     (<see cref="LayoutVariableResolver.FindInvalidatedDefinitions"/> without the name itself): the definitions a
    ///     caller value of that name makes the resolver evaluate again.
    /// </summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The dependents' slots in ascending order; empty when nothing depends on the name.</returns>
    public IReadOnlyList<int> GetDependentDefinitions(int slot) => this.dependentDefinitions[slot];

    /// <summary>
    ///     Maps names to slots, for state the executor keeps by name list, such as a conditional composite's
    ///     <see cref="CompiledConditionalScope.LocalNames"/>: removing or restoring a name without a slot is not observable.
    /// </summary>
    /// <param name="names">The names.</param>
    /// <returns>Each name's slot, or -1 when it has none.</returns>
    public int[] MapNames(IReadOnlyList<string> names)
    {
        int[] mapped = new int[names.Count];
        for (int index = 0; index < mapped.Length; index++)
        {
            _ = this.TryGetSlot(names[index], out mapped[index]);
        }

        return mapped;
    }

    /// <summary>Returns the slot-indexed program of an expression, compiled once per expression node and table.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The program; it evaluates through the dictionary evaluator when it cannot run on slots.</returns>
    public ProgramExpression Compile(Expr expression)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        return this.programs.GetValue(expression, source => new ProgramExpression(this, source));
    }

    /// <summary>Converts a dictionary entry into the slot value that evaluates the same way.</summary>
    /// <param name="expression">The entry: a literal, an unusable variable, an identifier, or an expression.</param>
    /// <returns>
    ///     A literal or out-of-domain value, an unusable value, an identifier value for a name without a slot, or a live
    ///     expression (an identifier with a slot is a one-instruction live expression, as the dictionary evaluator treats it).
    /// </returns>
    public SlotValue ToSlotValue(Expr expression) => expression switch
    {
        Literal { IsInDomain: true, } literal => SlotValue.FromLiteral(literal.Value),
        Literal literal => SlotValue.FromOutOfDomain(literal.ExactValue),
        UnusableVariable unusable => SlotValue.FromUnusable(unusable),
        Identifier identifier when !this.slots.ContainsKey(identifier.Name) => SlotValue.FromIdentifier(identifier.Name),
        _ => SlotValue.FromLiveExpression(this.Compile(expression)),
    };

    /// <summary>
    ///     Writes an operation's initial state for public integer variables into <paramref name="destination"/>, equal
    ///     to the dictionary <see cref="LayoutVariableResolver.CreateIntegers"/> returns: the static state with each
    ///     caller value overwriting its slot. A caller value of a name that definitions depend on makes the resolver
    ///     evaluate those definitions again, in one session over its dictionary in its order; that call is delegated to
    ///     the resolver and its result loaded, so the state is the resolver's by construction.
    /// </summary>
    /// <param name="destination">The slot array, at least <see cref="Count"/> long; slots beyond it are not written.</param>
    /// <param name="variables">The caller's variables, or <see langword="null"/>; names without a slot are ignored.</param>
    /// <exception cref="CStructLayoutException">A definition cannot be resolved (the resolver's failure).</exception>
    public void Initialize(SlotValue[] destination, IReadOnlyDictionary<string, int>? variables)
    {
        if (this.staticState is { } state)
        {
            Array.Copy(state, destination, state.Length);
            if (variables is null || variables.Count == 0)
            {
                return;
            }

            bool delegated = false;
            foreach (KeyValuePair<string, int> variable in variables)
            {
                if (!this.slots.TryGetValue(variable.Key, out int slot))
                {
                    continue;
                }

                if (this.dependentDefinitions[slot].Length > 0)
                {
                    delegated = true;
                    break;
                }

                destination[slot] = SlotValue.FromLiteral(variable.Value);
            }

            if (!delegated)
            {
                return;
            }
        }

        _ = this.Load(destination, this.resolver.CreateIntegers(variables), false);
    }

    /// <summary>
    ///     Writes an operation's initial state for internal expression variables (<see cref="LayoutVariableInput.FromExpressions"/>)
    ///     from the resolver's dictionary. Entries without a slot are returned, because a supplied expression that
    ///     stays unevaluated can name them.
    /// </summary>
    /// <param name="destination">The slot array, at least <see cref="Count"/> long.</param>
    /// <param name="variables">The supplied expressions, or <see langword="null"/>.</param>
    /// <param name="captureAll">Receives whether the operation must capture every field (<see cref="LayoutVariables.CaptureAll"/>).</param>
    /// <returns>The entries whose names have no slot, or <see langword="null"/> when there are none.</returns>
    /// <exception cref="CStructLayoutException">A definition or supplied expression cannot be resolved.</exception>
    public Dictionary<string, Expr>? Initialize(SlotValue[] destination, IReadOnlyDictionary<string, Expr>? variables, out bool captureAll)
    {
        Dictionary<string, Expr> resolved = this.resolver.Create(variables);
        captureAll = resolved is LayoutVariables { CaptureAll: true, };
        return this.Load(destination, resolved, true);
    }

    /// <summary>
    ///     Builds the dictionary the slots stand for: every defined slot under its name, then the entries without a
    ///     slot. The dictionary evaluator run over it gives the result the slot evaluation must give.
    /// </summary>
    /// <param name="values">The slot array.</param>
    /// <param name="unslotted">The entries without a slot, or <see langword="null"/>.</param>
    /// <returns>A new dictionary.</returns>
    public Dictionary<string, Expr> CreateDictionary(SlotValue[] values, IReadOnlyDictionary<string, Expr>? unslotted)
    {
        var dictionary = new Dictionary<string, Expr>(StringComparer.Ordinal);
        for (int slot = 0; slot < this.names.Length; slot++)
        {
            if (values[slot].ToExpression() is { } expression)
            {
                dictionary.Add(this.names[slot], expression);
            }
        }

        if (unslotted is not null)
        {
            foreach (KeyValuePair<string, Expr> entry in unslotted)
            {
                dictionary.Add(entry.Key, entry.Value);
            }
        }

        return dictionary;
    }

    /// <summary>Evaluates an expression with the dictionary evaluator over the dictionary the slots stand for.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="values">The slot array.</param>
    /// <param name="unslotted">The entries without a slot, or <see langword="null"/>.</param>
    /// <returns>The value; failures are the dictionary evaluator's.</returns>
    internal Int128 EvaluateWithDictionary(Expr expression, SlotValue[] values, IReadOnlyDictionary<string, Expr>? unslotted)
        => this.evaluator.Evaluate(expression, this.CreateDictionary(values, unslotted));

    /// <summary>
    ///     Resolves the state without caller variables once. The resolver resolves definitions that name non-definitions
    ///     again on every call without variables, deterministically, so one result stands for all of them; when that
    ///     fails, there is no static state and each operation asks the resolver (and fails as it always did).
    /// </summary>
    /// <returns>The state, or <see langword="null"/> when the resolver fails without caller variables.</returns>
    private SlotValue[]? ResolveStaticState()
    {
        Dictionary<string, Expr> resolved;
        try
        {
            resolved = this.resolver.CreateIntegers(null);
        }
        catch (CStructLayoutException)
        {
            return null;
        }

        var state = new SlotValue[this.names.Length];
        _ = this.Load(state, resolved, false);
        return state;
    }

    /// <summary>Converts a resolver dictionary into slot values.</summary>
    /// <param name="destination">The slot array.</param>
    /// <param name="resolved">The resolver's dictionary.</param>
    /// <param name="keepUnslotted">Whether entries without a slot are returned (expression inputs) or dropped (integers).</param>
    /// <returns>The entries without a slot, or <see langword="null"/>.</returns>
    private Dictionary<string, Expr>? Load(SlotValue[] destination, Dictionary<string, Expr> resolved, bool keepUnslotted)
    {
        for (int slot = 0; slot < this.names.Length; slot++)
        {
            destination[slot] = resolved.TryGetValue(this.names[slot], out Expr? expression)
                                    ? this.ToSlotValue(expression)
                                    : SlotValue.Undefined;
        }

        Dictionary<string, Expr>? unslotted = null;
        if (keepUnslotted && resolved.Count > 0)
        {
            foreach (KeyValuePair<string, Expr> entry in resolved)
            {
                if (!this.slots.ContainsKey(entry.Key))
                {
                    (unslotted ??= new Dictionary<string, Expr>(StringComparer.Ordinal)).Add(entry.Key, entry.Value);
                }
            }
        }

        return unslotted;
    }
}
