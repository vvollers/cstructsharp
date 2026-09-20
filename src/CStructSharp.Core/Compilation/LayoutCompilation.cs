namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using CStructSharp;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Introspection;
using CStructSharp.Parsing;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     Everything about a layout that is known before any byte is read: the parsed declarations, the resolved
///     definitions and enum codecs, the compiled model with every member's placement, the size and model queries,
///     introspection, and the rendered definition. It is the part of the library the source generator hosts inside
///     the compiler, so it holds no I/O, no delegates, and no reflection; <see cref="CStruct"/> wraps one instance
///     and adds the runtime (codec delegates, readers, writers, address resolution).
/// </summary>
internal sealed partial class LayoutCompilation
{
    private readonly BitfieldCodecTable bitfieldCodecs;

    private readonly ConstructionDictionary<string, CStructElement> cStructElements =
        new(StringComparer.Ordinal);

    private readonly ConstructionDictionary<string, byte> fieldAlignments;
    private readonly PrimitiveCatalog catalog;
    private readonly IReadOnlyDictionary<string, CompiledTypeReference> customSymbols;
    private readonly bool highBitFirst;
    private readonly CompiledModelQueries compiledModelQueries;
    private readonly CompiledSizeQueries compiledSizeQueries;
    private readonly EnumIntegerCodecTable enumIntegerCodecs;
    private readonly ExpressionEvaluator expressionEvaluator;
    private readonly LayoutExpressionEvaluator layoutExpressionEvaluator;
    private readonly LayoutVariableResolver layoutVariableResolver;
    private readonly IReadOnlyDictionary<string, Expr> staticLayoutVariables;
    private readonly Lazy<IReadOnlyDictionary<string, LayoutConstant>> constants;
    private readonly Lazy<LayoutInfo> layoutInfo;

    /// <summary>
    ///     Parses and compiles <paramref name="layout"/> (the prelude already prepended and the source validated by
    ///     the caller) with the given placement settings and primitive vocabulary.
    /// </summary>
    /// <param name="layout">The complete layout text.</param>
    /// <param name="pointerSize">The stored pointer width in bytes (1, 2, 4, or 8).</param>
    /// <param name="aligned">Whether members are placed at their natural alignment.</param>
    /// <param name="isLittleEndian">The byte order neutral primitive spellings resolve to.</param>
    /// <param name="compilationOptions">The effective options (never null).</param>
    /// <param name="catalog">The primitive catalog, including any custom codec descriptors.</param>
    /// <param name="customSymbols">The custom codec symbols the catalog registered, by name.</param>
    /// <exception cref="CStructLayoutException">The layout text is invalid; the source position is attached when known.</exception>
    public LayoutCompilation(
        string layout,
        byte pointerSize,
        bool aligned,
        bool isLittleEndian,
        CStructCompilationOptions compilationOptions,
        PrimitiveCatalog catalog,
        IReadOnlyDictionary<string, CompiledTypeReference> customSymbols)
    {
        CStructCompilationOptions effectiveCompilationOptions = compilationOptions;
        this.CompilationOptions = effectiveCompilationOptions;
        this.layoutInfo = new Lazy<LayoutInfo>(this.BuildLayoutInfo);
        this.constants = new Lazy<IReadOnlyDictionary<string, LayoutConstant>>(this.BuildConstants);
        this.highBitFirst = effectiveCompilationOptions.BitfieldAllocation == BitfieldAllocation.HighBitFirst;
        this.BitfieldPacking = effectiveCompilationOptions.BitfieldPacking;
        this.expressionEvaluator = new ExpressionEvaluator(
            ExpressionEvaluationLimits.FromOptions(effectiveCompilationOptions));
        this.layoutExpressionEvaluator = new LayoutExpressionEvaluator(this.expressionEvaluator);
        this.Source = layout;
        this.Aligned = aligned;
        this.PointerSize = pointerSize;
        this.IsLittleEndian = isLittleEndian;
        this.catalog = catalog;
        this.customSymbols = customSymbols;

        // The catalog's alignments are the shared baseline; only this layout's declarations are added on top.
        this.fieldAlignments = new ConstructionDictionary<string, byte>(StringComparer.Ordinal, this.catalog.Alignments);
        this.bitfieldCodecs = this.catalog.Bitfields;

        try
        {
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

                SymbolValidation.ValidateBuiltInNameCollision(declaration, this.catalog);
                if (this.cStructElements.TryGetValue(declaration.Name.Name, out CStructElement? existing))
                {
                    throw new CStructLayoutException(
                        $"Duplicate global declaration name '{declaration.Name.Name}': " +
                        $"{SymbolValidation.GetDeclarationKind(existing)} and {SymbolValidation.GetDeclarationKind(declaration)}.")
                    {
                        SourceOffset = declaration.Name.SourceOffset,
                    };
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
                        $"{SymbolValidation.GetDeclarationKind(existing)} and {SymbolValidation.GetDeclarationKind(tagged)}.")
                    {
                        SourceOffset = tagged.Name.SourceOffset,
                    };
                }

                SymbolValidation.ValidateBuiltInNameCollision(tagged, this.catalog);
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
                this.BitfieldPacking,
                this.highBitFirst,
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
        catch (CStructLayoutException exception) when (exception.SourceOffset >= 0 && exception.Line is null)
        {
            // The declaration that failed is known by its source offset; report it as a line and column once, here,
            // where the source text is at hand.
            (int line, int column) = LayoutParser.LocatePosition(this.Source, exception.SourceOffset);
            exception.AttachSourcePosition(line, column);
            throw;
        }
    }

    /// <summary>The compiled definitions (structs, unions, enums, typedefs, defines) by declared name.</summary>
    public IReadOnlyDictionary<string, CStructElement> CStructElements => this.cStructElements;

    public bool Aligned { get; }

    public IReadOnlyList<string> Includes { get; }

    public IReadOnlyDictionary<string, LayoutConstant> Constants => this.constants.Value;

    public LayoutInfo Layout => this.layoutInfo.Value;

    public CStructCompilationOptions CompilationOptions { get; }

    public string DefaultRoot => this.compiledModelQueries.GetFirstCompiledStructName();

    public BitfieldPacking BitfieldPacking { get; }

    public bool HighBitFirst => this.highBitFirst;

    public IReadOnlyDictionary<string, byte> FieldAlignments => this.fieldAlignments;

    public PrimitiveCatalog Catalog => this.catalog;

    public IReadOnlyDictionary<string, CompiledTypeReference> CustomSymbols => this.customSymbols;

    public BitfieldCodecTable BitfieldCodecs => this.bitfieldCodecs;

    public CompiledModelQueries ModelQueries => this.compiledModelQueries;

    public CompiledSizeQueries SizeQueries => this.compiledSizeQueries;

    public EnumIntegerCodecTable EnumIntegerCodecs => this.enumIntegerCodecs;

    public ExpressionEvaluator ExpressionEvaluator => this.expressionEvaluator;

    public LayoutExpressionEvaluator LayoutExpressionEvaluator => this.layoutExpressionEvaluator;

    public LayoutVariableResolver LayoutVariableResolver => this.layoutVariableResolver;

    public IReadOnlyDictionary<string, Expr> StaticLayoutVariables => this.staticLayoutVariables;

    public bool IsLittleEndian { get; }

    public byte PointerSize { get; }

    public string Source { get; }

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

    /// <summary>Freezes case constants without recursive traversal or changing runtime selectors.</summary>
    private static Expr? NormalizeCaseConstants(Expr? expression, Dictionary<Expr, Expr>? constants)
    {
        if (expression is null || constants is null || constants.Count == 0)
        {
            return expression;
        }

        var rewritten = new Dictionary<Expr, Expr>(constants, ReferenceEqualityComparer.Instance);
        var pending = new Stack<(Expr Expression, bool Complete)>();
        pending.Push((expression, false));
        while (pending.Count > 0)
        {
            (Expr current, bool complete) = pending.Pop();
            if (rewritten.ContainsKey(current))
            {
                continue;
            }

            if (complete)
            {
                rewritten[current] = current switch
                {
                    BinaryOp binary => new BinaryOp(binary.Type, rewritten[binary.Left], rewritten[binary.Right]),
                    UnaryOp unary => new UnaryOp(unary.Type, rewritten[unary.Expr]),
                    ConditionalExpr conditional => new ConditionalExpr(rewritten[conditional.Condition], rewritten[conditional.WhenTrue], rewritten[conditional.WhenFalse]),
                    _ => current,
                };
                continue;
            }

            pending.Push((current, true));
            if (current is BinaryOp operation)
            {
                pending.Push((operation.Right, false));
                pending.Push((operation.Left, false));
            }
            else if (current is UnaryOp unary)
            {
                pending.Push((unary.Expr, false));
            }
            else if (current is ConditionalExpr conditional)
            {
                pending.Push((conditional.WhenFalse, false));
                pending.Push((conditional.WhenTrue, false));
                pending.Push((conditional.Condition, false));
            }
        }

        return rewritten[expression];
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

    /// <summary>Publishes every constant definition and static define as a <see cref="LayoutConstant"/>.</summary>
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
        ImmutableDictionary<int, int>? arms = group.CaseLabels?.Select((label, index) =>
            new KeyValuePair<int, int>(((Literal)constants![label]).Value, index)).ToImmutableDictionary();
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

            // Every dimension gets the same early compile/evaluate pass a single-dimension array's one
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
                if (bitSize < 0 || (bitSize == 0 && field.Name.Name.Length > 0))
                {
                    throw new CStructLayoutException(
                        bitSize < 0
                            ? "Bitfield width cannot be negative: " + field.Name.Name
                            : "Bitfield width must be greater than zero (only an unnamed ': 0' separator may be zero): " + field.Name.Name);
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
                    field.OffsetAssertionExpression,
                    field.HasBitfieldDeclarator)
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
}
