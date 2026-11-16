namespace CStructSharp.Generators;

/// <summary>
///     Everything the generator needs from one <c>[CStructLayout]</c> attribute, extracted from the semantic model
///     in the transform step. It is a record with value equality (every member is a value, a string, or an
///     <see cref="EquatableArray{T}"/>), so the incremental pipeline re-runs generation only when something the
///     output depends on changed.
/// </summary>
/// <param name="ClassName">The attributed class's simple name.</param>
/// <param name="Namespace">The containing namespace; <see langword="null"/> for the global namespace.</param>
/// <param name="ContainingTypes">The types that contain the attributed class, outermost first.</param>
/// <param name="Accessibility">The attributed class's declared accessibility keyword, such as <c>public</c>.</param>
/// <param name="IsStatic">Whether the attributed class is declared <c>static</c>.</param>
/// <param name="IsPartial">Whether the attributed class is declared <c>partial</c>.</param>
/// <param name="ContainersArePartial">Whether every containing type is declared <c>partial</c>.</param>
/// <param name="Definition">The inline layout text; <see langword="null"/> when the layout comes from <paramref name="File"/>.</param>
/// <param name="File">The project-relative path of the <c>.cstruct</c> additional file; <see langword="null"/> for an inline layout.</param>
/// <param name="Root">The declaration the plain <c>Parse</c>/<c>Serialize</c> methods use; <see langword="null"/> for the first struct.</param>
/// <param name="Settings">The layout settings (alignment, byte order, pointer size and the like) the attribute names.</param>
/// <param name="KeepNames">Whether generated names keep the layout's spelling instead of becoming PascalCase.</param>
/// <param name="Views">Whether a <c>ref struct</c> view is generated per composite.</param>
/// <param name="Enumerators">Whether each view of a fixed-size struct gets a <c>foreach</c> enumerator over consecutive records.</param>
/// <param name="RuntimeByteOrder">Whether the byte order of neutral values is chosen when a value is read instead of at compile time.</param>
/// <param name="Shape">Whether composites become classes, value types, or a one-member value.</param>
/// <param name="MaxValueBytes">The largest struct, in bytes, that a value-type shape may generate.</param>
/// <param name="Surface">How much code is generated.</param>
/// <param name="FormatConstraintFailures">Whether a failed constraint throws a format exception (<see cref="System.IO.InvalidDataException"/> or <see cref="System.NotSupportedException"/>) instead of the constraint exception.</param>
/// <param name="AttributeSpan">The attribute's source location, for diagnostics.</param>
/// <param name="DefinitionSpan">The inline definition argument's source location; <see langword="null"/> when there is none.</param>
/// <param name="DefinitionLiteral">How the definition literal is written, to place layout errors inside it.</param>
/// <param name="LanguageVersion">The consumer's effective C# language version, as the compiler displays it.</param>
/// <param name="TypesWithConstructors">
///     The nested classes of the attributed class for which the consumer declared an instance constructor in a partial
///     declaration. The generated class of such a composite may not hold the new nested values its property
///     initializers create once construction ends, so its fixed reader replaces them instead of decoding into them.
/// </param>
/// <param name="ConsumerDeclaredTypes">
///     Every nested type the consumer declared, including partial declarations with an implicit constructor: their
///     instance initializers may run caller code, so reader-only bitfield shortcuts leave those classes on the
///     original path.
/// </param>
/// <param name="ConsumerDeclaredClasses">
///     The nested types the consumer declared as classes, which a value-type shape cannot extend (a partial
///     declaration must keep one kind).
/// </param>
/// <param name="ExplicitArguments">The named attribute arguments that choose an option, so a project default does not replace them.</param>
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
    GenerationShape Shape,
    int MaxValueBytes,
    GenerationSurface Surface,
    bool FormatConstraintFailures,
    SourceSpan AttributeSpan,
    SourceSpan? DefinitionSpan,
    DefinitionLiteralShape DefinitionLiteral,
    string LanguageVersion,
    EquatableArray<string> TypesWithConstructors,
    EquatableArray<string> ConsumerDeclaredTypes,
    EquatableArray<string> ConsumerDeclaredClasses,
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

        // Whether the attribute named this option, so the project's default must not replace it.
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
            Shape = Named("Shape") ? this.Shape : defaults.Shape ?? this.Shape,
            MaxValueBytes = Named("MaxValueBytes") ? this.MaxValueBytes : defaults.MaxValueBytes ?? this.MaxValueBytes,
            Surface = Named("Surface") ? this.Surface : defaults.Surface ?? this.Surface,
            FormatConstraintFailures = Named("ConstraintFailures") ? this.FormatConstraintFailures : defaults.FormatConstraintFailures ?? this.FormatConstraintFailures,
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
