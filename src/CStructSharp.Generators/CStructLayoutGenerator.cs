namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.RegularExpressions;
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

    private static readonly Regex LinePosition = new(@"line (\d+), column (\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

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

        IncrementalValueProvider<ImmutableArray<(string Path, string Text)>> layoutFiles = context.AdditionalTextsProvider
            .Where(static file => file.Path.EndsWith(".cstruct", StringComparison.OrdinalIgnoreCase))
            .Select(static (file, cancellation) => (file.Path, file.GetText(cancellation)?.ToString() ?? string.Empty))
            .Collect();

        context.RegisterSourceOutput(requests.Combine(layoutFiles), static (productionContext, pair) => Generate(productionContext, pair.Left, pair.Right));
    }

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
        bool aligned = false;
        bool littleEndian = true;
        int pointerSize = 8;
        string packing = "SysV";
        string allocation = "LowBitFirst";
        int cLongWidth = 0;
        string[]? defined = null;
        string? defaultEnumStorage = null;
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
            case "Aligned":
                aligned = named.Value.Value is true;
                break;
            case "LittleEndian":
                littleEndian = named.Value.Value is not false;
                break;
            case "PointerSize":
                pointerSize = named.Value.Value is int size ? size : 8;
                break;
            case "BitfieldPacking":
                packing = EnumMemberName(named.Value, "SysV");
                break;
            case "BitfieldAllocation":
                allocation = EnumMemberName(named.Value, "LowBitFirst");
                break;
            case "CLongWidth":
                cLongWidth = named.Value.Value is int width ? width : 0;
                break;
            case "Defined":
                defined = named.Value.Values.Select(value => value.Value as string ?? string.Empty).ToArray();
                break;
            case "DefaultEnumStorage":
                defaultEnumStorage = named.Value.Value as string;
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
            aligned,
            littleEndian,
            pointerSize,
            packing,
            allocation,
            cLongWidth,
            new EquatableArray<string>(defined),
            defaultEnumStorage,
            keepNames,
            views,
            SourceSpan.From(attributeLocation),
            definitionSpan,
            literalShape,
            ((CSharpParseOptions)declaration.SyntaxTree.Options).LanguageVersion.ToDisplayString());
    }

    private static string EnumMemberName(TypedConstant constant, string fallback)
    {
        if (constant.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType && constant.Value is not null)
        {
            foreach (IFieldSymbol member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.HasConstantValue && Equals(member.ConstantValue, constant.Value))
                {
                    return member.Name;
                }
            }
        }

        return fallback;
    }

    private static string TypeKeyword(INamedTypeSymbol type)
    {
        return type.TypeKind switch
        {
            TypeKind.Struct => type.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            _ => type.IsRecord ? "record" : "class",
        };
    }

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

        LayoutCompilation compilation;
        try
        {
            compilation = Compile(definition, request);
        }
        catch (Exception exception) when (exception is CStructException or ArgumentException)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.LayoutInvalid, LocateLayoutError(request, exception.Message), exception.Message));
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

    /// <summary>The compilation the runtime's <c>CStruct</c> constructor performs, minus its codec delegate table.</summary>
    private static LayoutCompilation Compile(string definition, LayoutRequest request)
    {
        var options = new CStructCompilationOptions
        {
            CLongWidth = request.CLongWidth == 0 ? 64 : request.CLongWidth,
            BitfieldPacking = ParseEnum(request.BitfieldPacking, BitfieldPacking.SysV),
            BitfieldAllocation = ParseEnum(request.BitfieldAllocation, BitfieldAllocation.LowBitFirst),
            Defined = request.Defined.Count == 0 ? null : DefinedSet(request.Defined),
            DefaultEnumStorage = request.DefaultEnumStorage,
        };
        Parsing.LayoutSourceValidator.ValidateLayoutSource(definition, options);
        if (request.PointerSize is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Pointer size must be 1, 2, 4, or 8 bytes.");
        }

        PrimitiveCatalog catalog = PrimitiveCatalog.For(request.LittleEndian, options.CLongWidth);
        return new LayoutCompilation(
            definition,
            (byte)request.PointerSize,
            request.Aligned,
            request.LittleEndian,
            options,
            catalog,
            ImmutableDictionary<string, CompiledTypeReference>.Empty);
    }

    private static IReadOnlySet<string> DefinedSet(EquatableArray<string> defined)
    {
        var set = new HashSet<string>(defined, StringComparer.Ordinal);
#if NETSTANDARD2_0
        return new ReadOnlySetAdapter<string>(set);
#else
        return set;
#endif
    }

    private static TEnum ParseEnum<TEnum>(string name, TEnum fallback)
        where TEnum : struct
    {
        return Enum.TryParse(name, out TEnum value) ? value : fallback;
    }

    private static bool LanguageVersionIsAtLeast12(string languageVersion)
    {
        return LanguageVersionFacts.TryParse(languageVersion, out LanguageVersion parsed) && parsed >= Microsoft.CodeAnalysis.CSharp.LanguageVersion.CSharp12;
    }

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
    ///     Places a layout error inside the definition literal when the runtime's message names a line and column
    ///     and the literal is a multi-line raw string (whose lines map one to one); otherwise at the argument.
    /// </summary>
    private static Location LocateLayoutError(LayoutRequest request, string message)
    {
        if (request.DefinitionSpan is not { } span)
        {
            return request.AttributeSpan.ToLocation();
        }

        Match match = LinePosition.Match(message);
        if (request.DefinitionLiteral.IsRawMultiLine && match.Success &&
            int.TryParse(match.Groups[1].Value, out int line) && int.TryParse(match.Groups[2].Value, out int column))
        {
            int sourceLine = request.DefinitionLiteral.ContentStartLine + line - 1;
            int sourceColumn = request.DefinitionLiteral.Indentation + Math.Max(0, column - 1);
            var position = new LinePosition(sourceLine, sourceColumn);
            return Location.Create(span.FilePath, new TextSpan(span.Start, 0), new LinePositionSpan(position, new LinePosition(sourceLine, sourceColumn + 1)));
        }

        return span.ToLocation();
    }
}
