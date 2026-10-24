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
///     <c>ConsumerDeclaredTypes</c> also includes partial declarations with an implicit constructor: their instance
///     initializers may run caller code, so reader-only bitfield shortcuts leave those classes on the original path.
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
    bool Enumerators,
    bool RuntimeByteOrder,
    GenerationSurface Surface,
    SourceSpan AttributeSpan,
    SourceSpan? DefinitionSpan,
    DefinitionLiteralShape DefinitionLiteral,
    string LanguageVersion,
    EquatableArray<string> TypesWithConstructors,
    EquatableArray<string> ConsumerDeclaredTypes,
    EquatableArray<string> ExplicitArguments)
{
    /// <summary>
    ///     Applies the project's defaults to every option the attribute did not name: the attribute's own arguments
    ///     (<see cref="ExplicitArguments"/>) always win, then the project's, then the library's.
    /// </summary>
    /// <param name="defaults">The project defaults read from the build properties.</param>
    /// <returns>The request with the effective options; this instance when the project sets none.</returns>
    public LayoutRequest WithDefaults(LayoutDefaults defaults)
    {
        if (defaults == LayoutDefaults.None)
        {
            return this;
        }

        bool Named(string name)
        {
            foreach (string argument in this.ExplicitArguments)
            {
                if (argument == name)
                {
                    return true;
                }
            }

            return false;
        }

        LayoutSettings settings = this.Settings.WithDefaults(defaults, Named);
        return this with
        {
            Settings = settings,
            KeepNames = Named("KeepNames") ? this.KeepNames : defaults.KeepNames ?? this.KeepNames,
            Views = Named("Views") ? this.Views : defaults.Views ?? this.Views,
            Enumerators = Named("Enumerators") ? this.Enumerators : defaults.Enumerators ?? this.Enumerators,
            RuntimeByteOrder = Named("RuntimeByteOrder") ? this.RuntimeByteOrder : defaults.RuntimeByteOrder ?? this.RuntimeByteOrder,
            Surface = Named("Surface") ? this.Surface : defaults.Surface ?? this.Surface,
        };
    }

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
