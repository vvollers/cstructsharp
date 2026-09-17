namespace CStructSharp;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;
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
    private readonly BitfieldCodecTable bitfieldCodecs;

    private readonly ConstructionDictionary<string, CStructElement> cStructElements =
        new(StringComparer.Ordinal);

    private readonly ConstructionDictionary<string, byte> fieldAlignments;
    private readonly FrozenDictionary<string, Func<Stream, object>> fieldHandlers;
    private readonly PrimitiveRegistry primitiveRegistry;

    /// <summary>Caller-supplied codecs (<see cref="CStructCompilationOptions.Codecs"/>) as type symbols; empty for every layout that has none.</summary>
    private readonly IReadOnlyDictionary<string, CompiledTypeReference> customSymbols;

    /// <summary>Whether bitfields take a storage unit from its top bit down (<see cref="CStructCompilationOptions.BitfieldAllocation"/>).</summary>
    private readonly bool highBitFirst;

    private readonly CompiledModelQueries compiledModelQueries;
    private readonly CompiledSizeQueries compiledSizeQueries;
    private readonly EnumIntegerCodecTable enumIntegerCodecs;
    private readonly ExpressionEvaluator expressionEvaluator;
    private readonly LayoutExpressionEvaluator layoutExpressionEvaluator;
    private readonly LayoutVariableResolver layoutVariableResolver;
    private readonly IReadOnlyDictionary<string, Expr> staticLayoutVariables;
    private readonly FrozenDictionary<string, Action<Stream, object>> writeHandlers;

    private readonly Lazy<IReadOnlyDictionary<string, LayoutConstant>> constants;
    private readonly Lazy<LayoutInfo> layoutInfo;

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
        this.CompilationOptions = effectiveCompilationOptions;
        this.layoutInfo = new Lazy<LayoutInfo>(this.BuildLayoutInfo);
        this.constants = new Lazy<IReadOnlyDictionary<string, LayoutConstant>>(this.BuildConstants);
        this.highBitFirst = effectiveCompilationOptions.BitfieldAllocation == BitfieldAllocation.HighBitFirst;
        if (!string.IsNullOrEmpty(effectiveCompilationOptions.Prelude))
        {
            ArgumentNullException.ThrowIfNull(layout);

            // The prelude is simply earlier source text: one parse, one namespace, one cache entry.
            layout = effectiveCompilationOptions.Prelude + "\n" + layout;
        }

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
        this.primitiveRegistry = GetPrimitiveRegistry(this.IsLittleEndian, effectiveCompilationOptions.CLongWidth);

        // The registry's alignments are the shared baseline; only this layout's declarations are added on top.
        this.fieldAlignments = new ConstructionDictionary<string, byte>(StringComparer.Ordinal, this.primitiveRegistry.Alignments);
        this.bitfieldCodecs = this.primitiveRegistry.Bitfields;
        if (effectiveCompilationOptions.Codecs is { Count: > 0, } customCodecs)
        {
            // Only a layout that registers codecs builds its own handler tables; every other layout shares the
            // process-wide registry as before.
            (this.fieldHandlers, this.writeHandlers, this.customSymbols) = this.RegisterCustomCodecs(customCodecs);
        }
        else
        {
            this.fieldHandlers = this.primitiveRegistry.Readers;
            this.writeHandlers = this.primitiveRegistry.Writers;
            this.customSymbols = FrozenDictionary<string, CompiledTypeReference>.Empty;
        }

        // Parse the layout text and index only exported top-level names. Anonymous inline declarations stay attached
        // to their containing field and receive declaration identity in the compiled model.
        // Syntax errors already carry the "invalid syntax" prefix; the remaining implementation exceptions can only
        // come from semantic projections of otherwise well-formed text and are normalized to the same public shape.
        IReadOnlyList<CStructElement> structResult;
        bool usesQualifiedIdentifiers;
        try
        {
            structResult = CStructDefinitionParser.ParseLayout(this.Source, effectiveCompilationOptions.Defined, effectiveCompilationOptions.DefaultEnumStorage, out usesQualifiedIdentifiers);
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or
                                          InvalidOperationException or ArgumentException)
        {
            throw new CStructLayoutException(LayoutParser.SyntaxErrorPrefix + exception.Message, exception);
        }

        var includes = new List<string>();
        foreach (CStructElement declaration in structResult)
        {
            if (declaration is IncludeDirective include)
            {
                // Recorded, never resolved: the core does not read translation-unit files.
                includes.Add(include.Path);
                continue;
            }

            SymbolValidation.ValidateBuiltInNameCollision(declaration, this.fieldHandlers);
            if (this.cStructElements.TryGetValue(declaration.Name.Name, out CStructElement? existing))
            {
                throw new CStructLayoutException(
                    $"Duplicate global declaration name '{declaration.Name.Name}': " +
                    $"{SymbolValidation.GetDeclarationKind(existing)} and {SymbolValidation.GetDeclarationKind(declaration)}.");
            }

            this.cStructElements.Add(declaration.Name.Name, declaration);
        }

        this.Includes = includes.Count == 0 ? Array.Empty<string>() : includes.ToArray();
        structResult = structResult.Where(declaration => declaration is not IncludeDirective).ToArray();

        // Validate enum storage before either expression evaluation or alignment can narrow/lookup the backing type.
        this.enumIntegerCodecs = new EnumIntegerCodecTable(structResult, this.cStructElements, effectiveCompilationOptions.CLongWidth, this.PointerSize);

        // Resolve layout-wide constants once. Operation-specific variables later reuse this resolver's static cache
        // and invalidate only definitions downstream of a caller override.
        Defines[] definitions = this.CStructElements.Values.OfType<Defines>().ToArray();
        if (usesQualifiedIdentifiers)
        {
            // `Enum.Member` in an expression: evaluate every named enum against the plain definitions first, then
            // publish each member as a literal constant next to them. Only layouts that spell a qualified name pay
            // for this second resolver.
            definitions = [.. definitions, .. this.CreateQualifiedMemberDefinitions(structResult, definitions),];
        }

        this.layoutVariableResolver = new LayoutVariableResolver(
            definitions,
            this.expressionEvaluator,
            this.FindExactEnumDefinitionDependencies(structResult, definitions));
        this.staticLayoutVariables = this.layoutVariableResolver.CreateStatic();

        // Compile every retained expression with this layout's immutable limits. Bit widths and enum values are static;
        // array expressions keep their compiled program because caller variables may change their result per operation.
        structResult = structResult.Select(this.NormalizeDeclarationExpressions).ToArray();
        this.cStructElements.ReplaceWith(
            structResult.Select(
                declaration => new KeyValuePair<string, CStructElement>(
                    declaration.Name.Name,
                    declaration)));

        // `typedef struct tag { ... } alias;` declares the tag as well as the alias, as in C. Registered after
        // normalization so the tag names the same (normalized) declaration instance the alias does.
        foreach (CStructElement declaration in structResult)
        {
            if (declaration is not Typedef { Struct: { } tagged, } || tagged.Name.Name == declaration.Name.Name)
            {
                continue;
            }

            if (this.cStructElements.TryGetValue(tagged.Name.Name, out CStructElement? existing))
            {
                if (ReferenceEquals(existing, tagged))
                {
                    continue;
                }

                throw new CStructLayoutException(
                    $"Duplicate global declaration name '{tagged.Name.Name}': " +
                    $"{SymbolValidation.GetDeclarationKind(existing)} and {SymbolValidation.GetDeclarationKind(tagged)}.");
            }

            SymbolValidation.ValidateBuiltInNameCollision(tagged, this.fieldHandlers);
            this.cStructElements.Add(tagged.Name.Name, tagged);
            structResult = [.. structResult, tagged,];
        }

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
        this.compiledSizeQueries = new CompiledSizeQueries(
            this.compiledLayout.Composites,
            this.Aligned,
            this.layoutExpressionEvaluator);
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
    }

    /// <summary>
    ///     Gets the exported top-level declarations by their case-sensitive names. Anonymous inline declarations remain
    ///     attached to their containing fields and are not promoted into this namespace.
    /// </summary>
    internal IReadOnlyDictionary<string, CStructElement> CStructElements => this.cStructElements;

    /// <summary>Gets whether composite fields use their portable alignment boundaries.</summary>
    public bool Aligned { get; }

    /// <summary>
    ///     Gets the paths of the layout's <c>#include</c> lines in source order, exactly as written. They are recorded
    ///     for the caller's benefit only; the core never reads or resolves them.
    /// </summary>
    public IReadOnlyList<string> Includes { get; }

    /// <summary>
    ///     Gets every <c>#define</c> of the layout by name: integer constants (evaluated without caller variables),
    ///     text and byte literals, bare names, and function-like macros kept as text. Only integer constants take
    ///     part in layout expressions.
    /// </summary>
    public IReadOnlyDictionary<string, LayoutConstant> Constants => this.constants.Value;

    /// <summary>Gets primitive-codec and exported-type alignments without exposing anonymous or backing-tag identities.</summary>
    internal IReadOnlyDictionary<string, byte> FieldAlignments => this.fieldAlignments;

    internal IReadOnlyDictionary<string, Func<Stream, object>> FieldHandlers =>
        this.fieldHandlers;

    /// <summary>Gets whether neutral numeric, pointer, and UTF-16 values use little-endian byte order.</summary>
    public bool IsLittleEndian { get; }

    /// <summary>Gets the configured pointer storage width: 1, 2, 4, or 8 bytes.</summary>
    public byte PointerSize { get; }

    internal IReadOnlyDictionary<string, Action<Stream, object>> WriteHandlers =>
        this.writeHandlers;

    private string Source { get; }

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

    /// <summary>Removes every layout retained by <see cref="GetOrCompile"/>; instances already handed out stay valid.</summary>
    public static void ClearCompiledCache()
    {
        CStructLayoutCache.Shared.Clear();
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
                Typedef { ArrayShape.Count: > 0, } typedef => this.NormalizeTypedefArray(typedef),
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

    /// <summary>Adds the caller's codecs to per-layout copies of the reader, writer, and alignment tables.</summary>
    private (FrozenDictionary<string, Func<Stream, object>> Readers, FrozenDictionary<string, Action<Stream, object>> Writers, IReadOnlyDictionary<string, CompiledTypeReference> Symbols)
        RegisterCustomCodecs(IReadOnlyList<ICustomCodec> codecs)
    {
        var readers = new Dictionary<string, Func<Stream, object>>(this.primitiveRegistry.Readers, StringComparer.Ordinal);
        var writers = new Dictionary<string, Action<Stream, object>>(this.primitiveRegistry.Writers, StringComparer.Ordinal);
        var symbols = new Dictionary<string, CompiledTypeReference>(StringComparer.Ordinal);
        foreach (ICustomCodec codec in codecs)
        {
            ArgumentNullException.ThrowIfNull(codec, nameof(codecs));
            string name = codec.Name;
            if (string.IsNullOrEmpty(name) || !(name[0] == '_' || char.IsLetter(name[0])) || name.Any(character => character != '_' && !char.IsLetterOrDigit(character)))
            {
                throw new ArgumentException($"Custom codec name '{name}' is not an identifier.", nameof(codecs));
            }

            if (this.primitiveRegistry.Symbols.ContainsKey(name) || symbols.ContainsKey(name))
            {
                throw new ArgumentException($"Custom codec name '{name}' is already a primitive type.", nameof(codecs));
            }

            if (codec.Alignment <= 0 || (codec.Alignment & (codec.Alignment - 1)) != 0 || codec.FixedSize is < 0)
            {
                throw new ArgumentException($"Custom codec '{name}' needs a power-of-two alignment and a non-negative size.", nameof(codecs));
            }

            ICustomCodec captured = codec;
            Func<Stream, object> reader = stream => captured.Read(stream);
            Action<Stream, object> writer = (stream, value) => captured.Write(stream, value);
            readers.Add(name, reader);
            writers.Add(name, writer);
            this.fieldAlignments[name] = (byte)Math.Min(codec.Alignment, byte.MaxValue);
            var symbol = new CompiledTypeSymbol(name, CompiledTypeKind.Primitive, null, codec.Alignment, codec.FixedSize, reader, writer, isCustomCodec: true);
            symbol.Bind(new CompiledPrimitiveType(symbol));
            symbol.Freeze();
            symbols.Add(name, new CompiledTypeReference(symbol, 0, name));
        }

        return (readers.ToFrozenDictionary(StringComparer.Ordinal), writers.ToFrozenDictionary(StringComparer.Ordinal), symbols);
    }

    private IReadOnlyDictionary<string, LayoutConstant> BuildConstants()
    {
        var result = new Dictionary<string, LayoutConstant>(StringComparer.Ordinal);
        foreach (CStructElement declaration in this.CStructElements.Values)
        {
            switch (declaration)
            {
            case ConstantDefinition constant:
                result.Add(constant.Name.Name, new LayoutConstant(constant.Name.Name, constant.Kind, constant.Value));
                break;
            case Defines define:
                bool isStatic = this.staticLayoutVariables.TryGetValue(define.Name.Name, out Expr? value) && value is Literal;
                LayoutConstant published = isStatic
                                               ? new LayoutConstant(define.Name.Name, LayoutConstantKind.Integer, ((Literal)value!).ExactValue)
                                               : new LayoutConstant(define.Name.Name, LayoutConstantKind.Expression, null);
                result.Add(define.Name.Name, published);
                break;
            }
        }

        return result;
    }

    /// <summary>Compiles the fixed dimensions of a <c>typedef T name[N];</c> alias; every count must be a static, non-negative integer.</summary>
    private Typedef NormalizeTypedefArray(Typedef typedef)
    {
        return new Typedef(typedef.Name, typedef.Type) { ArrayShape = this.EvaluateTypedefShape(typedef), TypeKeywordHint = typedef.TypeKeywordHint, };
    }

    private Expr[] EvaluateTypedefShape(Typedef typedef)
    {
        var dimensions = new Expr[typedef.ArrayShape.Count];
        for (int index = 0; index < dimensions.Length; index++)
        {
            Expr dimension = typedef.ArrayShape[index];
            if (dimension is Literal)
            {
                dimensions[index] = dimension;
                continue;
            }

            this.expressionEvaluator.Compile(dimension);
            int count = this.layoutExpressionEvaluator.Evaluate(
                dimension,
                this.staticLayoutVariables,
                "array length for typedef " + typedef.Name.Name);
            if (count < 0)
            {
                throw new CStructLayoutException("Array length cannot be negative: " + typedef.Name.Name);
            }

            dimensions[index] = new Literal(count);
        }

        return dimensions;
    }

    /// <summary>
    ///     Follows a field's typedef chain to the first <c>typedef T name[N];</c> alias and returns the element type
    ///     and dimensions it contributes; a field declared with a pointer to such an alias is rejected because the
    ///     language has no pointer-to-array storage.
    /// </summary>
    private (Identifier Type, IReadOnlyList<Expr> Shape)? ResolveTypedefArrayShape(Field field)
    {
        string name = field.Type.Name;
        List<Expr>? shape = null;
        int guard = 0;
        while (this.cStructElements.TryGetValue(name, out CStructElement? element) && element is Typedef { Struct: null, } alias)
        {
            if (alias.ArrayShape.Count > 0)
            {
                if (field.PointerDepth > 0 || alias.Type.PointerDepth > 0)
                {
                    throw new CStructLayoutException(
                        "A pointer to a typedef array is not supported: " + field.Name.Name);
                }

                // `typedef pair grid[2];` over `typedef uint16 pair[2];` is uint16[2][2]: outer alias first.
                (shape ??= []).AddRange(this.EvaluateTypedefShape(alias));
            }
            else if (alias.Type.PointerDepth > 0)
            {
                break;
            }

            if (++guard > 256)
            {
                break;
            }

            name = alias.Type.Name;
        }

        return shape is null ? null : (new Identifier(name), shape);
    }

    /// <summary>Evaluates every named enum with a preliminary resolver and returns one <c>Enum.Member</c> literal definition per member.</summary>
    private List<Defines> CreateQualifiedMemberDefinitions(IReadOnlyList<CStructElement> declarations, Defines[] definitions)
    {
        var preliminary = new LayoutVariableResolver(
            definitions,
            this.expressionEvaluator,
            this.FindExactEnumDefinitionDependencies(declarations, definitions));
        IReadOnlyDictionary<string, Expr> variables = preliminary.CreateStatic();
        var result = new List<Defines>();
        foreach (CstructEnum enm in declarations.OfType<CstructEnum>())
        {
            EnumIntegerCodec codec = this.enumIntegerCodecs.Get(enm.Name.Name);
            CstructEnum evaluated = enm.Evaluate(this.expressionEvaluator, variables, codec.BitWidth, codec.Minimum, codec.Maximum);
            foreach (EnumValue member in evaluated.Values)
            {
                result.Add(new Defines(new Identifier(enm.Name.Name + "." + member.Name.Name), member.Value));
            }
        }

        return result;
    }

    /// <summary>A text, byte, bare, or macro constant has no integer value, so naming it in an expression is a layout error, not a missing variable.</summary>
    private void RejectNonIntegerConstants(Expr expression, string fieldName)
    {
        foreach (string dependency in this.expressionEvaluator.GetDependencies(expression))
        {
            if (this.cStructElements.TryGetValue(dependency, out CStructElement? element) && element is ConstantDefinition constant)
            {
                throw new CStructLayoutException(
                    $"'{constant.Name.Name}' is a {constant.Kind.ToString().ToLowerInvariant()} constant and has no integer value: {fieldName}");
            }
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

    /// <summary>Compiles a group's selector and case dispatch once, preserving identity across all its arms.</summary>
    private ConditionalGroup NormalizeConditionalGroup(ConditionalGroup group, Dictionary<Expr, Expr>? constants, Dictionary<ConditionalGroup, ConditionalGroup> normalizedGroups)
    {
        if (normalizedGroups.TryGetValue(group, out ConditionalGroup? existing))
        {
            return existing;
        }

        Expr selector = NormalizeCaseConstants(group.Selector, constants)!;
        this.expressionEvaluator.Compile(selector);
        FrozenDictionary<int, int>? arms = group.CaseLabels?.Select((label, index) =>
            new KeyValuePair<int, int>(((Literal)constants![label]).Value, index)).ToFrozenDictionary();
        var normalized = new ConditionalGroup(selector) { CaseArms = arms };
        normalizedGroups.Add(group, normalized);
        return normalized;
    }

    /// <summary>Rebuilds a composite with precompiled array expressions and statically evaluated bit widths.</summary>
    private Struct NormalizeStructExpressions(Struct strct, Dictionary<Expr, Expr>? inheritedCaseConstants = null, Dictionary<ConditionalGroup, ConditionalGroup>? normalizedGroups = null)
    {
        if (normalizedGroups is null && (strct.BranchConditions.Count > 0 || strct.Fields.Any(field => field.BranchConditions.Count > 0)))
        {
            normalizedGroups = new Dictionary<ConditionalGroup, ConditionalGroup>();
        }

        Dictionary<Expr, Expr>? caseConstants = inheritedCaseConstants;
        bool copiedConstants = false;
        var fields = new List<Field>(strct.Fields.Count);
        foreach (SwitchCaseValidation validation in strct.Fields.OfType<SwitchCaseValidation>())
        {
            if (!copiedConstants)
            {
                caseConstants = inheritedCaseConstants is null
                    ? new Dictionary<Expr, Expr>(ReferenceEqualityComparer.Instance)
                    : new Dictionary<Expr, Expr>(inheritedCaseConstants, ReferenceEqualityComparer.Instance);
                copiedConstants = true;
            }

            var values = new HashSet<int>();
            foreach (Expr tag in validation.Tags)
            {
                int value = this.layoutExpressionEvaluator.Evaluate(
                    tag, this.staticLayoutVariables, "switch case constant");
                if (!values.Add(value))
                {
                    throw new CStructLayoutException("Duplicate switch case value: " + value);
                }

                caseConstants!.Add(tag, new Literal(value));
            }
        }

        foreach (Field field in strct.Fields.Where(field => field is not SwitchCaseValidation))
        {
            if (field.Condition is not null)
            {
                this.expressionEvaluator.Compile(field.Condition);
            }

            if (field is Struct nested)
            {
                fields.Add(this.NormalizeStructExpressions(nested, caseConstants, normalizedGroups));
                continue;
            }

            // Every dimension (LANG-05) gets the same early compile/evaluate pass a single-dimension array's one
            // count expression already got - the unsized-character-array sentinel is the only dimension value
            // that is never itself an expression to compile.
            foreach (Expr dimension in field.ArrayCount)
            {
                if (ReferenceEquals(dimension, Field.UnknownArraysize) || ExpressionEvaluator.ContainsCall(dimension))
                {
                    // A sizeof/offsetof dimension is folded to a literal once the types it names are compiled.
                    continue;
                }

                this.expressionEvaluator.Compile(dimension);
                this.RejectNonIntegerConstants(dimension, field.Name.Name);
                if (this.expressionEvaluator.GetDependencies(dimension).All(this.staticLayoutVariables.ContainsKey))
                {
                    int count = this.layoutExpressionEvaluator.Evaluate(
                        dimension,
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

            Identifier fieldType = field.Type;
            IReadOnlyList<Expr> arrayCount = field.ArrayCount;
            if (this.ResolveTypedefArrayShape(field) is (Identifier elementType, IReadOnlyList<Expr> typedefShape))
            {
                // `typedef uint32 quad[4]; quad rows[n];` is the array `uint32 rows[n][4]`: the field's own
                // dimensions are the outer ones.
                fieldType = elementType;
                bool unsized = arrayCount.Count == 1 && ReferenceEquals(arrayCount[0], Field.UnknownArraysize);
                if (unsized)
                {
                    throw new CStructLayoutException("An unsized array of a typedef array is not supported: " + field.Name.Name);
                }

                arrayCount = [.. arrayCount, .. typedefShape,];
            }

            fields.Add(
                new Field(
                    fieldType,
                    field.Name,
                    arrayCount,
                    bitSize,
                    field.PointerDepth,
                    field.TypeKeywordHint,
                    field.AlignmentOverrideExpression,
                    field.OffsetAssertionExpression)
                {
                    Condition = NormalizeCaseConstants(field.Condition, caseConstants),
                    BranchConditions = field.BranchConditions.Count == 0 ? Array.Empty<ConditionalBranch>() : field.BranchConditions.Select(item =>
                        new ConditionalBranch(this.NormalizeConditionalGroup(item.Group, caseConstants, normalizedGroups!), item.Arm)).ToArray(),
                });
        }

        return new Struct(strct.Name, [.. fields,], strct.IsUnion, strct.CompositeAlignmentOverrideExpression)
        {
            Condition = NormalizeCaseConstants(strct.Condition, caseConstants),
            BranchConditions = strct.BranchConditions.Count == 0 ? Array.Empty<ConditionalBranch>() : strct.BranchConditions.Select(item =>
                new ConditionalBranch(this.NormalizeConditionalGroup(item.Group, caseConstants, normalizedGroups!), item.Arm)).ToArray(),
        };
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
            Field field = target.EffectiveField ??
                          throw new CStructPathException(
                              "Path does not resolve to an array or string field: " + elementNameOrPath);
            CompiledField compiledField = target.EffectiveCompiledField ??
                                          throw new CStructPathException(
                                              "Path does not resolve to a compiled field: " + elementNameOrPath);

            if (compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or
                CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
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

            if (compiledField.Array.Kind == CompiledArrayKind.Scalar && compiledField.Codec.IsTerminatedText)
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

    /// <summary>Finds a named struct declaration in this compiled layout.</summary>
    internal Struct GetStruct(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        // Look up the declaration first so callers get the same clear error for an unknown name or a non-struct name.
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(name, out CStructElement? value))
        {
            throw new CStructPathException("Unknown struct declaration: " + name);
        }

        // A typedef of a struct or union (`typedef struct _X { } X;`, `typedef X Y;`) names the same storage.
        int guard = 0;
        while (value is Typedef alias && ++guard < 256)
        {
            if (alias.Struct is not null)
            {
                return alias.Struct;
            }

            if (alias.Type.PointerDepth > 0 || alias.ArrayShape.Count > 0 ||
                !this.compiledModelQueries.TryGetCompiledDeclaration(alias.Type.Name, out value))
            {
                break;
            }
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
        return this.compiledSizeQueries.GetCompiledComposite(this.GetStruct(name)).Symbol.Alignment;
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
        return this.compiledSizeQueries.GetCompiledStructSizeInBytes(
            this.compiledSizeQueries.GetCompiledComposite(this.GetStruct(name)),
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
    ///     Reads a selected composite value. Structs return <see cref="StructValue"/> values and unions return
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
        return this.ParseStreamCoreImpl(stream, elementNameOrPath, variables, options, false).Result;
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
        return this.ParseStreamCoreImpl(stream, elementNameOrPath, variables, options, true);
    }

    /// <summary>
    ///     Reads a selected object, optionally recording debug byte ranges - the shared implementation behind
    ///     <see cref="ParseStreamCore" /> and <see cref="ParseStreamWithDebugCore" />, which previously carried
    ///     two independently-maintained ~50-line copies of this same segment-resolution/parse/exception-attachment
    ///     structure, differing only by the <paramref name="debug" /> flag threaded through
    ///     <see cref="ParseStreamInternal" /> and <see cref="ParseCompiledStructAt" />. The one genuine behavioral
    ///     difference between the two modes - a single-segment root path returns the unwrapped selected value in
    ///     non-debug mode, but the whole root container (unless it is itself a <see cref="UnionValue" />) in debug
    ///     mode, so debug callers can see the root's own debug stack - is preserved explicitly below rather than
    ///     flattened away.
    /// </summary>
    private (List<DebugData> DebugData, dynamic Result) ParseStreamCoreImpl(
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

            dynamic returnedValue = debug ? (selected is UnionValue ? selected : root) : selected;
            return (rootDebugData, returnedValue);
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
            Struct target = ResolveStructTarget(resolvedTarget);
            (object result, List<DebugData> debugData) = this.ParseCompiledStructAt(
                state,
                resolvedTarget.Address,
                target,
                debug ? DebugPath.FromElements(resolvedTarget.DebugPrefix) : null,
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
