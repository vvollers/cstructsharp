namespace CStructSharp.Generators;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;

/// <summary>
///     The analyzer beside the generators: a string path in a <c>CStruct</c> call that cannot resolve against the
///     layout its receiver was built from (CSG200), <c>Parse</c> on a root that is a union or a scalar (CSG201), and
///     <c>dynamic</c> over a <c>StructValue</c>/<c>UnionValue</c> in a trimmed or AOT-published project (CSG300).
///     A layout is resolved only when it is plainly visible - <c>new CStruct("literal", ...)</c> or
///     <c>CStruct.GetOrCompile("literal", ...)</c> with constant arguments, assigned to the local, field, or property
///     the call uses, or a <c>[CStructLayout]</c> class's <c>Layout</c> - and it is compiled with those settings, as the
///     runtime would. Anything else stays silent: never a false positive.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CStructAnalyzer : DiagnosticAnalyzer
{
    /// <summary>The operations that return a struct and reject a union or scalar root at run time (CSG201).</summary>
    private static readonly ImmutableHashSet<string> StructOnlyMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Parse",
        "ParseAsync",
        "ParseMany",
        "ParseManyAsync",
        "ParseWithDebug",
        "ParseWithDebugAsync");

    // Keyed by the text and the settings: the same text built with another pointer size or byte order is another layout.
    private readonly ConcurrentDictionary<(string Text, LayoutSettings Settings), LayoutCompilation?> layouts = new();

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(GeneratorDiagnostics.PathUnresolved, GeneratorDiagnostics.ParseNotStruct, GeneratorDiagnostics.DynamicUnderAot);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterOperationAction(this.AnalyzeInvocation, OperationKind.Invocation);
        context.RegisterOperationAction(AnalyzeConversion, OperationKind.Conversion);
    }

    /// <summary>
    ///     CSG200 and CSG201: a constant path passed to a <c>CStruct</c> method is resolved against the receiver's layout,
    ///     when that layout is visible.
    /// </summary>
    /// <param name="context">The invocation being analyzed.</param>
    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = invocation.TargetMethod;
        if (method.IsStatic || method.ContainingType?.ToDisplayString() != "CStructSharp.CStruct")
        {
            return;
        }

        // A path method is recognized by its shape - a string parameter named "path", or the declaration "name" of the
        // size and alignment queries - so a new overload is checked without being listed here.
        IArgumentOperation? pathArgument = invocation.Arguments.FirstOrDefault(argument => argument.Parameter is { Type.SpecialType: SpecialType.System_String, Name: "path" or "name", });
        if (pathArgument is null || !pathArgument.Value.ConstantValue.HasValue || pathArgument.Value.ConstantValue.Value is not string path)
        {
            return;
        }

        LayoutCompilation? layout = invocation.Instance is { } instance ? this.ResolveLayout(instance) : null;
        if (layout is null)
        {
            return;
        }

        IReadOnlyList<PathSegment> segments;
        try
        {
            segments = CStructPathResolver.Parse(path);
        }
        catch (CStructPathException exception)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.PathUnresolved, pathArgument.Syntax.GetLocation(), path, exception.Message));
            return;
        }

        if (!layout.CStructElements.TryGetValue(segments[0].Name, out CStructElement? root))
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.PathUnresolved, pathArgument.Syntax.GetLocation(), path, "the layout has no declaration named '" + segments[0].Name + "' (declared: " + string.Join(", ", layout.CStructElements.Keys.Where(name => layout.CStructElements[name] is Struct or Typedef)) + ")"));
            return;
        }

        CompiledCompositeType? composite = FindComposite(layout, root);
        if (StructOnlyMethods.Contains(method.Name) && segments.Count == 1 && (composite is null || composite.IsUnion))
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.ParseNotStruct, pathArgument.Syntax.GetLocation(), path, composite is null ? "not a struct" : "a union"));
            return;
        }

        string? failure = WalkPath(composite, segments, path);
        if (failure is not null)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.PathUnresolved, pathArgument.Syntax.GetLocation(), path, failure));
        }
    }

    /// <summary>Follows the segments through the compiled composites by name; unknown members and too many indices are certain run-time path failures.</summary>
    private static string? WalkPath(CompiledCompositeType? composite, IReadOnlyList<PathSegment> segments, string path)
    {
        CompiledCompositeType? current = composite;
        for (int index = 1; index < segments.Count && current is not null; index++)
        {
            PathSegment segment = segments[index];
            CompiledField? field = current.Fields.FirstOrDefault(candidate => candidate.Name == segment.Name);
            if (field is null)
            {
                string members = string.Join(", ", current.Shape.Names);
                return "'" + current.Name + "' has no member '" + segment.Name + "' (members: " + members + ")";
            }

            int dimensions = field.Array.Kind == CompiledArrayKind.Scalar ? 0 : Math.Max(1, field.Array.Dimensions.Length);
            if (segment.Indexes.Count > dimensions)
            {
                return dimensions == 0 ? "'" + segment.Name + "' is not an array" : "'" + segment.Name + "' has " + dimensions + " dimension(s), not " + segment.Indexes.Count;
            }

            if (field.PointerDepth > 0)
            {
                // `.address` and `.value` follow a pointer; the target's members are not walked (its shape depends on the depth).
                if (index + 1 < segments.Count && segments[index + 1].Name is not ("address" or "value"))
                {
                    return "'" + segment.Name + "' is a pointer; select '.address' or '.value' after it";
                }

                return null;
            }

            current = field.Composite ?? (field.Type.Symbol.Definition as CompiledCompositeType);
        }

        return null;
    }

    private static CompiledCompositeType? FindComposite(LayoutCompilation layout, CStructElement root)
    {
        CStructElement element = root;
        for (int hops = 0; hops < 16 && element is Typedef typedef; hops++)
        {
            if (typedef.Struct is { } inline)
            {
                element = inline;
                break;
            }

            string target = typedef.Type.Name;
            if (!layout.CStructElements.TryGetValue(target, out CStructElement? next) || ReferenceEquals(next, element))
            {
                return null;
            }

            element = next;
        }

        return element is Struct declaration && layout.CompiledModel.Composites.TryGetValue(declaration, out CompiledTypeSymbol? symbol)
                   ? symbol.Definition as CompiledCompositeType
                   : null;
    }

    /// <summary>The compiled layout the receiver plainly refers to, or <see langword="null"/> when it is not visible.</summary>
    /// <param name="receiver">The <c>CStruct</c> instance a path method is called on.</param>
    /// <returns>The layout, compiled with the settings it was built with, or <see langword="null"/>.</returns>
    private LayoutCompilation? ResolveLayout(IOperation receiver)
    {
        (string? text, LayoutSettings? settings) = receiver switch
        {
            IObjectCreationOperation creation => CallLayout(creation.Arguments),
            IInvocationOperation factory when IsGetOrCompile(factory.TargetMethod) => CallLayout(factory.Arguments),
            ILocalReferenceOperation local => InitializerLayout(local.Local, receiver.SemanticModel),
            IFieldReferenceOperation field => InitializerLayout(field.Field, receiver.SemanticModel),
            IPropertyReferenceOperation { Property.Name: "Layout" } property when property.Property.ContainingType is { } owner => AttributeLayout(owner),
            _ => (null, null),
        };
        if (text is null || settings is null)
        {
            return null;
        }

        return this.layouts.GetOrAdd((text, settings), key => Compile(key.Text, key.Settings));
    }

    /// <summary>Compiles a layout as the runtime would, or returns <see langword="null"/> when it does not compile (the runtime reports that).</summary>
    /// <param name="text">The layout text.</param>
    /// <param name="settings">The settings it is built with.</param>
    /// <returns>The compiled layout, or <see langword="null"/>.</returns>
    private static LayoutCompilation? Compile(string text, LayoutSettings settings)
    {
        if (!settings.TryParseCodecs(out List<CustomCodecDescriptor> codecs, out _))
        {
            return null;
        }

        try
        {
            return settings.Compile(text, settings.CodecCatalog(codecs));
        }
        catch (Exception exception) when (exception is CStructException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Whether a method is <c>CStruct.GetOrCompile</c>, which takes the same layout arguments as the constructor.</summary>
    /// <param name="method">The invoked method.</param>
    /// <returns>Whether it is the cached-compilation factory.</returns>
    private static bool IsGetOrCompile(IMethodSymbol method)
        => method.Name == "GetOrCompile" && method.ContainingType?.ToDisplayString() == "CStructSharp.CStruct";

    /// <summary>
    ///     The layout of a <c>new CStruct(...)</c> or <c>CStruct.GetOrCompile(...)</c> call: the literal text and the
    ///     constant pointer size, alignment, and byte order (their defaults when omitted). A non-constant argument, or
    ///     compilation options other than <see langword="null"/>, leave the layout unknown.
    /// </summary>
    /// <param name="arguments">The call's arguments, including omitted optional ones.</param>
    /// <returns>The text and settings, or <see langword="null"/>s when either is not plainly visible.</returns>
    private static (string? Text, LayoutSettings? Settings) CallLayout(ImmutableArray<IArgumentOperation> arguments)
    {
        string? text = null;
        LayoutSettings settings = LayoutSettings.Default;
        foreach (IArgumentOperation argument in arguments)
        {
            Optional<object?> constant = argument.Value.ConstantValue;
            switch (argument.Parameter?.Name)
            {
            case "layout" when constant is { HasValue: true, Value: string literal, }:
                text = literal;
                break;
            case "pointerSize" when constant is { HasValue: true, Value: byte size, }:
                settings = settings with { PointerSize = size };
                break;
            case "aligned" when constant is { HasValue: true, Value: bool aligned, }:
                settings = settings with { Aligned = aligned };
                break;
            case "isLittleEndian" when constant is { HasValue: true, Value: bool littleEndian, }:
                settings = settings with { LittleEndian = littleEndian };
                break;
            case "compilationOptions" when constant is { HasValue: true, Value: null, }:
                break;
            default:
                return (null, null);
            }
        }

        return (text, settings);
    }

    /// <summary>
    ///     The layout of a local's or field's initializer: <c>new CStruct(...)</c> or <c>CStruct.GetOrCompile(...)</c>.
    ///     Its arguments are read through the semantic model when the declaration is in the receiver's own tree;
    ///     elsewhere only a single string-literal argument (every setting at its default) is recognized.
    /// </summary>
    /// <param name="symbol">The local or field.</param>
    /// <param name="model">The receiver's semantic model.</param>
    /// <returns>The text and settings, or <see langword="null"/>s when the initializer is not plainly visible.</returns>
    private static (string? Text, LayoutSettings? Settings) InitializerLayout(ISymbol symbol, SemanticModel? model)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not VariableDeclaratorSyntax { Initializer.Value: { } initializer })
            {
                continue;
            }

            if (model is not null && ReferenceEquals(model.SyntaxTree, reference.SyntaxTree))
            {
                return model.GetOperation(initializer) switch
                {
                    IObjectCreationOperation creation when creation.Type?.ToDisplayString() == "CStructSharp.CStruct" => CallLayout(creation.Arguments),
                    IInvocationOperation factory when IsGetOrCompile(factory.TargetMethod) => CallLayout(factory.Arguments),
                    _ => (null, null),
                };
            }

            ArgumentListSyntax? arguments = initializer switch
            {
                ObjectCreationExpressionSyntax creation when creation.Type.ToString() is "CStruct" or "CStructSharp.CStruct" or "global::CStructSharp.CStruct" => creation.ArgumentList,
                ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation.ArgumentList,
                InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "GetOrCompile" } } factory => factory.ArgumentList,
                _ => null,
            };
            return arguments is { Arguments: [{ Expression: LiteralExpressionSyntax literal }] } && literal.IsKind(SyntaxKind.StringLiteralExpression)
                       ? (literal.Token.ValueText, LayoutSettings.Default)
                       : (null, null);
        }

        return (null, null);
    }

    /// <summary>The inline layout text and settings of a <c>[CStructLayout]</c> class (a layout file is not read here).</summary>
    /// <param name="owner">The attributed class.</param>
    /// <returns>The text and settings, or <see langword="null"/>s when the class has no inline layout.</returns>
    private static (string? Text, LayoutSettings? Settings) AttributeLayout(INamedTypeSymbol owner)
    {
        foreach (AttributeData attribute in owner.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() == "CStructSharp.CStructLayoutAttribute")
            {
                string? text = attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;
                return (text, LayoutSettings.FromAttribute(attribute));
            }
        }

        return (null, null);
    }

    /// <summary>CSG300: a value the runtime binds dynamically, in a project that publishes trimmed or AOT.</summary>
    private static void AnalyzeConversion(OperationAnalysisContext context)
    {
        var conversion = (IConversionOperation)context.Operation;
        if (conversion.Type?.TypeKind != TypeKind.Dynamic || conversion.Operand.Type?.ToDisplayString() is not ("CStructSharp.Values.StructValue" or "CStructSharp.Values.UnionValue"))
        {
            return;
        }

        AnalyzerConfigOptions options = context.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        bool publishesAot = IsTrue(options, "build_property.PublishAot") || IsTrue(options, "build_property.PublishTrimmed");
        if (publishesAot)
        {
            context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.DynamicUnderAot, conversion.Syntax.GetLocation(), conversion.Operand.Type.Name));
        }
    }

    private static bool IsTrue(AnalyzerConfigOptions options, string key)
        => options.TryGetValue(key, out string? value) && string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
}
