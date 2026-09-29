namespace CStructSharp.Expressions;

using System.Collections.Generic;
using CStructSharp.Syntax;

/// <summary>An operation's caller variables: the public integer values, read only when resolved.</summary>
internal readonly struct LayoutVariableInput
{
    private readonly IReadOnlyDictionary<string, int>? integers;

    /// <summary>Initializes an input over the caller's integer variables.</summary>
    /// <param name="integers">The caller's variables by name, or <see langword="null"/> for none.</param>
    private LayoutVariableInput(IReadOnlyDictionary<string, int>? integers)
    {
        this.integers = integers;
    }

    /// <summary>Gets the caller's integer variables, or <see langword="null"/> for none.</summary>
    public IReadOnlyDictionary<string, int>? Integers => this.integers;

    /// <summary>Creates an input for the public contract.</summary>
    /// <param name="variables">
    ///     The caller's integer layout variables by name, or <see langword="null"/> for none.
    /// </param>
    /// <returns>An input that resolves the integers; the dictionary is read only when resolved.</returns>
    public static LayoutVariableInput FromIntegers(IReadOnlyDictionary<string, int>? variables)
    {
        return new LayoutVariableInput(variables);
    }

    /// <summary>Snapshots and resolves the caller's variables into operation-owned variables.</summary>
    /// <param name="resolver">The resolver that validates and converts the variables for one operation.</param>
    /// <returns>A new dictionary the operation owns and may add captured field values to.</returns>
    public Dictionary<string, Expr> Resolve(LayoutVariableResolver resolver)
    {
        return resolver.CreateIntegers(this.integers);
    }
}
