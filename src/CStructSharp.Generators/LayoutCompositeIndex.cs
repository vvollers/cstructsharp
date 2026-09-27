namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Threading;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     The composites of a compilation's inline <c>[CStructLayout]</c> layouts, by name, for the mapped classes whose
///     <c>Layout</c> names one. Each layout is compiled once, on the first lookup, for every mapped class; the first
///     layout that declares a name wins. A layout read from a file, or one that does not compile (its own class
///     reports that), contributes nothing.
/// </summary>
internal sealed class LayoutCompositeIndex
{
    private readonly Lazy<Dictionary<string, CompiledCompositeType>> composites;

    /// <summary>Indexes the given layouts; nothing is compiled until the first lookup.</summary>
    /// <param name="layouts">The compilation's layout requests, in declaration order.</param>
    public LayoutCompositeIndex(ImmutableArray<LayoutRequest> layouts)
    {
        this.composites = new Lazy<Dictionary<string, CompiledCompositeType>>(() => Build(layouts), LazyThreadSafetyMode.ExecutionAndPublication);
    }

    /// <summary>The composite a layout declares under <paramref name="layoutName"/>.</summary>
    /// <param name="layoutName">The struct or union name.</param>
    /// <returns>The compiled composite, or <see langword="null"/> when no inline layout declares it.</returns>
    public CompiledCompositeType? Find(string layoutName)
        => this.composites.Value.TryGetValue(layoutName, out CompiledCompositeType? composite) ? composite : null;

    /// <summary>Compiles every inline layout once and collects its composites by name.</summary>
    /// <param name="layouts">The layout requests.</param>
    /// <returns>The composites; the first layout that declares a name wins.</returns>
    private static Dictionary<string, CompiledCompositeType> Build(ImmutableArray<LayoutRequest> layouts)
    {
        var composites = new Dictionary<string, CompiledCompositeType>(StringComparer.Ordinal);
        foreach (LayoutRequest request in layouts)
        {
            if (request.Definition is null || !request.Settings.TryParseCodecs(out List<CustomCodecDescriptor> codecs, out _))
            {
                continue;
            }

            LayoutCompilation compilation;
            try
            {
                compilation = request.Settings.Compile(request.Definition, request.Settings.CodecCatalog(codecs));
            }
            catch (Exception exception) when (exception is CStructException or ArgumentException)
            {
                // The layout's own class reports the failure; the mapper falls back to run-time name matching.
                continue;
            }

            foreach (KeyValuePair<Syntax.Struct, CompiledTypeSymbol> entry in compilation.CompiledModel.Composites)
            {
                if (entry.Value.Definition is CompiledCompositeType composite && !composites.ContainsKey(composite.Name))
                {
                    composites.Add(composite.Name, composite);
                }
            }
        }

        return composites;
    }
}
