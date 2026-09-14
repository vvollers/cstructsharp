namespace CStructSharp.Structure;

using System.Collections.Generic;

/// <summary>Retains switch labels until constants can be resolved, including labels on empty arms.</summary>
internal sealed class SwitchCaseValidation : Field
{
    internal SwitchCaseValidation(IReadOnlyList<Expr> tags)
        : base(new Identifier("uint8"), new Identifier(string.Empty), NoArray, 0)
    {
        this.Tags = tags;
    }

    internal IReadOnlyList<Expr> Tags { get; }
}
