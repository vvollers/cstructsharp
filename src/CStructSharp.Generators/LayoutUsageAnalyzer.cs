namespace CStructSharp.Generators;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
///     Finds what a project generates and never uses: a <c>[CStructLayout]</c> class that nothing references (CSG016), and one that
///     generates more than the members that are used need (CSG017: a smaller <c>Surface</c>, or <c>Views = false</c> when no view
///     is used). Both are suggestions (severity Info; raise them in an <c>.editorconfig</c>). The analysis sees one compilation, so
///     it reports only a layout class that no other assembly can see, and stays silent when a friend assembly may use it
///     (<c>InternalsVisibleTo</c>, unless <c>CStructSharpAnalyzeWithFriends</c> is <c>true</c>), when <c>[CStructMapped]</c> classes
///     may resolve layouts through it, and for a layout that is referenced as a whole (<c>typeof</c>, <c>nameof</c>,
///     <c>using static</c>), whose use it cannot attribute to members.
/// </summary>
[DiagnosticAnalyzer(LanguageNames.CSharp)]
public sealed class LayoutUsageAnalyzer : DiagnosticAnalyzer
{
    private const string LayoutAttribute = "CStructSharp.CStructLayoutAttribute";
    private const string MappedAttribute = "CStructSharp.CStructMappedAttribute";
    private const string FriendAttribute = "System.Runtime.CompilerServices.InternalsVisibleToAttribute";

    /// <inheritdoc />
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics { get; } =
        ImmutableArray.Create(GeneratorDiagnostics.LayoutUnused, GeneratorDiagnostics.LayoutOvergenerates);

    /// <inheritdoc />
    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();
        context.RegisterCompilationStartAction(StartCompilation);
    }

    /// <summary>
    ///     The surfaces that generate a member the project uses, as a mask (bit 0 <c>Views</c>, bit 1 <c>Parser</c>, bit 2 <c>Reader</c>,
    ///     bit 3 <c>Full</c>). The surfaces do not form a chain (<c>Views</c> has <c>Definition</c> and <c>Parser</c> has not), so the
    ///     smallest surface for several members is the lowest bit of the masks' intersection. A member the table does not know is
    ///     taken to need the full surface, so a suggestion is never too small.
    /// </summary>
    /// <param name="member">The top-level member of the layout class that was referenced: a method, property, constant or nested type.</param>
    /// <returns>The mask of the surfaces that generate it.</returns>
    internal static int SurfaceMask(ISymbol? member)
    {
        const int Everywhere = 0b1111;
        const int Typed = 0b1110;
        const int ReaderUp = 0b1100;
        const int FullOnly = 0b1000;
        if (member is null)
        {
            return FullOnly;
        }

        string name = member.Name;
        switch (member)
        {
        case INamedTypeSymbol type:
            if (type.TypeKind == TypeKind.Enum || name is "Sizes" or "Offsets")
            {
                return Everywhere;
            }

            if (name.EndsWith("View", StringComparison.Ordinal))
            {
                return Everywhere;
            }

            // The enumerators of a view belong to the full surface unless the layout asks for them (Enumerators = true), and the typed
            // updater is a writer; an enumerator used on a smaller surface is therefore never suggested away.
            if (name.EndsWith("ViewEnumerator", StringComparison.Ordinal) || name.EndsWith("ViewEnumerable", StringComparison.Ordinal) || name == "Update")
            {
                return FullOnly;
            }

            return name == "InlineArrays" || type.TypeKind is TypeKind.Class or TypeKind.Struct ? Typed : FullOnly;
        case IMethodSymbol method:
            if (name.StartsWith("Parse", StringComparison.Ordinal) || name.StartsWith("TryParse", StringComparison.Ordinal))
            {
                if (name.EndsWith("Async", StringComparison.Ordinal) || name is "ParseWithDebug" or "ParseMapped")
                {
                    return FullOnly;
                }

                string? first = method.Parameters.Length > 0 ? method.Parameters[0].Type.ToDisplayString() : null;
                bool tryForm = name.StartsWith("TryParse", StringComparison.Ordinal);
                if (first is "System.ReadOnlySpan<byte>")
                {
                    return tryForm ? ReaderUp : Typed;
                }

                return first is "byte[]" or "System.ReadOnlyMemory<byte>" ? ReaderUp : FullOnly;
            }

            return FullOnly;
        case IFieldSymbol or IPropertySymbol:
            return name is "Definition" or "RootName" ? 0b1101 : name is "Sizes" or "Offsets" ? Everywhere : FullOnly;
        default:
            return FullOnly;
        }
    }

    /// <summary>The surface a rank names, as the <c>GeneratedSurface</c> member.</summary>
    /// <param name="rank">The rank.</param>
    /// <returns>The member name.</returns>
    private static string SurfaceName(int rank) => rank switch { 0 => "Views", 1 => "Parser", 2 => "Reader", _ => "Full" };

    /// <summary>The rank of a generation surface value as the attribute and the project default spell it.</summary>
    /// <param name="surface">The surface.</param>
    /// <returns>The rank.</returns>
    private static int RankOf(GenerationSurface surface) => surface switch { GenerationSurface.Views => 0, GenerationSurface.Parser => 1, GenerationSurface.Reader => 2, _ => 3 };

    /// <summary>Whether a type can be seen by another assembly: it and every type around it is public or protected.</summary>
    /// <param name="type">The type.</param>
    /// <returns><see langword="true"/> when it is part of the assembly's public surface.</returns>
    private static bool IsExternallyVisible(INamedTypeSymbol type)
    {
        for (INamedTypeSymbol? current = type; current is not null; current = current.ContainingType)
        {
            if (current.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Collects every layout class declared in source from a namespace, with the nested ones.</summary>
    /// <param name="scope">The namespace or type to search.</param>
    /// <param name="attribute">The <c>[CStructLayout]</c> attribute type.</param>
    /// <param name="layouts">The classes found.</param>
    private static void Collect(INamespaceOrTypeSymbol scope, INamedTypeSymbol attribute, List<INamedTypeSymbol> layouts)
    {
        foreach (ISymbol member in scope.GetMembers())
        {
            switch (member)
            {
            case INamespaceSymbol nested:
                Collect(nested, attribute, layouts);
                break;
            case INamedTypeSymbol type:
                if (type.Locations.Any(location => location.IsInSource) && type.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute)))
                {
                    layouts.Add(type);
                }

                Collect(type, attribute, layouts);
                break;
            }
        }
    }

    /// <summary>Starts the analysis of one compilation: finds the layouts, records what the code references, and reports at the end.</summary>
    /// <param name="start">The compilation-start context.</param>
    private static void StartCompilation(CompilationStartAnalysisContext start)
    {
        Compilation compilation = start.Compilation;
        INamedTypeSymbol? attribute = compilation.GetTypeByMetadataName(LayoutAttribute);
        if (attribute is null)
        {
            return;
        }

        AnalyzerConfigOptions global = start.Options.AnalyzerConfigOptionsProvider.GlobalOptions;
        bool withFriends = global.TryGetValue("build_property.CStructSharpAnalyzeWithFriends", out string? friends) && bool.TryParse(friends, out bool analyze) && analyze;
        if (!withFriends && compilation.Assembly.GetAttributes().Any(candidate => candidate.AttributeClass?.ToDisplayString() == FriendAttribute))
        {
            return;
        }

        var layouts = new List<INamedTypeSymbol>();
        Collect(compilation.Assembly.GlobalNamespace, attribute, layouts);
        layouts.RemoveAll(IsExternallyVisible);
        INamedTypeSymbol? mapped = compilation.GetTypeByMetadataName(MappedAttribute);
        if (layouts.Count == 0 || (mapped is not null && HasMappedClass(compilation.Assembly.GlobalNamespace, mapped)))
        {
            return;
        }

        LayoutDefaults defaults = LayoutDefaults.From(global);
        var byName = new Dictionary<string, List<INamedTypeSymbol>>(StringComparer.Ordinal);
        foreach (INamedTypeSymbol layout in layouts)
        {
            if (!byName.TryGetValue(layout.Name, out List<INamedTypeSymbol>? sameName))
            {
                byName[layout.Name] = sameName = new List<INamedTypeSymbol>();
            }

            sameName.Add(layout);
        }

        var usage = new ConcurrentDictionary<INamedTypeSymbol, LayoutUse>(SymbolEqualityComparer.Default);
        start.RegisterSyntaxNodeAction(context => RecordReference(context, byName, usage), SyntaxKind.IdentifierName);
        start.RegisterCompilationEndAction(end => Report(end, layouts, usage, attribute, defaults));
    }

    /// <summary>Whether any class in the namespace carries <c>[CStructMapped]</c>.</summary>
    /// <param name="scope">The namespace or type to search.</param>
    /// <param name="mapped">The attribute type.</param>
    /// <returns>Whether a mapped class exists.</returns>
    private static bool HasMappedClass(INamespaceOrTypeSymbol scope, INamedTypeSymbol mapped)
    {
        foreach (ISymbol member in scope.GetMembers())
        {
            if (member is INamespaceSymbol nested ? HasMappedClass(nested, mapped) : member is INamedTypeSymbol type && (type.GetAttributes().Any(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, mapped)) || HasMappedClass(type, mapped)))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Records one identifier that names a layout class: which member of it the code goes on to use, or that the class is referenced as a whole.</summary>
    /// <param name="context">The identifier's syntax context.</param>
    /// <param name="byName">The layout classes by simple name.</param>
    /// <param name="usage">What the code uses of each layout.</param>
    private static void RecordReference(SyntaxNodeAnalysisContext context, Dictionary<string, List<INamedTypeSymbol>> byName, ConcurrentDictionary<INamedTypeSymbol, LayoutUse> usage)
    {
        var node = (IdentifierNameSyntax)context.Node;
        if (!byName.ContainsKey(node.Identifier.ValueText))
        {
            return;
        }

        if (context.SemanticModel.GetSymbolInfo(node, context.CancellationToken).Symbol is not INamedTypeSymbol type || !byName.TryGetValue(type.Name, out List<INamedTypeSymbol>? candidates) || !candidates.Contains(type, SymbolEqualityComparer.Default))
        {
            return;
        }

        LayoutUse use = usage.GetOrAdd(type, static _ => new LayoutUse());
        SimpleNameSyntax? member = node.Parent switch
        {
            MemberAccessExpressionSyntax access when access.Expression == node => access.Name,
            QualifiedNameSyntax qualified when qualified.Left == node => qualified.Right,
            _ => null,
        };
        if (member is null)
        {
            use.WholeType = true;
            return;
        }

        SymbolInfo info = context.SemanticModel.GetSymbolInfo(member, context.CancellationToken);
        ISymbol? symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        use.Members.Add(SurfaceMask(symbol));
        use.UsedViews |= symbol is INamedTypeSymbol { Name: var viewName } && viewName.EndsWith("View", StringComparison.Ordinal);
        use.Names.Add(member.Identifier.ValueText);
    }

    /// <summary>Reports the layouts nothing references and the layouts that generate more than the code uses.</summary>
    /// <param name="context">The compilation-end context.</param>
    /// <param name="layouts">The layout classes the assembly keeps to itself.</param>
    /// <param name="usage">What the code uses of each.</param>
    /// <param name="attribute">The <c>[CStructLayout]</c> attribute type.</param>
    /// <param name="defaults">The project defaults for the options an attribute does not name.</param>
    private static void Report(CompilationAnalysisContext context, List<INamedTypeSymbol> layouts, ConcurrentDictionary<INamedTypeSymbol, LayoutUse> usage, INamedTypeSymbol attribute, LayoutDefaults defaults)
    {
        foreach (INamedTypeSymbol layout in layouts)
        {
            Location location = layout.Locations.First(candidate => candidate.IsInSource);
            if (!usage.TryGetValue(layout, out LayoutUse? use))
            {
                context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.LayoutUnused, location, layout.Name));
                continue;
            }

            if (use.WholeType)
            {
                continue;
            }

            AttributeData data = layout.GetAttributes().First(candidate => SymbolEqualityComparer.Default.Equals(candidate.AttributeClass, attribute));
            GenerationSurface surface = defaults.Surface ?? GenerationSurface.Full;
            bool views = defaults.Views ?? true;
            foreach (KeyValuePair<string, TypedConstant> named in data.NamedArguments)
            {
                if (named.Key == "Surface" && named.Value.Value is int surfaceValue && Enum.IsDefined(typeof(GenerationSurface), surfaceValue))
                {
                    surface = (GenerationSurface)surfaceValue;
                }
                else if (named.Key == "Views")
                {
                    views = named.Value.Value is not false;
                }
            }

            int declared = RankOf(surface);
            int common = use.Members.Aggregate(0b1111, (all, mask) => all & mask);
            int needed = 0;
            while (needed < 3 && ((common >> needed) & 1) == 0)
            {
                needed++;
            }

            var advice = new List<string>();
            if (needed < declared)
            {
                advice.Add("Surface = GeneratedSurface." + SurfaceName(needed));
            }

            // A view is used by name; the views of a layout that no code names are generated for nothing.
            if (views && !use.UsedViews && surface != GenerationSurface.Views)
            {
                advice.Add("Views = false");
            }

            if (advice.Count > 0)
            {
                context.ReportDiagnostic(Diagnostic.Create(GeneratorDiagnostics.LayoutOvergenerates, location, layout.Name, string.Join(" and ", use.Names.OrderBy(name => name, StringComparer.Ordinal).Take(4)), string.Join(", ", advice)));
            }
        }
    }

    /// <summary>What the code is seen to use of one layout class.</summary>
    private sealed class LayoutUse
    {
        private volatile bool wholeType;
        private volatile bool usedViews;

        /// <summary>Gets the masks of the surfaces that generate each member used.</summary>
        public ConcurrentBag<int> Members { get; } = new();

        /// <summary>Gets the names of the members used.</summary>
        public ConcurrentBag<string> Names { get; } = new();

        /// <summary>Gets or sets whether the class is named as a whole (<c>typeof</c>, <c>nameof</c>, <c>using static</c>), so its use cannot be attributed to members.</summary>
        public bool WholeType
        {
            get => this.wholeType;
            set => this.wholeType = value;
        }

        /// <summary>Gets or sets whether a view type of the layout is named.</summary>
        public bool UsedViews
        {
            get => this.usedViews;
            set => this.usedViews = value;
        }
    }
}
