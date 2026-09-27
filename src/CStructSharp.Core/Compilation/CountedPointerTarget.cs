namespace CStructSharp.Compilation;

/// <summary>
///     The compiled facts of a pointer declarator's <c>@count(N)</c>, shared by the field and every view derived from
///     it: the target's element count and, once needed, the scalar view that reads one element.
/// </summary>
/// <param name="elements">The one-dimensional array shape of the target, fixed or counted at run time.</param>
internal sealed class CountedPointerTarget(CompiledArrayShape elements)
{
    /// <summary>The target's element count strategy.</summary>
    public CompiledArrayShape Elements { get; } = elements;

    /// <summary>The scalar view of one target element, set on first use.</summary>
    public CompiledField? Element { get; set; }
}
