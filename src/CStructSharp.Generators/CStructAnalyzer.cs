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
///     A layout is resolved only when it is plainly visible - <c>new CStruct("literal")</c> or
///     <c>CStruct.GetOrCompile("literal")</c> assigned to the local, field, or property the call uses, or a
///     <c>[CStructLayout]</c> class's <c>Layout</c> - and anything else stays silent: never a false positive.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class CStructAnalyzer : DiagnosticAnalyzer
{
    private static readonly ImmutableHashSet<string> PathMethods = ImmutableHashSet.Create(
        StringComparer.Ordinal,
        "Parse",
        "ParseWithDebug",
        "ReadValue",
        "ReadValueWithDebug",
        "ResolveAddress",
        "GetArrayLength",
        "Serialize",
        "Write",
        "Update",
        "GetStructSizeInBytes");

    private readonly ConcurrentDictionary<string, LayoutCompilation?> layouts = new(StringComparer.Ordinal);

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

    private void AnalyzeInvocation(OperationAnalysisContext context)
    {
        var invocation = (IInvocationOperation)context.Operation;
        IMethodSymbol method = invocation.TargetMethod;
        if (method.ContainingType?.ToDisplayString() != "CStructSharp.CStruct" || !PathMethods.Contains(method.Name))
        {
            return;
        }

        IArgumentOperation? pathArgument = invocation.Arguments.FirstOrDefault(argument => argument.Parameter?.Name == "path" || argument.Parameter?.Name == "elementNameOrPath" || (argument.Parameter?.Type.SpecialType == SpecialType.System_String && argument.Parameter.Name.EndsWith("Path", StringComparison.Ordinal)));
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
        if (method.Name is "Parse" or "ParseWithDebug" && segments.Count == 1 && (composite is null || composite.IsUnion))
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
    private LayoutCompilation? ResolveLayout(IOperation receiver)
    {
        string? text = null;
        string[]? defined = null;
        switch (receiver)
        {
        case IObjectCreationOperation creation:
            text = LayoutLiteral(creation.Arguments);
            break;
        case IInvocationOperation factory when factory.TargetMethod.Name == "GetOrCompile" && factory.TargetMethod.ContainingType?.ToDisplayString() == "CStructSharp.CStruct":
            text = LayoutLiteral(factory.Arguments);
            break;
        case ILocalReferenceOperation local:
            text = InitializerLiteral(local.Local, receiver.SemanticModel);
            break;
        case IFieldReferenceOperation field:
            text = InitializerLiteral(field.Field, receiver.SemanticModel);
            break;
        case IPropertyReferenceOperation { Property.Name: "Layout" } property when property.Property.ContainingType is { } owner:
            (text, defined) = AttributeLayout(owner);
            break;
        default:
            return null;
        }

        if (text is null)
        {
            return null;
        }

        string key = defined is null ? text : text + "\0" + string.Join(",", defined);
        return this.layouts.GetOrAdd(key, _ => Compile(text, defined));
    }

    private static LayoutCompilation? Compile(string text, string[]? defined)
    {
        try
        {
            var options = new CStructCompilationOptions
            {
                Defined = defined is null ? null : CStructLayoutGenerator.DefinedSet(new EquatableArray<string>(defined)),
            };
            Parsing.LayoutSourceValidator.ValidateLayoutSource(text, options);
            PrimitiveCatalog catalog = PrimitiveCatalog.For(true, 64);
            return new LayoutCompilation(text, 8, false, true, options, catalog, ImmutableDictionary<string, CompiledTypeReference>.Empty);
        }
        catch (Exception exception) when (exception is CStructException or ArgumentException)
        {
            return null;
        }
    }

    private static string? LayoutLiteral(ImmutableArray<IArgumentOperation> arguments)
    {
        IArgumentOperation? first = arguments.FirstOrDefault(argument => argument.Parameter?.Ordinal == 0);
        return first?.Value.ConstantValue is { HasValue: true, Value: string literal } ? literal : null;
    }

    /// <summary>
    ///     The layout literal of a local's or field's initializer, read from its syntax: <c>new CStruct("...")</c> or
    ///     <c>CStruct.GetOrCompile("...")</c> with a string literal (or a constant the receiver's own tree resolves)
    ///     as the first argument.
    /// </summary>
    private static string? InitializerLiteral(ISymbol symbol, SemanticModel? model)
    {
        foreach (SyntaxReference reference in symbol.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is not VariableDeclaratorSyntax { Initializer.Value: { } initializer })
            {
                continue;
            }

            ExpressionSyntax? first = initializer switch
            {
                ObjectCreationExpressionSyntax creation when creation.Type.ToString() is "CStruct" or "CStructSharp.CStruct" or "global::CStructSharp.CStruct" => creation.ArgumentList?.Arguments.FirstOrDefault()?.Expression,
                ImplicitObjectCreationExpressionSyntax implicitCreation => implicitCreation.ArgumentList.Arguments.FirstOrDefault()?.Expression,
                InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax { Name.Identifier.Text: "GetOrCompile" } } factory => factory.ArgumentList.Arguments.FirstOrDefault()?.Expression,
                _ => null,
            };
            if (first is null)
            {
                return null;
            }

            if (first is LiteralExpressionSyntax literal && literal.IsKind(SyntaxKind.StringLiteralExpression))
            {
                return literal.Token.ValueText;
            }

            if (model is not null && ReferenceEquals(model.SyntaxTree, reference.SyntaxTree))
            {
                Optional<object?> constant = model.GetConstantValue(first);
                return constant.HasValue ? constant.Value as string : null;
            }

            return null;
        }

        return null;
    }

    /// <summary>The layout text of a [CStructLayout] class, when it is inline (a file is not read here).</summary>
    private static (string? Text, string[]? Defined) AttributeLayout(INamedTypeSymbol owner)
    {
        foreach (AttributeData attribute in owner.GetAttributes())
        {
            if (attribute.AttributeClass?.ToDisplayString() != "CStructSharp.CStructLayoutAttribute")
            {
                continue;
            }

            string? text = attribute.ConstructorArguments.Length == 1 && attribute.ConstructorArguments[0].Value is string literal ? literal : null;
            string[]? defined = null;
            foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
            {
                if (named.Key == "Defined" && !named.Value.IsNull)
                {
                    defined = named.Value.Values.Select(value => value.Value as string ?? string.Empty).ToArray();
                }
            }

            return (text, defined);
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
