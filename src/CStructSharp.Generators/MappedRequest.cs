namespace CStructSharp.Generators;

/// <summary>Everything the generator needs from one <c>[CStructMapped]</c> type, with value equality for the incremental pipeline.</summary>
/// <param name="ClassName">The attributed type's simple name.</param>
/// <param name="Namespace">The containing namespace; <see langword="null"/> for the global namespace.</param>
/// <param name="ContainingTypes">The types that contain the attributed type, outermost first.</param>
/// <param name="Accessibility">The attributed type's declared accessibility keyword, such as <c>public</c>.</param>
/// <param name="TypeKeyword">The declaration keyword that re-declares the type as partial; see <see cref="ContainingType.KeywordOf"/>.</param>
/// <param name="IsPartial">Whether the attributed type is declared <c>partial</c>.</param>
/// <param name="ContainersArePartial">Whether every containing type is declared <c>partial</c>.</param>
/// <param name="HasParameterlessConstructor">Whether the mapper can create the type: a value type, or a class with an internal or public parameterless constructor.</param>
/// <param name="Layout">The <c>Layout</c> argument naming the layout struct for direct members; <see langword="null"/> when not given.</param>
/// <param name="Members">The public settable instance properties the mapper fills, in declaration order.</param>
/// <param name="AttributeSpan">The attribute's source location, for diagnostics.</param>
internal sealed record MappedRequest(
    string ClassName,
    string? Namespace,
    EquatableArray<ContainingType> ContainingTypes,
    string Accessibility,
    string TypeKeyword,
    bool IsPartial,
    bool ContainersArePartial,
    bool HasParameterlessConstructor,
    string? Layout,
    EquatableArray<MappedMember> Members,
    SourceSpan AttributeSpan)
{
    /// <summary>The generated file's hint name: unique per attributed type.</summary>
    public string HintName
    {
        get
        {
            string prefix = this.Namespace is null ? string.Empty : this.Namespace + ".";
            string containers = string.Concat(System.Linq.Enumerable.Select(this.ContainingTypes, container => container.Name + "."));
            return prefix + containers + this.ClassName + ".CStructMapped.g.cs";
        }
    }
}
