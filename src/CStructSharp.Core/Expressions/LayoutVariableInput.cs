namespace CStructSharp.Expressions;

using System.Collections.Generic;
using CStructSharp.Syntax;

/// <summary>Distinguishes public integer variables from private expression fixtures without an adapter allocation.</summary>
internal readonly struct LayoutVariableInput
{
    private readonly IReadOnlyDictionary<string, Expr>? expressions;
    private readonly IReadOnlyDictionary<string, int>? integers;
    private readonly bool useIntegers;

    /// <summary>Initializes an integer or expression variable source.</summary>
    private LayoutVariableInput(
        IReadOnlyDictionary<string, int>? integers,
        IReadOnlyDictionary<string, Expr>? expressions,
        bool useIntegers)
    {
        this.integers = integers;
        this.expressions = expressions;
        this.useIntegers = useIntegers;
    }

    /// <summary>Creates an input for the simple public contract.</summary>
    /// <param name="variables">
    ///     The caller's integer layout variables by name, or <see langword="null"/> for none.
    /// </param>
    /// <returns>An input that resolves the integers; the dictionary is read only when resolved.</returns>
    public static LayoutVariableInput FromIntegers(IReadOnlyDictionary<string, int>? variables)
    {
        return new LayoutVariableInput(variables, null, true);
    }

    /// <summary>Creates an input for internal expression-domain tests.</summary>
    /// <param name="variables">Layout variables given as expressions, or <see langword="null"/> for none.</param>
    /// <returns>An input that resolves the expressions; the dictionary is read only when resolved.</returns>
    public static LayoutVariableInput FromExpressions(IReadOnlyDictionary<string, Expr>? variables)
    {
        return new LayoutVariableInput(null, variables, false);
    }

    /// <summary>Snapshots and resolves the selected source into operation-owned variables.</summary>
    /// <param name="resolver">The resolver that validates and converts the variables for one operation.</param>
    /// <returns>A new dictionary the operation owns and may add captured field values to.</returns>
    public Dictionary<string, Expr> Resolve(LayoutVariableResolver resolver)
    {
        return this.useIntegers
                   ? resolver.CreateIntegers(this.integers)
                   : resolver.Create(this.expressions);
    }
}
