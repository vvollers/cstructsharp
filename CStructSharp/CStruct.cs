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
    private static readonly Identifier CharType = new("char");
    private static readonly Identifier CstringType = new("cstring");

    private static readonly Identifier StringType = new("string");
    private static readonly Identifier WcharBigEndianType = new("wchar>");
    private static readonly Identifier WcharLittleEndianType = new("wchar<");
    private static readonly Identifier WcharType = new("wchar");

    private readonly ConstructionDictionary<string, CStructElement> cStructElements =
        new(StringComparer.Ordinal);

    private readonly ConstructionDictionary<string, byte> fieldAlignments = new(StringComparer.Ordinal);
    private readonly ConstructionDictionary<string, Func<Stream, object>> fieldHandlers =
        new(StringComparer.Ordinal);

    private readonly ExpressionEvaluator expressionEvaluator;
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
        ValidateLayoutSource(layout, effectiveCompilationOptions);
        this.expressionEvaluator = new ExpressionEvaluator(
            ExpressionEvaluationLimits.FromOptions(effectiveCompilationOptions));

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
        this.BuildIntegralBitfieldStorageCodecs();

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
            this.ValidateBuiltInNameCollision(declaration);
            if (this.cStructElements.TryGetValue(declaration.Name.Name, out CStructElement? existing))
            {
                throw new CStructLayoutException(
                    $"Duplicate global declaration name '{declaration.Name.Name}': " +
                    $"{GetDeclarationKind(existing)} and {GetDeclarationKind(declaration)}.");
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
        this.CompileEnumStorageDescriptors(structResult);

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
        this.ValidateScopedMemberNames(structResult);

        foreach (KeyValuePair<string, CStructElement> el in this.CStructElements)
        {
            if (el.Value is CstructEnum en)
            {
                // An enum occupies exactly the same bytes as its declared primitive storage type.
                this.fieldAlignments[el.Key] = (byte)this.GetEnumIntegerCodec(en.Name.Name).SizeInBytes;
            }
        }

        // Convert parsed declarations into one validated immutable model. Its recursive binder owns type resolution,
        // alignment, sizing, placement, and operation descriptors, so no parallel layout cache can drift from it.
        this.compiledLayout = this.CompileIntermediateRepresentation();
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
        this.integralBitfieldStorageCodecs.Freeze();
        this.enumIntegerCodecs.Freeze();
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

    /// <summary>Rejects empty, oversized, or pathologically nested layout text before the parser allocates its syntax tree.</summary>
    private static void ValidateLayoutSource(string layout, CStructCompilationOptions options)
    {
        ArgumentNullException.ThrowIfNull(layout);
        if (string.IsNullOrWhiteSpace(layout))
        {
            throw new CStructLayoutException("Layout definition cannot be empty.");
        }

        if (options.MaxDefinitionLength <= 0 ||
            options.MaxLayoutNestingDepth <= 0 ||
            options.MaxExpressionNestingDepth <= 0 ||
            options.MaxExpressionTokens <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Layout compilation limits must be greater than zero.");
        }

        if (layout.Length > options.MaxDefinitionLength)
        {
            throw new CStructLayoutException("Layout definition exceeds the configured length limit.");
        }

        int depth = 0;
        int expressionDepth = 0;
        bool lineComment = false;
        bool blockComment = false;
        for (int index = 0; index < layout.Length; index++)
        {
            char character = layout[index];
            char next = index + 1 < layout.Length ? layout[index + 1] : '\0';
            if (lineComment)
            {
                if (character is '\r' or '\n')
                {
                    lineComment = false;
                }

                continue;
            }

            if (blockComment)
            {
                if (character == '*' && next == '/')
                {
                    blockComment = false;
                    index++;
                }

                continue;
            }

            if (character == '/' && next == '/')
            {
                lineComment = true;
                index++;
                continue;
            }

            if (character == '/' && next == '*')
            {
                blockComment = true;
                index++;
                continue;
            }

            if (character == '{')
            {
                depth++;
                if (depth > options.MaxLayoutNestingDepth)
                {
                    throw new CStructLayoutException("Layout definition exceeds the configured nesting-depth limit.");
                }
            }
            else if (character == '}' && depth > 0)
            {
                depth--;
            }
            else if (character == '(')
            {
                expressionDepth++;
                if (expressionDepth > options.MaxExpressionNestingDepth)
                {
                    throw new CStructLayoutException(
                        "Layout definition exceeds the configured expression-nesting limit.");
                }
            }
            else if (character == ')' && expressionDepth > 0)
            {
                expressionDepth--;
            }
        }
    }

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
        catch (Exception exception) when (this.IsExpressionFailure(exception))
        {
            throw new CStructLayoutException(
                "Layout declaration contains an invalid expression: " + exception.Message,
                exception);
        }
    }

    /// <summary>Evaluates one enum in its exact validated signed/unsigned storage domain.</summary>
    private CstructEnum EvaluateEnumDeclaration(CstructEnum enm)
    {
        EnumIntegerCodec codec = this.GetEnumIntegerCodec(enm.Name.Name);
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
                    int count = this.EvaluateLayoutExpression(
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
                bitSize = this.EvaluateLayoutExpression(
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

    /// <summary>Evaluates one core layout expression and normalizes deterministic failures to the layout domain.</summary>
    private int EvaluateLayoutExpression(
        Expr expression,
        IReadOnlyDictionary<string, Expr> variables,
        string context)
    {
        try
        {
            return this.expressionEvaluator.Evaluate(expression, variables);
        }
        catch (CStructLayoutException)
        {
            throw;
        }
        catch (Exception exception) when (this.IsExpressionFailure(exception))
        {
            throw new CStructLayoutException(
                $"Cannot evaluate {context}: {exception.Message}",
                exception);
        }
    }

    /// <summary>Recognizes supported expression-domain failures without hiding unrelated programming defects.</summary>
    private bool IsExpressionFailure(Exception exception)
    {
        return exception is InvalidOperationException or ArithmeticException or
               KeyNotFoundException or NotSupportedException;
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
        ReadOperationSettings effectiveOptions = SnapshotReadOptions(options);
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

            if (compiledField.Array.Kind == CompiledArrayKind.Flexible && IsCharArrayField(field))
            {
                state.Stream.Position = target.Address;
                Func<Stream, object> reader = target.EffectiveCompiledField?.TerminatedReader ??
                                              throw new InvalidOperationException(
                                                  "Resolved string target has no compiled reader.");
                return ((string)reader(state.Stream)).Length;
            }

            if (compiledField.Array.Kind == CompiledArrayKind.Scalar &&
                IsVariableLengthType(compiledField.CodecName))
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
            AttachExceptionContext(exception, segments, stream);
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
        if (!this.TryGetCompiledDeclaration(name, out CStructElement? value))
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
            this.GetFirstCompiledStructName(),
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
        ReadOperationSettings effectiveOptions = SnapshotReadOptions(options);
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
            AttachExceptionContext(exception, segments, stream);
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
            this.GetFirstCompiledStructName(),
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
        ReadOperationSettings effectiveOptions = SnapshotReadOptions(options);
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
            AttachExceptionContext(exception, segments, stream);
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
        ReadOperationSettings effectiveOptions = SnapshotReadOptions(options);
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

    /// <summary>Chooses the zero-terminated string reader that matches a character pointer type.</summary>
    private static string GetStringPointerHandlerKey(Identifier type)
    {
        if (IsVariableLengthType(type.Name))
        {
            return type.Name;
        }

        if (type.Equals(WcharBigEndianType))
        {
            return "string>";
        }

        if (type.Equals(WcharLittleEndianType))
        {
            return "string<";
        }

        return type.Equals(WcharType) ? StringType.Name : CstringType.Name;
    }

    /// <summary>Returns whether a non-pointer field is a fixed array of narrow or wide characters.</summary>
    private static bool IsCharArrayField(Field field)
    {
        return !field.IsPointer && (field.Type.Equals(CharType) || IsWideCharacterType(field.Type));
    }

    /// <summary>Returns whether a pointer target should be read as a terminated string.</summary>
    private static bool IsStringPointerType(Identifier type)
    {
        return type.Equals(CharType) || IsWideCharacterType(type) || IsVariableLengthType(type.Name);
    }

    /// <summary>Returns whether a type is a neutral or explicit-endian 16-bit character.</summary>
    private static bool IsWideCharacterType(Identifier type)
    {
        return type.Equals(WcharType) || type.Equals(WcharBigEndianType) || type.Equals(WcharLittleEndianType);
    }

    /// <summary>Accepts either a root object or an object that contains the root under its layout name.</summary>
    private static object NormalizeRootData(object data, string rootName, PocoBindingMode bindingMode)
    {
        if (data is null)
        {
            // A root scalar pointer may legitimately be null. Its compiled field decides whether null is valid.
            return data!;
        }

        return TryGetMemberValue(data, rootName, bindingMode, out object value) ? value : data;
    }

#pragma warning disable SA1204 // Low-level static helpers remain adjacent to the parsing code that uses them.
    /// <summary>Reads exactly a fixed number of bytes and puts them in the requested byte order.</summary>
    private static Span<byte> ReadIntoBuffer(Stream stream, int len, bool isLittleEndian)
    {
        // Allocate exactly the primitive width requested by the caller.
        byte[] buffer = new byte[len];
        try
        {
            // ReadExactly handles throttled and network-like streams that return fewer bytes per Read call.
            stream.ReadExactly(buffer);
        }
        catch (EndOfStreamException exception)
        {
            throw new CStructReadException("Not enough bytes in stream.", exception);
        }

        // BitConverter follows the current machine. Reverse only when the binary format differs.
        if (isLittleEndian != BitConverter.IsLittleEndian)
        {
            Array.Reverse(buffer);
        }

        return buffer.AsSpan();
    }

    /// <summary>Reads one required byte and turns an unexpected end of stream into a layout-specific error.</summary>
    private static byte ReadByteExactly(Stream stream)
    {
        int value = stream.ReadByte();
        if (value < 0)
        {
            throw new CStructReadException("Not enough bytes in stream.");
        }

        return (byte)value;
    }

    /// <summary>Converts one, two, four, or eight bytes into an unsigned number in the layout's byte order.</summary>
    private static ulong ReadUnsigned(byte[] buffer, bool littleEndian)
    {
        // Do not reorder the caller's buffer: bitfield and pointer code may still need the original byte sequence.
        byte[] local = (byte[])buffer.Clone();
        if (littleEndian != BitConverter.IsLittleEndian)
        {
            Array.Reverse(local);
        }

        // BitConverter only needs to handle the four widths supported by CStruct pointer and primitive codecs.
        return local.Length switch
        {
            1 => local[0],
            2 => BitConverter.ToUInt16(local, 0),
            4 => BitConverter.ToUInt32(local, 0),
            8 => BitConverter.ToUInt64(local, 0),
            _ => throw new InvalidOperationException("Unsupported integer size: " + local.Length),
        };
    }

    /// <summary>Follows a public path through caller-provided write data, including array indexes.</summary>
    private static object ResolveDataPath(object data, IReadOnlyList<PathSegment> segments, PocoBindingMode bindingMode)
    {
        // Walk the object one segment at a time so member lookup and array indexing share the same public path rules.
        object value = data;
        foreach (PathSegment segment in segments)
        {
            // A segment may first select a named child and then one item within that child.
            value = GetMemberValueOrThrow(value, segment.Name, bindingMode);
            if (segment.Index.HasValue)
            {
                value = GetIndexedValue(value, segment.Index.Value);
            }
        }

        return value;
    }

    /// <summary>Reads a named value from an expando, dictionary, public property, or public field.</summary>
    private static bool TryGetMemberValue(object data, string name, PocoBindingMode bindingMode, out object value)
    {
        if (data is null)
        {
            value = null!;
            return false;
        }

        if (data is ExpandoObject expando)
        {
            // Parsed and browser JSON data use ExpandoObject, where names are direct dictionary keys.
            var dict = (IDictionary<string, object?>)expando;
            bool found = dict.TryGetValue(name, out object? memberValue);
            value = memberValue!;
            return found;
        }

        if (data is IDictionary<string, object> dictObj)
        {
            // Plain dictionaries are supported for callers that do not use dynamic objects.
            bool found = dictObj.TryGetValue(name, out object? memberValue);
            value = memberValue!;
            return found;
        }

        // Match the dynamic API first, then let ordinary objects participate through simple public members.
        Type type = data.GetType();
        PropertyInfo? property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance) ??
                                 type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (property != null && property.CanRead)
        {
            // PublicReadWrite intentionally excludes read-only properties for callers that want a stricter POCO contract.
            if (bindingMode == PocoBindingMode.PublicReadWrite && !property.CanWrite)
            {
                value = null!;
                return false;
            }

            // Presence and value are separate facts. The compiled field writer decides whether null is valid.
            value = property.GetValue(data)!;
            return true;
        }

        FieldInfo? field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance) ??
                           type.GetField(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
        if (field != null)
        {
            // Public fields are the final POCO fallback when no matching property exists.
            value = field.GetValue(data)!;
            return true;
        }

        value = null!;
        return false;
    }

    /// <summary>Stores a just-written scalar value so later array lengths and expressions can use its name.</summary>
    private static void UpdateVariablesFromValue(CStructElementWriterState state, string name, object value)
    {
        try
        {
            if (value is Pointer pointer)
            {
                // Expressions referring to a pointer use its address, not the complex Pointer wrapper.
                state.Variables[name] = new Literal(Convert.ToInt32(pointer.Address));
                return;
            }

            if (value is string str)
            {
                // Existing parser behavior treats a string as an identifier for later expression use.
                state.Variables[name] = new Identifier(str);
                return;
            }

            // Normal scalar values become literal expressions for following array counts and calculations.
            state.Variables[name] = new Literal(Convert.ToInt32(value));
        }
        catch
        {
            // The field still shadows a caller/definition value even when it cannot feed the Int32 expression language.
            state.Variables.Remove(name);
        }
    }

    /// <summary>Writes bytes in the layout's requested byte order.</summary>
    private static void WriteEndianBytes(Stream stream, byte[] bytes, bool littleEndian)
    {
        if (littleEndian != BitConverter.IsLittleEndian)
        {
            Array.Reverse(bytes);
        }

        stream.Write(bytes, 0, bytes.Length);
    }

    /// <summary>Converts an unsigned value to one, two, four, or eight bytes in the layout's byte order.</summary>
    private static byte[] WriteUnsigned(ulong value, int byteSize, bool littleEndian)
    {
        // Start with the platform conversion for the requested width.
        byte[] bytes = byteSize switch
        {
            1 => new[] { (byte)value, },
            2 => BitConverter.GetBytes((ushort)value),
            4 => BitConverter.GetBytes((uint)value),
            8 => BitConverter.GetBytes(value),
            _ => throw new InvalidOperationException("Unsupported integer size: " + byteSize),
        };

        if (littleEndian != BitConverter.IsLittleEndian)
        {
            // Reverse only when the layout's byte order differs from the machine's order.
            Array.Reverse(bytes);
        }

        return bytes;
    }

#pragma warning restore SA1204
}
