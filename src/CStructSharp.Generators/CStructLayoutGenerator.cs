namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
///     The incremental source generator behind <c>[CStructLayout]</c>: for every attributed <c>static partial</c>
///     class it compiles the layout with the same Core the runtime uses and emits the layout's types and
///     operations as C#. The pipeline is a pure function of the <see cref="LayoutRequest"/> record (plus the
///     <c>.cstruct</c> additional files), so an unchanged attribute never re-runs generation.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CStructLayoutGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "CStructSharp.CStructLayoutAttribute";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<LayoutRequest> requests = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is ClassDeclarationSyntax,
                static (syntaxContext, cancellation) => CreateRequest(syntaxContext, cancellation))
            .Where(static request => request is not null)
            .Select(static (request, _) => request!);

        // A layout file is any AdditionalFile named *.cstruct, or one the build marks with CStructSharpLayout=true
        // (the package's build props mark every .cstruct file; a project can mark others). The file provider is part of
        // the pipeline, so an edit to a layout file re-runs the classes that use it.
        IncrementalValueProvider<ImmutableArray<(string Path, string Text)>> layoutFiles = context.AdditionalTextsProvider
            .Combine(context.AnalyzerConfigOptionsProvider)
            .Where(static pair => IsLayoutFile(pair.Left, pair.Right))
            .Select(static (pair, cancellation) => (pair.Left.Path, pair.Left.GetText(cancellation)?.ToString() ?? string.Empty))
            .Collect();

        // DisableCStructSharpGenerator=true (a CompilerVisibleProperty of the package's build props) keeps the runtime
        // and skips generation: the attributed classes stay as the consumer wrote them.
        IncrementalValueProvider<bool> disabled = context.AnalyzerConfigOptionsProvider
            .Select(static (provider, _) => provider.GlobalOptions.TryGetValue("build_property.DisableCStructSharpGenerator", out string? value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase));

        context.RegisterSourceOutput(
            requests.Combine(layoutFiles).Combine(disabled),
            static (productionContext, pair) =>
            {
                if (!pair.Right)
                {
                    Generate(productionContext, pair.Left.Left, pair.Left.Right);
                }
            });
    }

    /// <summary>Recognizes a layout by its .cstruct suffix or explicit AdditionalFiles metadata without reading its bytes.</summary>
    /// <param name="file">The compiler-owned additional file.</param>
    /// <param name="options">Per-file build metadata visible to the generator.</param>
    /// <returns>Whether this file participates in layout generation.</returns>
    private static bool IsLayoutFile(AdditionalText file, Microsoft.CodeAnalysis.Diagnostics.AnalyzerConfigOptionsProvider options)
    {
        if (file.Path.EndsWith(".cstruct", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return options.GetOptions(file).TryGetValue("build_metadata.AdditionalFiles.CStructSharpLayout", out string? marked) && string.Equals(marked, "true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The layout request of a [CStructLayout] class, for the mapped-class generator's layout resolution.</summary>
    /// <param name="context">The compiler's matched attribute, declaration and semantic symbol.</param>
    /// <param name="cancellation">Cancels syntax retrieval when the compiler abandons this generation pass.</param>
    /// <returns>The immutable request snapshot, or null when the target is not a class declaration.</returns>
    internal static LayoutRequest? CreateRequestForMapping(GeneratorAttributeSyntaxContext context, CancellationToken cancellation) => CreateRequest(context, cancellation);

    /// <summary>Snapshots one attributed class's configuration and source locations for incremental generation.</summary>
    /// <param name="context">The compiler's matched attribute, declaration and semantic symbol.</param>
    /// <param name="cancellation">Cancels syntax retrieval when the compiler abandons this generation pass.</param>
    /// <returns>An immutable request, or null when the target is not a class declaration.</returns>
    /// <remarks>No binary input is read and no layout is compiled here. Equality of the snapshot lets Roslyn reuse unchanged work.</remarks>
    /// <exception cref="OperationCanceledException">The compiler cancels syntax retrieval.</exception>
    private static LayoutRequest? CreateRequest(GeneratorAttributeSyntaxContext context, CancellationToken cancellation)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol || context.TargetNode is not ClassDeclarationSyntax declaration)
        {
            return null;
        }

        AttributeData attribute = context.Attributes[0];
        string? definition = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
        string? file = null;
        string? root = null;
        bool keepNames = false;
        bool views = true;
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            switch (named.Key)
            {
            case "File":
                file = named.Value.Value as string;
                break;
            case "Root":
                root = named.Value.Value as string;
                break;
            case "KeepNames":
                keepNames = named.Value.Value is true;
                break;
            case "Views":
                views = named.Value.Value is not false;
                break;
            }
        }

        // The attribute's own syntax gives the diagnostic locations: the whole attribute, and the definition literal.
        AttributeSyntax? attributeSyntax = attribute.ApplicationSyntaxReference?.GetSyntax(cancellation) as AttributeSyntax;
        Location attributeLocation = attributeSyntax?.GetLocation() ?? declaration.Identifier.GetLocation();
        SourceSpan? definitionSpan = null;
        var literalShape = new DefinitionLiteralShape(false, 0, 0);
        if (attributeSyntax?.ArgumentList?.Arguments.FirstOrDefault(argument => argument.NameEquals is null && argument.NameColon is null) is { } definitionArgument)
        {
            definitionSpan = SourceSpan.From(definitionArgument.GetLocation());
            literalShape = DescribeLiteral(definitionArgument.Expression);
        }

        var containers = new List<ContainingType>();
        bool containersArePartial = true;
        for (INamedTypeSymbol? container = symbol.ContainingType; container is not null; container = container.ContainingType)
        {
            containers.Insert(0, new ContainingType(TypeKeyword(container), container.Name));
            containersArePartial &= container.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellation) is TypeDeclarationSyntax type && type.Modifiers.Any(SyntaxKind.PartialKeyword));
        }

        return new LayoutRequest(
            symbol.Name,
            symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            new EquatableArray<ContainingType>(containers.ToArray()),
            SyntaxFacts.GetText(symbol.DeclaredAccessibility),
            symbol.IsStatic,
            declaration.Modifiers.Any(SyntaxKind.PartialKeyword),
            containersArePartial,
            definition,
            file,
            root,
            LayoutSettings.FromAttribute(attribute),
            keepNames,
            views,
            SourceSpan.From(attributeLocation),
            definitionSpan,
            literalShape,
            ((CSharpParseOptions)declaration.SyntaxTree.Options).LanguageVersion.ToDisplayString(),
            TypesWithConstructors(symbol));
    }

    /// <summary>
    ///     The nested classes of the attributed class for which the consumer declared an instance constructor. The
    ///     generator's own output is not part of the compilation it inspects, so every constructor found is the
    ///     consumer's.
    /// </summary>
    /// <param name="symbol">The attributed class.</param>
    /// <returns>The names of those classes, in declaration order.</returns>
    private static EquatableArray<string> TypesWithConstructors(INamedTypeSymbol symbol)
    {
        return new EquatableArray<string>(symbol.GetTypeMembers()
                                                .Where(type => type.InstanceConstructors.Any(constructor => !constructor.IsImplicitlyDeclared))
                                                .Select(type => type.Name)
                                                .ToArray());
    }

    /// <summary>The declaration keyword of a containing type, for re-declaring it around the generated partial class.</summary>
    /// <param name="type">The containing type.</param>
    /// <returns><c>class</c>, <c>struct</c>, <c>record</c>, <c>record struct</c>, or <c>interface</c>.</returns>
    private static string TypeKeyword(INamedTypeSymbol type)
    {
        return type.TypeKind switch
        {
            TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            _ => type.IsRecord ? "record" : "class",
        };
    }

    /// <summary>
    ///     Describes the definition argument's literal: for a multi-line raw string, the source line its content
    ///     starts on and the indentation its closing quotes remove, so a layout line and column map to the file.
    /// </summary>
    /// <param name="expression">The definition argument.</param>
    /// <returns>The shape; not raw multi-line for any other expression.</returns>
    private static DefinitionLiteralShape DescribeLiteral(ExpressionSyntax expression)
    {
        if (expression is LiteralExpressionSyntax { Token: { } token } && token.IsKind(SyntaxKind.MultiLineRawStringLiteralToken))
        {
            // The raw literal's content starts on the line after the opening quotes; the closing quotes' indentation
            // is what the compiler strips from every content line.
            FileLinePositionSpan span = token.GetLocation().GetLineSpan();
            string text = token.Text;
            int lastNewLine = text.LastIndexOf('\n');
            int indentation = lastNewLine < 0 ? 0 : text.Length - lastNewLine - 1 - text.Substring(lastNewLine + 1).TrimStart().Length;
            return new DefinitionLiteralShape(true, span.StartLinePosition.Line + 1, indentation);
        }

        return new DefinitionLiteralShape(false, 0, 0);
    }

    /// <summary>
    ///     Generates one <c>[CStructLayout]</c> class: checks the class shape and language version, reads the definition
    ///     (inline or from a <c>.cstruct</c> file), builds the codec catalog and the layout as the runtime would, and
    ///     emits the source. Every failure becomes a diagnostic instead of generated code.
    /// </summary>
    /// <param name="context">The output for sources and diagnostics.</param>
    /// <param name="request">The attribute's settings and source spans.</param>
    /// <param name="layoutFiles">The project's <c>.cstruct</c> additional files.</param>
    private static void Generate(SourceProductionContext context, LayoutRequest request, ImmutableArray<(string Path, string Text)> layoutFiles)
    {
        if (!request.IsPartial || !request.IsStatic || !request.ContainersArePartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.NotPartial, request.AttributeSpan.ToLocation(), request.ClassName));
            return;
        }

        if (!LanguageVersionIsAtLeast12(request.LanguageVersion))
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.LanguageVersion, request.AttributeSpan.ToLocation(), request.LanguageVersion));
            return;
        }

        string? definition = request.Definition;
        if (definition is null)
        {
            if (request.File is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.LayoutInvalid, request.AttributeSpan.ToLocation(), "[CStructLayout] needs either the definition text or File = \"name.cstruct\"."));
                return;
            }

            definition = FindLayoutFile(layoutFiles, request.File);
            if (definition is null)
            {
                context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.FileNotFound, request.AttributeSpan.ToLocation(), request.File));
                return;
            }
        }

        if (!request.Settings.TryParseCodecs(out List<CustomCodecDescriptor> codecs, out string? invalid))
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.CodecDeclarationInvalid, request.AttributeSpan.ToLocation(), invalid));
            return;
        }

        // The codecs join the catalog in their own step, so a failure there is a codec declaration's.
        PrimitiveCatalog catalog;
        try
        {
            catalog = request.Settings.CodecCatalog(codecs);
        }
        catch (ArgumentException exception)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.CodecDeclarationInvalid, request.AttributeSpan.ToLocation(), exception.Message));
            return;
        }

        LayoutCompilation compilation;
        try
        {
            compilation = request.Settings.Compile(definition, catalog);
        }
        catch (Exception exception) when (exception is CStructException or ArgumentException)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.LayoutInvalid, LocateLayoutError(request, exception), exception.Message));
            return;
        }

        string rootName = compilation.DefaultRoot;
        if (request.Root is not null)
        {
            if (compilation.CStructElements.ContainsKey(request.Root))
            {
                rootName = request.Root;
            }
            else
            {
                context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.UnknownRoot, request.AttributeSpan.ToLocation(), request.Root, rootName));
            }
        }

        var emitter = new LayoutEmitter(request, compilation, definition, rootName);
        foreach (Diagnostic diagnostic in emitter.Validate())
        {
            context.ReportDiagnostic(diagnostic);
        }

        if (emitter.HasErrors)
        {
            return;
        }

        context.AddSource(request.HintName, SourceText.From(emitter.Emit(), System.Text.Encoding.UTF8));
    }

    /// <summary>Whether the consuming project's C# version is 12 or later, which the generated code needs.</summary>
    /// <param name="languageVersion">The version's display text.</param>
    /// <returns>Whether it is at least C# 12.</returns>
    private static bool LanguageVersionIsAtLeast12(string languageVersion)
    {
        return LanguageVersionFacts.TryParse(languageVersion, out LanguageVersion parsed) && parsed >= Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12;
    }

    /// <summary>The text of the <c>.cstruct</c> additional file the attribute's <c>File</c> names: the same path, or one ending with it.</summary>
    /// <param name="files">The project's layout files.</param>
    /// <param name="requested">The attribute's relative path, with either slash.</param>
    /// <returns>The file's text, or <see langword="null"/> when no file matches.</returns>
    private static string? FindLayoutFile(ImmutableArray<(string Path, string Text)> files, string requested)
    {
        string normalized = requested.Replace('\\', '/');
        foreach ((string path, string text) in files)
        {
            string candidate = path.Replace('\\', '/');
            if (candidate.Equals(normalized, StringComparison.OrdinalIgnoreCase) || candidate.EndsWith("/" + normalized, StringComparison.OrdinalIgnoreCase))
            {
                return text;
            }
        }

        return null;
    }

    /// <summary>
    ///     Places a layout error inside the definition literal when the error has a line and column and the literal
    ///     is a multi-line raw string (whose lines map one to one); otherwise at the argument.
    /// </summary>
    /// <param name="request">The attribute's settings and source spans.</param>
    /// <param name="exception">The compilation failure.</param>
    /// <returns>The diagnostic's location.</returns>
    private static Location LocateLayoutError(LayoutRequest request, Exception exception)
    {
        if (request.DefinitionSpan is not { } span)
        {
            return request.AttributeSpan.ToLocation();
        }

        if (request.DefinitionLiteral.IsRawMultiLine && exception is CStructLayoutException { Line: int line, Column: int column, })
        {
            int sourceLine = request.DefinitionLiteral.ContentStartLine + line - 1;
            int sourceColumn = request.DefinitionLiteral.Indentation + Math.Max(0, column - 1);
            var position = new LinePosition(sourceLine, sourceColumn);
            return Location.Create(span.FilePath, new TextSpan(span.Start, 0), new LinePositionSpan(position, new LinePosition(sourceLine, sourceColumn + 1)));
        }

        return span.ToLocation();
    }
}
