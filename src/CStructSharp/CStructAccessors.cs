namespace CStructSharp;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Syntax;

/// <summary>
///     Prepared reads: <see cref="GetAccessor{T}(string)"/> resolves a member path once into a
///     <see cref="FieldAccessor{T}"/>, and <see cref="CreateView(ReadOnlySpan{byte}, string?, ReadOptions?)"/> wraps
///     the bytes of one struct in a <see cref="StructView"/> that reads members on demand without building values.
/// </summary>
public sealed partial class CStruct
{
    // Accessors StructView.Get<T>(string) resolved, keyed by root, path and type; bounded for callers that build
    // paths at run time. Readonly, so the layout's own fields stay frozen after construction.
    private const int MaximumCachedAccessors = 1024;

    private readonly ConcurrentDictionary<(string Root, string Path, Type Type), object> viewAccessors = new();

    /// <summary>
    ///     Resolves <paramref name="path"/> - a struct declaration followed by members and indexes, as
    ///     <c>ReadValue</c> accepts it - once, for reading the member many times.
    /// </summary>
    /// <typeparam name="T">The type the member is read as.</typeparam>
    /// <param name="path">The path, starting with a struct declaration: <c>reading.pos.x</c>, <c>reading.samples[3]</c>.</param>
    /// <returns>The accessor. A member the layout does not declare is reported when the accessor reads, as the path string would be.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is <see langword="null"/>.</exception>
    /// <exception cref="CStructPathException">The path is malformed, or does not start with a struct declaration.</exception>
    public FieldAccessor<T> GetAccessor<T>(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        IReadOnlyList<PathSegment> segments = this.ParsePath(path);
        string root = segments[0].Name;
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(root, out CStructElement? declaration))
        {
            throw this.compiledModelQueries.UnknownRoot(root);
        }

        if (segments[0].Indexes.Count > 0 || !path.StartsWith(root, StringComparison.Ordinal) ||
            !this.TryGetRootComposite(declaration, out CompiledCompositeType? rootComposite) || rootComposite.IsUnion)
        {
            throw new CStructPathException("An accessor path starts with a struct declaration: " + path);
        }

        // The part below the root, as StructValue.Get<T> on the parsed root takes it.
        string relative = path.Length == root.Length ? string.Empty : path[(root.Length + 1)..];

        var steps = new List<FieldAccessor<T>.Step>();
        bool walkable = true;
        bool fixedOffset = rootComposite.StaticPlan is not null;
        int offset = 0;
        int arrays = 0;
        CompiledCompositeType? composite = rootComposite;
        CompiledField? leaf = null;
        bool leafIsElement = false;
        for (int index = 1; index < segments.Count; index++)
        {
            PathSegment segment = segments[index];
            if (composite is null || !composite.Shape.TryGetIndex(segment.Name, out int slot))
            {
                // A member this layout does not declare, or a step below a value that is not a struct: the read
                // reports it through the path string.
                walkable = false;
                fixedOffset = false;
                break;
            }

            int[] indexes = [.. segment.Indexes];
            steps.Add(new FieldAccessor<T>.Step(composite.Shape, slot, indexes));
            if (!TryFindField(composite, segment.Name, 0, out CompiledField? field, out int fieldOffset))
            {
                fixedOffset = false;
                composite = null;
                leaf = null;
                continue;
            }

            // A constant offset needs every member on the path statically placed and unconditional; an index needs a
            // one-dimensional fixed array of fixed-size elements.
            fixedOffset &= !composite.IsUnion && fieldOffset >= 0 && field.PointerDepth == 0 && field.BitSize == 0 &&
                           !field.IsConditional;
            offset += fieldOffset;
            leafIsElement = false;
            if (indexes.Length > 0)
            {
                if (indexes.Length == 1 && field.Array.Kind == CompiledArrayKind.Fixed && field.Array.Dimensions.Length == 1 &&
                    field.Array.FixedCount is int count && indexes[0] < count && field.FixedElementSize is int stride)
                {
                    offset += indexes[0] * stride;
                    arrays = Math.Max(arrays, count);
                    leafIsElement = true;
                }
                else
                {
                    fixedOffset = false;
                }
            }

            leaf = field;
            bool single = field.Array.Kind == CompiledArrayKind.Scalar || leafIsElement;
            composite = single && field.Composite is { IsUnion: false } nested ? nested : null;
        }

        // Only a fixed-width number read as its own CLR type decodes directly; anything else (an enum, text, a whole
        // array or struct, a converted type) is read through ReadValue.
        PrimitiveCodec codec = leaf?.Codec ?? default;
        bool decodable = fixedOffset && leaf is not null && (leaf.Array.Kind == CompiledArrayKind.Scalar || leafIsElement) &&
                         leaf.Composite is null && leaf.Enum is null && !leaf.IsCharacterArray && codec.IsFixedWidthNumeric &&
                         FieldAccessor<T>.NaturalType(codec) == typeof(T);

        // Resolving a path also checks the members before it (their arrays against MaxArrayElements, for one), so a
        // direct read requires limits that the whole root satisfies, not only the members on the path.
        StaticReadPlan? plan = rootComposite.StaticPlan;
        int arrayLimit = Math.Max(arrays, plan?.MaximumArrayCount ?? 0);
        int nestingLimit = Math.Max(segments.Count, plan?.NestingDepth ?? 0);
        return new FieldAccessor<T>(this, path, root, rootComposite, relative, walkable ? [.. steps] : null, decodable ? offset : -1, codec, arrayLimit, nestingLimit);
    }

    /// <summary>
    ///     Wraps the bytes of one struct in a <see cref="StructView"/> that reads members when asked, without building a
    ///     <see cref="Values.StructValue"/>. A member read through the view is the value <c>ReadValue&lt;T&gt;</c> reads for
    ///     the same path; in a struct whose members all have build-time offsets it is decoded straight from the span.
    /// </summary>
    /// <param name="source">The bytes; the struct starts at the first one. The view reads them in place.</param>
    /// <param name="path">A struct declaration; <see langword="null"/> selects the first declared struct.</param>
    /// <param name="options">The read options every member read uses; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The view.</returns>
    /// <exception cref="CStructPathException">The path is not a struct declaration.</exception>
    /// <exception cref="CStructReadException">The struct has a fixed size and <paramref name="source"/> is shorter.</exception>
    public StructView CreateView(ReadOnlySpan<byte> source, string? path = null, ReadOptions? options = null)
    {
        string root = path ?? this.compiledModelQueries.GetFirstCompiledStructName();
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(root, out CStructElement? declaration))
        {
            throw this.compiledModelQueries.UnknownRoot(root);
        }

        if (!this.TryGetRootComposite(declaration, out CompiledCompositeType? composite) || composite.IsUnion)
        {
            throw new CStructPathException("The selected path does not resolve to a struct object.");
        }

        StaticReadPlan? plan = composite.StaticPlan;
        if (plan is not null && source.Length < plan.Size)
        {
            // The generated view's failure for a source that cannot hold the value.
            var failure = new CStructReadException(ReadFailures.ShortRead(plan.Size, source.Length));
            failure.AttachContext(root, 0);
            throw failure;
        }

        // Direct decoding needs limits that cover the whole struct; any other options read each member through ReadValue.
        bool direct = plan is not null &&
                      (options is null ||
                       (options.ExecutionPath == ExecutionPath.Fastest && ReadOperationSettings.SnapshotReadOptions(options) is { HasValidLimits: true } settings &&
                        settings.CoversPlan(plan)));
        return new StructView(this, root, composite, plan is null ? source : source[..plan.Size], source, options, direct);
    }

    /// <summary>The accessor <see cref="StructView.Get{T}(string)"/> uses for a path below <paramref name="root"/>, resolved once per path and type.</summary>
    /// <typeparam name="T">The type the member is read as.</typeparam>
    /// <param name="root">The view's root declaration.</param>
    /// <param name="path">The path below the root.</param>
    /// <returns>The accessor for <c>root.path</c>.</returns>
    internal FieldAccessor<T> GetViewAccessor<T>(string root, string path)
    {
        (string, string, Type) key = (root, path, typeof(T));
        if (this.viewAccessors.TryGetValue(key, out object? cached))
        {
            return (FieldAccessor<T>)cached;
        }

        FieldAccessor<T> accessor = this.GetAccessor<T>(path.Length == 0 ? root : root + "." + path);
        if (this.viewAccessors.Count < MaximumCachedAccessors)
        {
            this.viewAccessors.TryAdd(key, accessor);
        }

        return accessor;
    }

    /// <summary>
    ///     Finds the compiled field a member name selects in <paramref name="composite"/>, looking through anonymous
    ///     promoted members, and its offset from the composite's start (-1 when it is not fixed at build time).
    /// </summary>
    private static bool TryFindField(CompiledCompositeType composite, string name, int depth, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CompiledField? found, out int offset)
    {
        foreach (CompiledField field in composite.Fields)
        {
            if (field.Name == name)
            {
                found = field;
                offset = field.FixedOffset ?? -1;
                return true;
            }

            if (depth < 64 && composite.PromotedFields.Contains(field) && field.Composite is { } promoted &&
                TryFindField(promoted, name, depth + 1, out found, out int inner))
            {
                // A member of a promoted union starts at the union's start; the plan rules out unions for direct reads.
                offset = field.FixedOffset is int outer && inner >= 0 && !promoted.IsUnion ? outer + inner : -1;
                return true;
            }
        }

        found = null;
        offset = -1;
        return false;
    }
}
