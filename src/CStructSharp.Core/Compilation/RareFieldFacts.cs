namespace CStructSharp.Compilation;

using System.Collections.Immutable;

/// <summary>
///     The rare compiled facts of a field, behind one reference so that a field without them costs only that reference:
///     a pointer declarator's <c>@count(N)</c> (the target's element count and, once needed, the scalar view that reads one
///     element) and the field's <c>@expect</c> and <c>@supports</c> conditions. The field and every view derived from it
///     share the object.
/// </summary>
/// <param name="elements">The one-dimensional array shape of a counted pointer target, fixed or counted at run time; <see langword="null"/> for a field that only has constraints.</param>
internal sealed class RareFieldFacts(CompiledArrayShape? elements)
{
    /// <summary>The target's element count strategy, or <see langword="null"/> when the field has no <c>@count</c>.</summary>
    public CompiledArrayShape? Elements { get; } = elements;

    /// <summary>The scalar view of one target element, set on first use.</summary>
    public CompiledField? Element { get; set; }

    /// <summary>The field's constraints in declaration order.</summary>
    public ImmutableArray<CompiledConstraint> Constraints { get; set; } = ImmutableArray<CompiledConstraint>.Empty;
}
