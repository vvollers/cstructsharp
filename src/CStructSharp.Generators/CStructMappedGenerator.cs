namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

/// <summary>
///     The <c>[CStructMapped]</c> generator: a <c>partial</c> class or struct with settable properties becomes an
///     <c>ICStructMapped&lt;TSelf&gt;</c> whose <c>ReadFrom</c>/<c>WriteTo</c> assign the members by name with the
///     conversions <c>Get&lt;T&gt;</c> applies, and a module initializer registers it. Members match their layout
///     member by exact name, then case-insensitively, then ignoring underscores (<c>bit_depth</c> for
///     <c>BitDepth</c>), unless <c>[CStructMember("name")]</c> names it or the attribute's <c>Layout</c> resolves
///     against a <c>[CStructLayout]</c> of the same compilation, in which case the exact names are emitted.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class CStructMappedGenerator : IIncrementalGenerator
{
    private const string AttributeMetadataName = "CStructSharp.CStructMappedAttribute";
    private const string MemberAttributeMetadataName = "CStructSharp.CStructMemberAttribute";
    private const string MappedInterfaceMetadataName = "CStructSharp.ICStructMapped`1";

    /// <inheritdoc />
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        IncrementalValuesProvider<MappedRequest> requests = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                AttributeMetadataName,
                static (node, _) => node is TypeDeclarationSyntax,
                static (syntaxContext, cancellation) => CreateRequest(syntaxContext, cancellation))
            .Where(static request => request is not null)
            .Select(static (request, _) => request!);

        // The [CStructLayout] classes of the compilation, for a mapped class whose Layout names one of their composites.
        IncrementalValueProvider<ImmutableArray<LayoutRequest>> layouts = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "CStructSharp.CStructLayoutAttribute",
                static (node, _) => node is ClassDeclarationSyntax,
                static (syntaxContext, cancellation) => CStructLayoutGenerator.CreateRequestForMapping(syntaxContext, cancellation))
            .Where(static request => request is not null)
            .Select(static (request, _) => request!)
            .Collect();

        context.RegisterSourceOutput(requests.Combine(layouts), static (productionContext, pair) => Generate(productionContext, pair.Left, pair.Right));
    }

    private static MappedRequest? CreateRequest(GeneratorAttributeSyntaxContext context, CancellationToken cancellation)
    {
        if (context.TargetSymbol is not INamedTypeSymbol symbol || context.TargetNode is not TypeDeclarationSyntax declaration)
        {
            return null;
        }

        AttributeData attribute = context.Attributes[0];
        string? layout = null;
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            if (named.Key == "Layout")
            {
                layout = named.Value.Value as string;
            }
        }

        var containers = new List<ContainingType>();
        bool containersArePartial = true;
        for (INamedTypeSymbol? container = symbol.ContainingType; container is not null; container = container.ContainingType)
        {
            containers.Insert(0, new ContainingType(TypeKeyword(container), container.Name));
            containersArePartial &= container.DeclaringSyntaxReferences.Any(reference => reference.GetSyntax(cancellation) is TypeDeclarationSyntax type && type.Modifiers.Any(SyntaxKind.PartialKeyword));
        }

        INamedTypeSymbol? memberAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName(MemberAttributeMetadataName);
        INamedTypeSymbol? mappedInterface = context.SemanticModel.Compilation.GetTypeByMetadataName(MappedInterfaceMetadataName);
        INamedTypeSymbol? mappedAttribute = context.SemanticModel.Compilation.GetTypeByMetadataName(AttributeMetadataName);
        var members = new List<MappedMember>();
        foreach (IPropertySymbol property in symbol.GetMembers().OfType<IPropertySymbol>())
        {
            if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || property.GetMethod is null || property.SetMethod is null)
            {
                continue;
            }

            string? layoutName = null;
            foreach (AttributeData memberData in property.GetAttributes())
            {
                if (memberAttribute is not null && SymbolEqualityComparer.Default.Equals(memberData.AttributeClass, memberAttribute) && memberData.ConstructorArguments.Length == 1)
                {
                    layoutName = memberData.ConstructorArguments[0].Value as string;
                }
            }

            Location location = property.Locations.FirstOrDefault() ?? declaration.Identifier.GetLocation();
            members.Add(Describe(property, layoutName, mappedInterface, mappedAttribute, SourceSpan.From(location)));
        }

        bool hasParameterlessConstructor = symbol.IsValueType || symbol.InstanceConstructors.Any(constructor => constructor.Parameters.Length == 0 && constructor.DeclaredAccessibility >= Accessibility.Internal);
        return new MappedRequest(
            symbol.Name,
            symbol.ContainingNamespace.IsGlobalNamespace ? null : symbol.ContainingNamespace.ToDisplayString(),
            new EquatableArray<ContainingType>(containers.ToArray()),
            SyntaxFacts.GetText(symbol.DeclaredAccessibility),
            TypeKeyword(symbol),
            declaration.Modifiers.Any(SyntaxKind.PartialKeyword),
            containersArePartial,
            hasParameterlessConstructor,
            layout,
            new EquatableArray<MappedMember>(members.ToArray()),
            SourceSpan.From((attribute.ApplicationSyntaxReference?.GetSyntax(cancellation) as AttributeSyntax)?.GetLocation() ?? declaration.Identifier.GetLocation()));
    }

    private static MappedMember Describe(IPropertySymbol property, string? layoutName, INamedTypeSymbol? mappedInterface, INamedTypeSymbol? mappedAttribute, SourceSpan span)
    {
        ITypeSymbol type = property.Type;
        bool nullableValue = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable && nullable.TypeArguments.Length == 1;
        ITypeSymbol effective = nullableValue ? ((INamedTypeSymbol)type).TypeArguments[0] : type;
        string display = effective.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
        bool initOnly = property.SetMethod!.IsInitOnly;

        if (effective is INamedTypeSymbol { IsGenericType: true } generic && generic.TypeArguments.Length == 1)
        {
            string definition = generic.ConstructedFrom.ToDisplayString();
            ITypeSymbol element = generic.TypeArguments[0];
            string elementDisplay = element.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
            if (definition == "CStructSharp.Generated.Pointer<T>")
            {
                return new MappedMember(property.Name, layoutName, display, MappedMemberKind.TypedPointer, elementDisplay, nullableValue, initOnly, string.Empty, span);
            }

            if (definition is "System.Collections.Generic.List<T>" or "System.Collections.Generic.IList<T>" or "System.Collections.Generic.ICollection<T>")
            {
                return new MappedMember(property.Name, layoutName, display, Unsupported(element, mappedInterface, mappedAttribute) is { } inner ? MappedMemberKind.Unsupported : MappedMemberKind.List, elementDisplay, nullableValue, initOnly, Unsupported(element, mappedInterface, mappedAttribute) ?? string.Empty, span);
            }

            if (definition is "System.Collections.Generic.IReadOnlyList<T>" or "System.Collections.Generic.IReadOnlyCollection<T>" or "System.Collections.Generic.IEnumerable<T>")
            {
                return new MappedMember(property.Name, layoutName, display, Unsupported(element, mappedInterface, mappedAttribute) is { } inner ? MappedMemberKind.Unsupported : MappedMemberKind.ReadOnlyCollection, elementDisplay, nullableValue, initOnly, Unsupported(element, mappedInterface, mappedAttribute) ?? string.Empty, span);
            }
        }

        string? unsupported = Unsupported(effective, mappedInterface, mappedAttribute);
        return new MappedMember(property.Name, layoutName, display, unsupported is null ? MappedMemberKind.Converted : MappedMemberKind.Unsupported, string.Empty, nullableValue, initOnly, unsupported ?? string.Empty, span);
    }

    /// <summary>The name of a class type the mapper cannot convert (neither a reader value nor a mapped class), or <see langword="null"/> when the type converts.</summary>
    private static string? Unsupported(ITypeSymbol type, INamedTypeSymbol? mappedInterface, INamedTypeSymbol? mappedAttribute)
    {
        if (type is IArrayTypeSymbol array)
        {
            return Unsupported(array.ElementType, mappedInterface, mappedAttribute);
        }

        if (type.TypeKind is TypeKind.Enum or TypeKind.Struct || type.SpecialType is SpecialType.System_String or SpecialType.System_Object)
        {
            return null;
        }

        if (type.TypeKind == TypeKind.Class)
        {
            string display = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString();
            if (display is "CStructSharp.Values.StructValue" or "CStructSharp.Values.UnionValue" or "CStructSharp.Values.Pointer")
            {
                return null;
            }

            bool mapped = (mappedInterface is not null && type.AllInterfaces.Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.OriginalDefinition, mappedInterface)))
                          || (mappedAttribute is not null && type.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, mappedAttribute)));
            return mapped ? null : display;
        }

        return null;
    }

    private static string TypeKeyword(INamedTypeSymbol type)
    {
        return type switch
        {
            { IsRecord: true, IsValueType: true } => "record struct",
            { IsRecord: true } => "record",
            { IsValueType: true } => "struct",
            _ => "class",
        };
    }

    private static void Generate(SourceProductionContext context, MappedRequest request, ImmutableArray<LayoutRequest> layouts)
    {
        if (!request.IsPartial || !request.ContainersArePartial)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.MappedNotPartial, request.AttributeSpan.ToLocation(), request.ClassName, "must be declared 'partial' (and every containing type 'partial')"));
            return;
        }

        if (!request.HasParameterlessConstructor)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.MappedNotPartial, request.AttributeSpan.ToLocation(), request.ClassName, "needs a parameterless constructor the generated ReadFrom can call"));
            return;
        }

        bool failed = false;
        foreach (MappedMember member in request.Members)
        {
            if (member.Kind == MappedMemberKind.Unsupported)
            {
                context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.MappedMemberType, member.Span.ToLocation(), member.PropertyName, member.UnsupportedTypeName));
                failed = true;
            }
        }

        if (failed)
        {
            return;
        }

        // The layout's member names, when the attribute names a composite of a [CStructLayout] in this compilation.
        IReadOnlyList<string>? layoutMembers = null;
        if (request.Layout is not null)
        {
            layoutMembers = CStructLayoutGenerator.ResolveMembers(layouts, request.Layout);
            if (layoutMembers is not null)
            {
                foreach (MappedMember member in request.Members)
                {
                    if (ResolveLayoutName(member, layoutMembers) is null)
                    {
                        context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.MappedMemberMissing, member.Span.ToLocation(), member.PropertyName, request.Layout));
                    }
                }
            }
        }

        context.AddSource(request.HintName, SourceText.From(MappedEmitter.Emit(request, layoutMembers), System.Text.Encoding.UTF8));
    }

    /// <summary>The layout member a property maps to, by the mapper's rule, or <see langword="null"/> when the layout has none.</summary>
    internal static string? ResolveLayoutName(MappedMember member, IReadOnlyList<string> layoutMembers)
    {
        if (member.LayoutName is not null)
        {
            return layoutMembers.Contains(member.LayoutName) ? member.LayoutName : null;
        }

        string? exact = layoutMembers.FirstOrDefault(name => name == member.PropertyName);
        if (exact is not null)
        {
            return exact;
        }

        var insensitive = layoutMembers.Where(name => string.Equals(name, member.PropertyName, StringComparison.OrdinalIgnoreCase)).ToList();
        if (insensitive.Count == 1)
        {
            return insensitive[0];
        }

        var collapsed = layoutMembers.Where(name => name.IndexOf('_') >= 0 && string.Equals(name.Replace("_", string.Empty), member.PropertyName, StringComparison.OrdinalIgnoreCase)).ToList();
        return collapsed.Count == 1 ? collapsed[0] : null;
    }
}
