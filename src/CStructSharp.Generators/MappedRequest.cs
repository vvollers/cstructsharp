namespace CStructSharp.Generators;

/// <summary>Everything the generator needs from one <c>[CStructMapped]</c> type, with value equality for the incremental pipeline.</summary>
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
