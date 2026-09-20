namespace CStructSharp;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Numerics;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Introspection;
using CStructSharp.Parsing;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Compiles a C-like layout definition and uses it to read, inspect, write, or update binary data.
///     Create one instance per layout, then reuse it for sequential or concurrent operations. Concurrent calls must
///     use distinct streams, payload graphs, and other mutable resources, or the caller must synchronize each shared
///     resource for the complete operation.
/// </summary>
/// <remarks>
///     Layout compilation is immutable after construction, so one instance can be reused instead of reparsing the
///     layout for every record. Each operation snapshots caller variables and option values. Stream overloads start
///     at the current position; memory overloads use the start of the supplied region as coordinate zero.
/// </remarks>
public sealed partial class CStruct
{
    private readonly LayoutCompilation compilation;
    private readonly BitfieldCodecTable bitfieldCodecs;
    private readonly PrimitiveCatalog catalog;
    private readonly CodecTable codecs;
    private readonly IReadOnlyDictionary<string, CompiledTypeReference> customSymbols;
    private readonly bool highBitFirst;
    private readonly CompiledModelQueries compiledModelQueries;
    private readonly CompiledSizeQueries compiledSizeQueries;
    private readonly EnumIntegerCodecTable enumIntegerCodecs;
    private readonly ExpressionEvaluator expressionEvaluator;
    private readonly LayoutExpressionEvaluator layoutExpressionEvaluator;
    private readonly LayoutVariableResolver layoutVariableResolver;
    private readonly IReadOnlyDictionary<string, Expr> staticLayoutVariables;

    public CStruct(
        string layout,
        byte pointerSize = 8,
        bool aligned = false,
        bool isLittleEndian = true,
        CStructCompilationOptions? compilationOptions = null)
    {
        CStructCompilationOptions effectiveCompilationOptions =
            compilationOptions ?? new CStructCompilationOptions();
        if (!string.IsNullOrEmpty(effectiveCompilationOptions.Prelude))
        {
            ArgumentNullException.ThrowIfNull(layout);

            // The prelude is simply earlier source text: one parse, one namespace, one cache entry.
            layout = effectiveCompilationOptions.Prelude + "\n" + layout;
        }

        LayoutSourceValidator.ValidateLayoutSource(layout, effectiveCompilationOptions);

        // Reject pointer widths that the primitive reader and writer cannot represent.
        if (pointerSize is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(pointerSize), "Pointer size must be 1, 2, 4, or 8 bytes.");
        }

        // The catalog (names, ids, alignments, symbols) is compile-time knowledge shared with the source generator;
        // the codec table is its runtime delegate half. Both are built once per byte order and long width; only a
        // layout that registers custom codecs derives its own pair.
        IReadOnlyDictionary<string, CompiledTypeReference> customSymbols;
        if (effectiveCompilationOptions.Codecs is { Count: > 0, } customCodecs)
        {
            (_, this.codecs, customSymbols) = RegisterCustomCodecs(isLittleEndian, effectiveCompilationOptions.CLongWidth, customCodecs);
        }
        else
        {
            this.codecs = GetSharedCodecTable(isLittleEndian, effectiveCompilationOptions.CLongWidth);
            customSymbols = ImmutableDictionary<string, CompiledTypeReference>.Empty;
        }

        // Everything known before a byte is read lives in the compilation; the fields below are its members
        // cached on this instance so the operation partials read them as their own.
        this.compilation = new LayoutCompilation(layout, pointerSize, aligned, isLittleEndian, effectiveCompilationOptions, this.codecs.Catalog, customSymbols);
        this.catalog = this.compilation.Catalog;
        this.customSymbols = this.compilation.CustomSymbols;
        this.bitfieldCodecs = this.compilation.BitfieldCodecs;
        this.highBitFirst = this.compilation.HighBitFirst;
        this.compiledModelQueries = this.compilation.ModelQueries;
        this.compiledSizeQueries = this.compilation.SizeQueries;
        this.enumIntegerCodecs = this.compilation.EnumIntegerCodecs;
        this.expressionEvaluator = this.compilation.ExpressionEvaluator;
        this.layoutExpressionEvaluator = this.compilation.LayoutExpressionEvaluator;
        this.layoutVariableResolver = this.compilation.LayoutVariableResolver;
        this.staticLayoutVariables = this.compilation.StaticLayoutVariables;
    }

    /// <summary>
    ///     Gets the exported top-level declarations by their case-sensitive names. Anonymous inline declarations remain
    ///     attached to their containing fields and are not promoted into this namespace.
    /// </summary>
    /// <summary>The compile-time half of this layout: declarations, compiled model, placement, and introspection.</summary>
    internal LayoutCompilation Compilation => this.compilation;

    /// <summary>The immutable compiled layout model (the compilation's), for tests and the WASM static-plan export.</summary>
    internal CompiledLayoutModel CompiledModel => this.compilation.CompiledModel;

    /// <summary>The operation-variable resolver, exposed for tests that exercise supplied-variable resolution directly.</summary>
    internal LayoutVariableResolver CompiledLayoutVariables => this.compilation.CompiledLayoutVariables;

    internal IReadOnlyDictionary<string, CStructElement> CStructElements => this.compilation.CStructElements;

    /// <summary>Gets whether composite fields use their portable alignment boundaries.</summary>
    public bool Aligned => this.compilation.Aligned;

    /// <summary>
    ///     Gets the paths of the layout's <c>#include</c> lines in source order, exactly as written. They are recorded
    ///     for the caller's benefit only; the core never reads or resolves them.
    /// </summary>
    public IReadOnlyList<string> Includes => this.compilation.Includes;

    /// <summary>
    ///     Gets every <c>#define</c> of the layout by name: integer constants (evaluated without caller variables),
    ///     text and byte literals, bare names, and function-like macros kept as text. Only integer constants take
    ///     part in layout expressions.
    /// </summary>
    public IReadOnlyDictionary<string, LayoutConstant> Constants => this.compilation.Constants;

    /// <summary>
    ///     Gets the name of the first struct or union declared in the layout: the root that every read operation
    ///     selects when its <c>path</c> argument is <see langword="null"/>, and the one to pass to
    ///     <c>Serialize</c> or <c>Write</c> for a whole-record write.
    /// </summary>
    /// <exception cref="CStructLayoutException">The layout declares no struct or union.</exception>
    public string DefaultRoot => this.compilation.DefaultRoot;

    /// <summary>Which compiler family's rule places adjacent bitfields (<see cref="CStructCompilationOptions.BitfieldPacking"/>).</summary>
    internal BitfieldPacking BitfieldPacking => this.compilation.BitfieldPacking;

    /// <summary>Gets primitive-codec and exported-type alignments without exposing anonymous or backing-tag identities.</summary>
    internal IReadOnlyDictionary<string, byte> FieldAlignments => this.compilation.FieldAlignments;

    /// <summary>The primitive vocabulary this layout reads with: the compile-time catalog and its runtime delegates.</summary>
    internal CodecTable Codecs => this.codecs;

    /// <summary>Gets whether neutral numeric, pointer, and UTF-16 values use little-endian byte order.</summary>
    public bool IsLittleEndian => this.compilation.IsLittleEndian;

    /// <summary>Gets the configured pointer storage width: 1, 2, 4, or 8 bytes.</summary>
    public byte PointerSize => this.compilation.PointerSize;

    private string Source => this.compilation.Source;

    /// <summary>
    ///     Returns a compiled layout for the given source and options, reusing a process-wide bounded cache of the
    ///     most recently used layouts. Use this instead of the constructor when the same definition text is compiled
    ///     repeatedly (for example once per message); callers that already keep a <see cref="CStruct"/> instance
    ///     gain nothing. The returned instance is shared and immutable, exactly like any other <see cref="CStruct"/>.
    /// </summary>
    /// <param name="layout">The Portable v1 layout source to compile.</param>
    /// <param name="pointerSize">The binary format's pointer width in bytes; supported values are 1, 2, 4, and 8.</param>
    /// <param name="aligned"><see langword="true"/> to apply the portable composite-alignment rules; otherwise, <see langword="false"/>.</param>
    /// <param name="isLittleEndian"><see langword="true"/> for little-endian neutral values; <see langword="false"/> for big-endian neutral values.</param>
    /// <param name="compilationOptions">Optional resource limits for parsing and compiling the layout; every limit is part of the cache key.</param>
    /// <returns>A compiled layout equal to <c>new CStruct(layout, pointerSize, aligned, isLittleEndian, compilationOptions)</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pointerSize"/> is unsupported, or a compilation limit is not positive.</exception>
    /// <exception cref="CStructLayoutException">The layout is empty, exceeds a configured limit, or is not valid Portable v1 syntax.</exception>
    public static CStruct GetOrCompile(
        string layout,
        byte pointerSize = 8,
        bool aligned = false,
        bool isLittleEndian = true,
        CStructCompilationOptions? compilationOptions = null)
    {
        return CStructLayoutCache.Shared.GetOrCompile(layout, pointerSize, aligned, isLittleEndian, compilationOptions);
    }

    /// <summary>Gets a declared struct or union by name, unwrapping a typedef alias.</summary>
    /// <param name="name">The declaration name.</param>
    /// <returns>The struct declaration.</returns>
    /// <exception cref="CStructPathException">No struct or union has that name.</exception>
    internal Struct GetStruct(string name)
    {
        return this.compilation.GetStruct(name);
    }

    /// <summary>Gets the compiled enum behind a declaration, for the browser bridge's projection.</summary>
    /// <param name="declaration">The enum declaration.</param>
    /// <returns>The compiled enum type.</returns>
    internal CompiledEnumType GetCompiledEnumForInterop(CstructEnum declaration)
    {
        return this.compilation.GetCompiledEnumForInterop(declaration);
    }

    /// <summary>Gets the alignment in bytes of a declared struct or union.</summary>
    /// <param name="name">The declaration name.</param>
    /// <returns>The alignment in bytes.</returns>
    public int GetStructAlignmentInBytes(string name)
    {
        return this.compilation.GetStructAlignmentInBytes(name);
    }

    /// <summary>Gets the fixed size in bytes of a declared struct or union.</summary>
    /// <param name="name">The declaration name.</param>
    /// <returns>The size in bytes.</returns>
    /// <exception cref="CStructLayoutException">The struct has a runtime-sized member.</exception>
    public int GetStructSizeInBytes(string name)
    {
        return this.compilation.GetStructSizeInBytes(name);
    }

    /// <summary>Removes every layout retained by <see cref="GetOrCompile"/>; instances already handed out stay valid.</summary>
    public static void ClearCompiledCache()
    {
        CStructLayoutCache.Shared.Clear();
    }

    /// <summary>
    ///     Derives the catalog and delegate table of a layout that registers custom codecs: the shared catalog gains
    ///     one descriptor per codec (validating names, alignment, and size), and the table gains one adapter pair.
    /// </summary>
    private static (PrimitiveCatalog Catalog, CodecTable Codecs, IReadOnlyDictionary<string, CompiledTypeReference> Symbols)
        RegisterCustomCodecs(bool littleEndian, int cLongWidth, IReadOnlyList<ICustomCodec> codecs)
    {
        var descriptors = new CustomCodecDescriptor[codecs.Count];
        var instances = ImmutableArray.CreateBuilder<ICustomCodec>(codecs.Count);
        for (int index = 0; index < codecs.Count; index++)
        {
            ICustomCodec codec = codecs[index];
            ArgumentNullException.ThrowIfNull(codec, nameof(codecs));
            descriptors[index] = new CustomCodecDescriptor(codec.Name, codec.FixedSize, codec.Alignment);
            instances.Add(codec);
        }

        PrimitiveCatalog catalog = PrimitiveCatalog.For(littleEndian, cLongWidth).WithCustomCodecs(descriptors);
        var symbols = new Dictionary<string, CompiledTypeReference>(StringComparer.Ordinal);
        foreach (CustomCodecDescriptor descriptor in catalog.CustomCodecs)
        {
            symbols.Add(descriptor.Name, catalog.Symbols[descriptor.Name]);
        }

        return (catalog, BuildCodecTable(catalog, instances.ToImmutable()), symbols);
    }

    /// <summary>
    ///     Makes a scalar's exact value authoritative for later Int32 layout expressions without retaining a stale
    ///     caller or definition value when the parsed/written scalar lies outside that expression domain.
    /// </summary>
    private void UpdateExactLayoutVariable(
        Dictionary<string, Expr> variables,
        string name,
        BigInteger value)
    {
        if (value >= int.MinValue && value <= int.MaxValue)
        {
            variables[name] = new Literal((int)value);
        }
        else
        {
            variables.Remove(name);
        }
    }

    /// <summary>
    ///     Reads the requested array/string count with variables from a read-only caller view. The variables are
    ///     snapshotted before traversal and are never modified.
    /// </summary>
    internal int GetDynamicArrayLengthCore(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ReadOperationSettings effectiveOptions = ReadOperationSettings.SnapshotReadOptions(options);
        IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);
        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        var state = new CStructOperationContext(
            stream,
            effectiveVariables,
            this.Aligned,
            effectiveOptions);
        long originalPosition = state.Stream.Position;

        try
        {
            ResolvedTarget target = this.ResolveTargetFromLayout(
                state,
                segments);
            CompiledField compiledField = target.EffectiveCompiledField ??
                                          throw new CStructPathException(
                                              "Path does not resolve to an array or string field: " + elementNameOrPath);

            if (compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or
                CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
            {
                return target.ArrayLength ??
                       throw new CStructPathException("Resolved array target has no compiled length.");
            }

            if (compiledField.Array.Kind == CompiledArrayKind.Flexible && compiledField.IsCharacterArray)
            {
                state.Stream.Position = target.Address;
                Func<Stream, object> reader = (target.EffectiveCompiledField is { } stringField ? this.codecs.TerminatedReaderOf(stringField) : null) ??
                                              throw new InvalidOperationException(
                                                  "Resolved string target has no compiled reader.");
                return ((string)reader(state.Stream)).Length;
            }

            if (compiledField.Array.Kind == CompiledArrayKind.Scalar && compiledField.Codec.IsTerminatedText)
            {
                state.Stream.Position = target.Address;
                Func<Stream, object> reader = this.codecs.ReaderOf(compiledField) ??
                                              throw new InvalidOperationException(
                                                  "Resolved named string target has no compiled reader.");
                return ((string)reader(state.Stream)).Length;
            }

            throw new CStructPathException("Path does not resolve to an array or string: " + elementNameOrPath);
        }
        catch (CStructException exception)
        {
            state.Complete();
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
        finally
        {
            state.Stream.Position = originalPosition;
            state.Complete();
        }
    }

    /// <summary>
    ///     Reads a selected object using a read-only variable view. The operation snapshots the supplied entries before
    ///     resolving layout definitions or reading the stream.
    /// </summary>
    internal object ParseStreamCore(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options)
    {
        return this.ParseStreamCoreImpl(stream, elementNameOrPath, variables, options, false).Result;
    }

    /// <summary>
    ///     Reads a selected object with debug ranges while snapshotting a read-only variable view before traversal.
    /// </summary>
    internal (List<DebugData> DebugData, object Result) ParseStreamWithDebugCore(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options)
    {
        return this.ParseStreamCoreImpl(stream, elementNameOrPath, variables, options, true);
    }

    /// <summary>
    ///     Reads the composite a path selects, optionally recording debug byte ranges. Both modes return the
    ///     selected value itself; a debug caller gets the root's own records in the list.
    /// </summary>
    private (List<DebugData> DebugData, object Result) ParseStreamCoreImpl(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options,
        bool debug)
    {
        ReadOperationSettings effectiveOptions = ReadOperationSettings.SnapshotReadOptions(options);
        IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);
        if (segments.Count == 1)
        {
            (StructValue root, _) = this.ParseStreamInternal(
                stream,
                elementNameOrPath,
                variables,
                effectiveOptions,
                debug,
                out List<DebugData> rootDebugData);
            var rootValues = (IDictionary<string, object?>)root;
            if (!rootValues.TryGetValue(segments[0].Name, out object? selected) || selected is null)
            {
                // `typedef struct _X { ... } X;` parsed as `X` is stored under its tag, `_X`.
                if (!this.compiledModelQueries.TryGetCompiledDeclaration(segments[0].Name, out CStructElement? rootDeclaration) ||
                    rootDeclaration is not Typedef { Struct: { } tagged, } ||
                    !rootValues.TryGetValue(tagged.Name.Name, out selected) || selected is null)
                {
                    throw new CStructPathException("The selected path does not resolve to a composite object.");
                }
            }

            return (rootDebugData, selected);
        }

        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        var state = new CStructOperationContext(
            stream,
            effectiveVariables,
            this.Aligned,
            effectiveOptions);

        try
        {
            ResolvedTarget resolvedTarget = this.ResolveTargetFromLayout(
                state,
                segments);
            CompiledCompositeType target = ResolveStructTarget(resolvedTarget);
            (object result, List<DebugData> debugData) = this.ParseCompiledStructAt(
                state,
                resolvedTarget.Address,
                target,
                debug ? DebugPath.FromNames(resolvedTarget.DebugPrefix) : null,
                resolvedTarget.ContainingStructureDepth,
                resolvedTarget.PointerAccessorsConsumed,
                debug);
            return (debugData, result);
        }
        catch (CStructException exception)
        {
            state.Complete();
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
        finally
        {
            state.Complete();
        }
    }

    /// <summary>
    ///     Resolves a path with variables supplied through a read-only view. The caller's entries are snapshotted and
    ///     never mutated.
    /// </summary>
    internal long ResolveAddressCore(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ReadOperationSettings effectiveOptions = ReadOperationSettings.SnapshotReadOptions(options);
        IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);
        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        var state = new CStructOperationContext(
            stream,
            effectiveVariables,
            this.Aligned,
            effectiveOptions);
        long originalPosition = state.Stream.Position;

        try
        {
            return this.ResolveTargetFromLayout(state, segments).Address;
        }
        finally
        {
            state.Stream.Position = originalPosition;
            state.Complete();
        }
    }
}
