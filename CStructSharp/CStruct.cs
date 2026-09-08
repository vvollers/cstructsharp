namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using CStructSharp.Structure;
using Pidgin;
using CstructEnum = CStructSharp.Structure.Enum;

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
    private readonly BitfieldCodecTable bitfieldCodecs;

    private readonly ConstructionDictionary<string, CStructElement> cStructElements =
        new(StringComparer.Ordinal);

    private readonly ConstructionDictionary<string, byte> fieldAlignments = new(StringComparer.Ordinal);
    private readonly ConstructionDictionary<string, Func<Stream, object>> fieldHandlers =
        new(StringComparer.Ordinal);

    private readonly CompiledModelQueries compiledModelQueries;
    private readonly EnumIntegerCodecTable enumIntegerCodecs;
    private readonly ExpressionEvaluator expressionEvaluator;
    private readonly LayoutExpressionEvaluator layoutExpressionEvaluator;
    private readonly LayoutVariableResolver layoutVariableResolver;
    private readonly IReadOnlyDictionary<string, Expr> staticLayoutVariables;
    private readonly ConstructionDictionary<string, Action<Stream, object>> writeHandlers =
        new(StringComparer.Ordinal);

    /// <summary>
    ///     Creates a reusable layout from C-like source text.
    ///     Choose the pointer width, alignment rule, and byte order used by numeric values, pointers, and neutral
    ///     UTF-16 character data in the binary format being handled.
    /// </summary>
    /// <param name="layout">The Portable v1 layout source to compile.</param>
    /// <param name="pointerSize">The binary format's pointer width in bytes; supported values are 1, 2, 4, and 8.</param>
    /// <param name="aligned"><see langword="true"/> to apply the portable composite-alignment rules; otherwise, <see langword="false"/>.</param>
    /// <param name="isLittleEndian"><see langword="true"/> for little-endian neutral values; <see langword="false"/> for big-endian neutral values.</param>
    /// <param name="compilationOptions">Optional resource limits for parsing and compiling the layout; <see langword="null"/> uses the documented defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pointerSize"/> is unsupported, or a compilation limit is not positive.</exception>
    /// <exception cref="CStructLayoutException">The layout is empty, exceeds a configured limit, or is not valid Portable v1 syntax.</exception>
    public CStruct(
        string layout,
        byte pointerSize = 8,
        bool aligned = false,
        bool isLittleEndian = true,
        CStructCompilationOptions? compilationOptions = null)
    {
        CStructCompilationOptions effectiveCompilationOptions =
            compilationOptions ?? new CStructCompilationOptions();
        LayoutSourceValidator.ValidateLayoutSource(layout, effectiveCompilationOptions);
        this.expressionEvaluator = new ExpressionEvaluator(
            ExpressionEvaluationLimits.FromOptions(effectiveCompilationOptions));
        this.layoutExpressionEvaluator = new LayoutExpressionEvaluator(this.expressionEvaluator);

        // Reject pointer widths that the primitive reader and writer cannot represent.
        if (pointerSize is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(pointerSize), "Pointer size must be 1, 2, 4, or 8 bytes.");
        }

        // Store immutable layout choices before building lookup tables from them.
        this.Source = layout;
        this.Aligned = aligned;
        this.PointerSize = pointerSize;
        this.IsLittleEndian = isLittleEndian;

        // Primitive readers and writers are built once because their byte order is part of the layout contract.
        this.BuildFieldHandlers();
        this.BuildWriteHandlers();
        this.bitfieldCodecs = new BitfieldCodecTable(
            this.IsLittleEndian,
            this.fieldAlignments,
            this.fieldHandlers,
            this.writeHandlers,
            PrimitiveCodecs.FieldTypeAliasses);

        // Parse the layout text and index only exported top-level names. Anonymous inline declarations stay attached
        // to their containing field and receive declaration identity in the compiled model.
        IReadOnlyList<CStructElement> structResult;
        try
        {
            structResult = CStructDefinitionParser.Parser.ParseOrThrow(this.Source).ToArray();
        }
        catch (Exception exception) when (exception is ParseException or FormatException or OverflowException or
                                          InvalidOperationException or ArgumentException)
        {
            // Parser combinators and their semantic projections can fail through different implementation exceptions.
            // Normalize all expected invalid-input failures at the public compilation boundary.
            string detail = exception is ParseException parseException
                                ? parseException.Message
                                : exception.Message;
            throw new CStructLayoutException("Layout definition contains invalid syntax: " + detail, exception);
        }

        foreach (CStructElement declaration in structResult)
        {
            SymbolValidation.ValidateBuiltInNameCollision(declaration, this.fieldHandlers);
            if (this.cStructElements.TryGetValue(declaration.Name.Name, out CStructElement? existing))
            {
                throw new CStructLayoutException(
                    $"Duplicate global declaration name '{declaration.Name.Name}': " +
                    $"{SymbolValidation.GetDeclarationKind(existing)} and {SymbolValidation.GetDeclarationKind(declaration)}.");
            }

            this.cStructElements.Add(declaration.Name.Name, declaration);
        }

        // Resolve layout-wide constants once. Operation-specific variables later reuse this resolver's static cache
        // and invalidate only definitions downstream of a caller override.
        Defines[] definitions = this.CStructElements.Values.OfType<Defines>().ToArray();
        this.layoutVariableResolver = new LayoutVariableResolver(
            definitions,
            this.expressionEvaluator,
            this.FindExactEnumDefinitionDependencies(structResult, definitions));
        this.staticLayoutVariables = this.layoutVariableResolver.CreateStatic();

        // Validate enum storage before either expression evaluation or alignment can narrow/lookup the backing type.
        this.enumIntegerCodecs = new EnumIntegerCodecTable(structResult, this.cStructElements);

        // Compile every retained expression with this layout's immutable limits. Bit widths and enum values are static;
        // array expressions keep their compiled program because caller variables may change their result per operation.
        structResult = structResult.Select(this.NormalizeDeclarationExpressions).ToArray();
        this.cStructElements.ReplaceWith(
            structResult.Select(
                declaration => new KeyValuePair<string, CStructElement>(
                    declaration.Name.Name,
                    declaration)));

        // A compiled layout cannot safely expose duplicate field or enum-member names: readers, writers, and paths
        // would otherwise select different declarations from the same lexical scope.
        SymbolValidation.ValidateScopedMemberNames(structResult);

        foreach (KeyValuePair<string, CStructElement> el in this.CStructElements)
        {
            if (el.Value is CstructEnum en)
            {
                // An enum occupies exactly the same bytes as its declared primitive storage type.
                this.fieldAlignments[el.Key] = (byte)this.enumIntegerCodecs.Get(en.Name.Name).SizeInBytes;
            }
        }

        // Convert parsed declarations into one validated immutable model. Its recursive binder owns type resolution,
        // alignment, sizing, placement, and operation descriptors, so no parallel layout cache can drift from it.
        this.compiledLayout = this.CompileIntermediateRepresentation();
        this.compiledModelQueries = new CompiledModelQueries(this.compiledLayout);
        foreach (KeyValuePair<string, CStructElement> declaration in this.CStructElements)
        {
            if (this.compiledLayout.Symbols.TryGetValue(
                    declaration.Key,
                    out CompiledTypeReference compiledType))
            {
                this.fieldAlignments[declaration.Key] =
                    compiledType.PointerDepth > 0 ? this.PointerSize : (byte)compiledType.Symbol.Alignment;
            }
        }

        // Publish immutable snapshots only after every constructor-time validator and compiler has finished.
        this.cStructElements.Freeze();
        this.fieldAlignments.Freeze();
        this.fieldHandlers.Freeze();
        this.writeHandlers.Freeze();
    }

    /// <summary>
    ///     Gets the exported top-level declarations by their case-sensitive names. Anonymous inline declarations remain
    ///     attached to their containing fields and are not promoted into this namespace.
    /// </summary>
    internal IReadOnlyDictionary<string, CStructElement> CStructElements =>
        this.cStructElements.IsFrozen ? this.cStructElements.Snapshot : this.cStructElements;

    /// <summary>Gets whether composite fields use their portable alignment boundaries.</summary>
    public bool Aligned { get; }

    /// <summary>Gets primitive-codec and exported-type alignments without exposing anonymous or backing-tag identities.</summary>
    internal IReadOnlyDictionary<string, byte> FieldAlignments =>
        this.fieldAlignments.IsFrozen ? this.fieldAlignments.Snapshot : this.fieldAlignments;

    internal IReadOnlyDictionary<string, Func<Stream, object>> FieldHandlers =>
        this.fieldHandlers.IsFrozen ? this.fieldHandlers.Snapshot : this.fieldHandlers;

    /// <summary>Gets whether neutral numeric, pointer, and UTF-16 values use little-endian byte order.</summary>
    public bool IsLittleEndian { get; }

    /// <summary>Gets the configured pointer storage width: 1, 2, 4, or 8 bytes.</summary>
    public byte PointerSize { get; }

    internal IReadOnlyDictionary<string, Action<Stream, object>> WriteHandlers =>
        this.writeHandlers.IsFrozen ? this.writeHandlers.Snapshot : this.writeHandlers;

    private string Source { get; }

    /// <summary>Normalizes one parsed declaration into expressions compiled by this reusable layout instance.</summary>
    private CStructElement NormalizeDeclarationExpressions(CStructElement declaration)
    {
        try
        {
            return declaration switch
            {
                Struct strct => this.NormalizeStructExpressions(strct),
                Typedef { Struct: not null, } typedef =>
                    new Typedef(typedef.Name, this.NormalizeStructExpressions(typedef.Struct)),
                CstructEnum enm => this.EvaluateEnumDeclaration(enm),
                _ => declaration,
            };
        }
        catch (CStructLayoutException)
        {
            throw;
        }
        catch (Exception exception) when (LayoutExpressionEvaluator.IsExpressionFailure(exception))
        {
            throw new CStructLayoutException(
                "Layout declaration contains an invalid expression: " + exception.Message,
                exception);
        }
    }

    /// <summary>Evaluates one enum in its exact validated signed/unsigned storage domain.</summary>
    private CstructEnum EvaluateEnumDeclaration(CstructEnum enm)
    {
        EnumIntegerCodec codec = this.enumIntegerCodecs.Get(enm.Name.Name);
        return enm.Evaluate(
            this.expressionEvaluator,
            this.staticLayoutVariables,
            codec.BitWidth,
            codec.Minimum,
            codec.Maximum);
    }

    /// <summary>Finds the complete definition closure that an enum may evaluate outside the Int32 domain.</summary>
    private HashSet<string> FindExactEnumDefinitionDependencies(
        IReadOnlyList<CStructElement> declarations,
        IReadOnlyList<Defines> definitions)
    {
        Dictionary<string, Defines> definitionsByName = definitions.ToDictionary(
            definition => definition.Name.Name,
            definition => definition,
            StringComparer.Ordinal);
        var result = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>();
        foreach (CstructEnum enm in declarations.OfType<CstructEnum>())
        {
            foreach (EnumValue member in enm.DeclaredValues)
            {
                foreach (string dependency in this.expressionEvaluator.GetDependencies(member.Value))
                {
                    pending.Enqueue(dependency);
                }
            }
        }

        while (pending.Count > 0)
        {
            string name = pending.Dequeue();
            if (!definitionsByName.TryGetValue(name, out Defines? definition) || !result.Add(name))
            {
                continue;
            }

            foreach (string dependency in this.expressionEvaluator.GetDependencies(definition.Value))
            {
                pending.Enqueue(dependency);
            }
        }

        return result;
    }

    /// <summary>Rebuilds a composite with precompiled array expressions and statically evaluated bit widths.</summary>
    private Struct NormalizeStructExpressions(Struct strct)
    {
        var fields = new List<Field>(strct.Fields.Count);
        foreach (Field field in strct.Fields)
        {
            if (field is Struct nested)
            {
                fields.Add(this.NormalizeStructExpressions(nested));
                continue;
            }

            if (!ReferenceEquals(field.ArrayCount, Field.NoArray) &&
                !ReferenceEquals(field.ArrayCount, Field.UnknownArraysize))
            {
                this.expressionEvaluator.Compile(field.ArrayCount);
                if (this.expressionEvaluator.GetDependencies(field.ArrayCount).
                    All(this.staticLayoutVariables.ContainsKey))
                {
                    int count = this.layoutExpressionEvaluator.Evaluate(
                        field.ArrayCount,
                        this.staticLayoutVariables,
                        "array length for " + field.Name.Name);
                    if (count < 0)
                    {
                        throw new CStructLayoutException(
                            "Array length cannot be negative: " + field.Name.Name);
                    }
                }
            }

            int bitSize = 0;
            if (!ReferenceEquals(field.BitSizeExpression, NoneExpr.Instance))
            {
                bitSize = this.layoutExpressionEvaluator.Evaluate(
                    field.BitSizeExpression,
                    this.staticLayoutVariables,
                    "bitfield width for " + field.Name.Name);
                if (bitSize <= 0)
                {
                    throw new CStructLayoutException(
                        "Bitfield width must be greater than zero: " + field.Name.Name);
                }
            }

            fields.Add(new Field(field.Type, field.Name, field.ArrayCount, bitSize, field.PointerDepth));
        }

        return new Struct(strct.Name, [.. fields,], strct.IsUnion);
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
    ///     Reads the requested value and returns its item count when it is an array or string.
    ///     Use this when a layout contains a length determined by data already present in the stream. Optional
    ///     variables are plain integer values and are copied before traversal.
    /// </summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to inspect.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer-coordinate settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The number of elements in the selected array or the number of characters in the selected string.</returns>
    /// <exception cref="CStructPathException">The path is invalid or does not select an array or string.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide the bytes required to resolve the value.</exception>
    public int GetDynamicArrayLength(
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.GetDynamicArrayLengthCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromIntegers(variables),
            options);
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
        IReadOnlyList<PathSegment> segments = CStructPathResolver.Parse(elementNameOrPath);
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
            Field field = target.EffectiveField ??
                          throw new CStructPathException(
                              "Path does not resolve to an array or string field: " + elementNameOrPath);
            CompiledField compiledField = target.EffectiveCompiledField ??
                                          throw new CStructPathException(
                                              "Path does not resolve to a compiled field: " + elementNameOrPath);

            if (compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime)
            {
                return target.ArrayLength ??
                       throw new CStructPathException("Resolved array target has no compiled length.");
            }

            if (compiledField.Array.Kind == CompiledArrayKind.Flexible && CharacterFieldTypes.IsCharArrayField(field))
            {
                state.Stream.Position = target.Address;
                Func<Stream, object> reader = target.EffectiveCompiledField?.TerminatedReader ??
                                              throw new InvalidOperationException(
                                                  "Resolved string target has no compiled reader.");
                return ((string)reader(state.Stream)).Length;
            }

            if (compiledField.Array.Kind == CompiledArrayKind.Scalar &&
                PrimitiveCodecs.IsVariableLengthType(compiledField.CodecName))
            {
                state.Stream.Position = target.Address;
                Func<Stream, object> reader = compiledField.Reader ??
                                              throw new InvalidOperationException(
                                                  "Resolved named string target has no compiled reader.");
                return ((string)reader(state.Stream)).Length;
            }

            throw new CStructPathException("Path does not resolve to an array or string: " + elementNameOrPath);
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
        finally
        {
            state.Stream.Position = originalPosition;
        }
    }

    /// <summary>Finds a named struct declaration in this compiled layout.</summary>
    internal Struct GetStruct(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Look up the declaration first so callers get the same clear error for an unknown name or a non-struct name.
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(name, out CStructElement? value))
        {
            throw new CStructPathException("Unknown struct declaration: " + name);
        }

        if (value is Struct str)
        {
            return str;
        }

        throw new CStructPathException("Declaration is not a struct: " + name);
    }

    /// <summary>Returns the byte boundary used when aligned mode places this struct in a stream.</summary>
    /// <param name="name">The case-sensitive name of an exported struct or union declaration.</param>
    /// <returns>The portable alignment boundary in bytes.</returns>
    /// <exception cref="CStructPathException"><paramref name="name"/> is unknown or does not identify a struct or union.</exception>
    public int GetStructAlignmentInBytes(string name)
    {
        // Alignment comes from the compiled model so nested struct fields and aliases share one rule everywhere.
        return this.GetCompiledComposite(this.GetStruct(name)).Symbol.Alignment;
    }

    /// <summary>
    ///     Calculates how many bytes a fixed-size struct occupies, including padding when aligned mode is enabled.
    ///     For a union, this is the size of its largest member.
    /// </summary>
    /// <param name="name">The case-sensitive name of an exported struct or union declaration.</param>
    /// <returns>The fixed encoded size in bytes.</returns>
    /// <exception cref="CStructPathException"><paramref name="name"/> is unknown or does not identify a struct or union.</exception>
    /// <exception cref="CStructLayoutException">The selected declaration contains a runtime-sized field and therefore has no fixed size.</exception>
    public int GetStructSizeInBytes(string name)
    {
        // A public size query has no runtime field values. Reject flexible/dynamic arrays rather than inventing a size.
        return this.GetCompiledStructSizeInBytes(
            this.GetCompiledComposite(this.GetStruct(name)),
            this.staticLayoutVariables,
            true);
    }

    /// <summary>Reads the first declared struct or union from a stream using this layout.</summary>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <returns>A dynamic struct object or a lossless <see cref="UnionValue"/>.</returns>
    /// <exception cref="CStructReadException">The stream cannot provide or decode the required bytes.</exception>
    public dynamic ParseStream(Stream stream)
    {
        return this.ParseStreamCore(
            stream,
            this.compiledModelQueries.GetFirstCompiledStructName(),
            LayoutVariableInput.FromIntegers(null),
            new ReadOptions());
    }

    /// <summary>Reads the struct or nested object selected by <paramref name="elementNameOrPath"/>.</summary>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to read.</param>
    /// <returns>A dynamic struct object, lossless <see cref="UnionValue"/>, or selected nested value.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide or decode the required bytes.</exception>
    public dynamic ParseStream(Stream stream, string elementNameOrPath)
    {
        return this.ParseStreamCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromIntegers(null),
            new ReadOptions());
    }

    /// <summary>
    ///     Reads a selected composite value. Structs return <see cref="ExpandoObject"/> values and unions return
    ///     lossless <see cref="UnionValue"/> values.
    ///     The options control pointer handling; supplied integer variables are copied before the read starts.
    /// </summary>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to read.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer-coordinate settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>A dynamic struct object, lossless <see cref="UnionValue"/>, or selected nested value.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide or decode the required bytes.</exception>
    public dynamic ParseStream(
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ParseStreamCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromIntegers(variables),
            options);
    }

    /// <summary>
    ///     Reads a selected object using a read-only variable view. The operation snapshots the supplied entries before
    ///     resolving layout definitions or reading the stream.
    /// </summary>
    internal dynamic ParseStreamCore(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options)
    {
        ReadOperationSettings effectiveOptions = ReadOperationSettings.SnapshotReadOptions(options);
        IReadOnlyList<PathSegment> segments = CStructPathResolver.Parse(elementNameOrPath);
        if (segments.Count == 1)
        {
            (ExpandoObject root, _) = this.ParseStreamInternal(
                stream,
                elementNameOrPath,
                variables,
                effectiveOptions,
                false,
                out _);
            var rootValues = (IDictionary<string, object?>)root;
            return rootValues.TryGetValue(segments[0].Name, out object? selected) && selected is not null
                       ? selected
                       : throw new CStructPathException("The selected path does not resolve to a composite object.");
        }

        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        var state = new CStructOperationContext(
            stream,
            effectiveVariables,
            this.Aligned,
            effectiveOptions);
        ResolvedTarget resolvedTarget = this.ResolveTargetFromLayout(
            state,
            segments);
        Struct target = ResolveStructTarget(resolvedTarget);

        try
        {
            return this.ParseCompiledStructAt(
                state,
                resolvedTarget.Address,
                target,
                resolvedTarget.DebugPrefix.ToArray(),
                resolvedTarget.ContainingStructureDepth,
                resolvedTarget.PointerAccessorsConsumed,
                false).Result;
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
    }

    /// <summary>Reads the first declared struct or union and also returns the byte ranges used for each value.</summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <returns>The captured byte-range records and the parsed dynamic result.</returns>
    /// <exception cref="CStructReadException">The stream is not seekable or cannot provide or decode the required bytes.</exception>
    public (List<DebugData> DebugData, dynamic Result) ParseStreamWithDebug(Stream stream)
    {
        return this.ParseStreamWithDebugCore(
            stream,
            this.compiledModelQueries.GetFirstCompiledStructName(),
            LayoutVariableInput.FromIntegers(null),
            new ReadOptions());
    }

    /// <summary>Reads a selected object and returns its values together with debug byte ranges.</summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to read.</param>
    /// <returns>The captured byte-range records and the parsed dynamic result.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream is not seekable or cannot provide or decode the required bytes.</exception>
    public (List<DebugData> DebugData, dynamic Result) ParseStreamWithDebug(
        Stream stream,
        string elementNameOrPath)
    {
        return this.ParseStreamWithDebugCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromIntegers(null),
            new ReadOptions());
    }

    /// <summary>Reads a selected object with debug data and the requested pointer settings.</summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to read.</param>
    /// <param name="options">Read limits and pointer-coordinate settings.</param>
    /// <returns>The captured byte-range records and the parsed dynamic result.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream is not seekable or cannot provide or decode the required bytes.</exception>
    public (List<DebugData> DebugData, dynamic Result) ParseStreamWithDebug(
        Stream stream,
        string elementNameOrPath,
        ReadOptions options)
    {
        return this.ParseStreamWithDebugCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromIntegers(null),
            options);
    }

    /// <summary>
    ///     Reads a selected object and records where every read value came from in the stream.
    ///     Debug reads require a seekable stream because the reader revisits bytes to capture them. Optional variables
    ///     are plain integer values and are copied before traversal.
    /// </summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to read.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer-coordinate settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The captured byte-range records and the parsed dynamic result.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream is not seekable or cannot provide or decode the required bytes.</exception>
    public (List<DebugData> DebugData, dynamic Result) ParseStreamWithDebug(
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options = null)
    {
        return this.ParseStreamWithDebugCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromIntegers(variables),
            options);
    }

    /// <summary>
    ///     Reads a selected object with debug ranges while snapshotting a read-only variable view before traversal.
    /// </summary>
    internal (List<DebugData> DebugData, dynamic Result) ParseStreamWithDebugCore(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options)
    {
        ReadOperationSettings effectiveOptions = ReadOperationSettings.SnapshotReadOptions(options);
        IReadOnlyList<PathSegment> segments = CStructPathResolver.Parse(elementNameOrPath);
        if (segments.Count == 1)
        {
            (ExpandoObject root, _) = this.ParseStreamInternal(
                stream,
                elementNameOrPath,
                variables,
                effectiveOptions,
                true,
                out List<DebugData> rootDebugData);
            var rootValues = (IDictionary<string, object?>)root;
            return rootValues.TryGetValue(segments[0].Name, out object? selected) && selected is not null
                       ? (rootDebugData, selected is UnionValue ? selected : root)
                       : throw new CStructPathException("The selected path does not resolve to a composite object.");
        }

        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        var state = new CStructOperationContext(
            stream,
            effectiveVariables,
            this.Aligned,
            effectiveOptions);
        ResolvedTarget resolvedTarget = this.ResolveTargetFromLayout(
            state,
            segments);
        Struct target = ResolveStructTarget(resolvedTarget);

        try
        {
            (object result, List<DebugData> debugData) = this.ParseCompiledStructAt(
                state,
                resolvedTarget.Address,
                target,
                resolvedTarget.DebugPrefix.ToArray(),
                resolvedTarget.ContainingStructureDepth,
                resolvedTarget.PointerAccessorsConsumed,
                true);
            return (debugData, result);
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
    }

    /// <summary>
    ///     Finds the stream position for a layout path without changing the caller-facing data model.
    ///     A path ending in <c>.value</c> on a pointer resolves to the pointer target; <c>.address</c> resolves to the pointer field.
    ///     Optional variables are plain integer values and are copied before traversal.
    /// </summary>
    /// <param name="stream">The readable, seekable stream whose current position is the operation origin.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to locate.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional traversal limits and pointer-coordinate settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The absolute stream position of the selected field or pointer target.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide the bytes required for traversal.</exception>
    public long ResolveAddress(
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ResolveAddressCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromIntegers(variables),
            options);
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
        IReadOnlyList<PathSegment> segments = CStructPathResolver.Parse(elementNameOrPath);
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
        }
    }
}
