namespace CStructSharp.Syntax;

using System.Collections.Generic;

/// <summary>Retains switch labels until constants can be resolved, including labels on empty arms.</summary>
internal sealed class SwitchCaseValidation : Field
{
    /// <summary>Records the case labels of one switch arm.</summary>
    /// <param name="tags">The arm's case label expressions.</param>
    internal SwitchCaseValidation(IReadOnlyList<Expr> tags)
        : base(new Identifier("uint8"), new Identifier(string.Empty), NoArray, NoneExpr.Instance)
    {
        this.Tags = tags;
    }

    internal IReadOnlyList<Expr> Tags { get; }
}
