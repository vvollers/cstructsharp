namespace CStructSharp.Generators;

/// <summary>
///     Everything the generator needs from one <c>[CStructLayout]</c> attribute, extracted from the semantic model
///     in the transform step. It is a record with value equality (every member is a value, a string, or an
///     <see cref="EquatableArray{T}"/>), so the incremental pipeline re-runs generation only when something the
///     output depends on changed.
/// </summary>
/// <remarks>
///     <c>TypesWithConstructors</c> names the nested classes of the attributed class for which the consumer declared
///     an instance constructor in a partial declaration. The generated class of such a composite may not hold the new
///     nested values its property initializers create once construction ends, so its fixed reader replaces them
///     instead of decoding into them.
/// </remarks>
internal sealed record LayoutRequest(
    string ClassName,
    string? Namespace,
    EquatableArray<ContainingType> ContainingTypes,
    string Accessibility,
    bool IsStatic,
    bool IsPartial,
    bool ContainersArePartial,
    string? Definition,
    string? File,
    string? Root,
    LayoutSettings Settings,
    bool KeepNames,
    bool Views,
    SourceSpan AttributeSpan,
    SourceSpan? DefinitionSpan,
    DefinitionLiteralShape DefinitionLiteral,
    string LanguageVersion,
    EquatableArray<string> TypesWithConstructors)
{
    /// <summary>The generated file's hint name: unique per attributed class.</summary>
    public string HintName
    {
        get
        {
            string prefix = this.Namespace is null ? string.Empty : this.Namespace + ".";
            string containers = string.Empty;
            foreach (ContainingType type in this.ContainingTypes)
            {
                containers += type.Name + ".";
            }

            return prefix + containers + this.ClassName + ".CStructLayout.g.cs";
        }
    }
}
