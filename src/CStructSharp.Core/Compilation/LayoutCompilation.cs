namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;
using System.Threading;
using CStructSharp;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation.Programs;
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

    // Built on first use, never by the constructor: a layout that only takes the static and direct paths must not pay
    // for it (construction allocation is budgeted).
    private SlotTable? slotTable;

    /// <summary>
    ///     Parses and compiles <paramref name="layout"/> (the prelude already prepended and the source validated by
    ///     <see cref="Create"/>) with the given placement settings and primitive vocabulary.
    /// </summary>
    /// <param name="layout">The complete layout text.</param>
    /// <param name="pointerSize">The stored pointer width in bytes (1, 2, 4, or 8).</param>
    /// <param name="aligned">Whether members are placed at their natural alignment.</param>
    /// <param name="isLittleEndian">The byte order neutral primitive spellings resolve to.</param>
    /// <param name="compilationOptions">The effective options (never null).</param>
    /// <param name="catalog">The primitive catalog, including any custom codec descriptors.</param>
    /// <exception cref="CStructLayoutException">The layout text is invalid; the source position is attached when known.</exception>
    private LayoutCompilation(
        string layout,
        byte pointerSize,
        bool aligned,
        bool isLittleEndian,
        CStructCompilationOptions compilationOptions,
        PrimitiveCatalog catalog)
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
                structResult = LayoutParser.ParseLayout(this.Source, effectiveCompilationOptions.Defined, effectiveCompilationOptions.DefaultEnumStorage, out usesQualifiedIdentifiers);
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

    /// <summary>Gets a value indicating whether members are placed at their natural alignment.</summary>
    public bool Aligned { get; }

    /// <summary>Gets the <c>#include</c> paths in source order; they are recorded, never resolved or read.</summary>
    public IReadOnlyList<string> Includes { get; }

    /// <summary>Gets the layout's <c>#define</c> constants by name, built on first access.</summary>
    public IReadOnlyDictionary<string, LayoutConstant> Constants => this.constants.Value;

    /// <summary>Gets the read-only description of the layout's declarations, built on first access.</summary>
    public LayoutInfo Layout => this.layoutInfo.Value;

    /// <summary>Gets the effective compilation options the layout was compiled with.</summary>
    public CStructCompilationOptions CompilationOptions { get; }

    /// <summary>Gets the name of the first struct or union in source order, used when a caller names no root.</summary>
    public string DefaultRoot => this.compiledModelQueries.GetFirstCompiledStructName();

    /// <summary>Gets how adjacent bit-fields share storage units.</summary>
    public BitfieldPacking BitfieldPacking { get; }

    /// <summary>Gets a value indicating whether bit-fields fill their unit from the most significant bit.</summary>
    public bool HighBitFirst => this.highBitFirst;

    /// <summary>Gets the primitive vocabulary (sizes, alignments, custom codecs) the layout resolves against.</summary>
    public PrimitiveCatalog Catalog => this.catalog;

    /// <summary>Gets the catalog's table of primitive codecs that are valid bit-field storage.</summary>
    public BitfieldCodecTable BitfieldCodecs => this.bitfieldCodecs;

    /// <summary>Gets the lookups over the finished compiled model (roots, enums, declarations).</summary>
    public CompiledModelQueries ModelQueries => this.compiledModelQueries;

    /// <summary>Gets the size calculations over the compiled composites.</summary>
    public CompiledSizeQueries SizeQueries => this.compiledSizeQueries;

    /// <summary>Gets the exact signed or unsigned integer domain declared for each enum.</summary>
    public EnumIntegerCodecTable EnumIntegerCodecs => this.enumIntegerCodecs;

    /// <summary>Gets the expression evaluator bound to this layout's evaluation limits.</summary>
    public ExpressionEvaluator ExpressionEvaluator => this.expressionEvaluator;

    /// <summary>Gets the evaluator that reports expression failures as layout or read errors.</summary>
    public LayoutExpressionEvaluator LayoutExpressionEvaluator => this.layoutExpressionEvaluator;

    /// <summary>Gets the resolver that combines layout definitions with caller-supplied variable overrides.</summary>
    public LayoutVariableResolver LayoutVariableResolver => this.layoutVariableResolver;

    /// <summary>Gets the layout's definitions resolved without caller overrides, by name.</summary>
    public IReadOnlyDictionary<string, Expr> StaticLayoutVariables => this.staticLayoutVariables;

    /// <summary>
    ///     Gets the layout's slot table: one slot per name a layout expression can read, the resolved definitions'
    ///     initial states, and the slot-indexed expression programs. Built on first access and shared by every thread.
    /// </summary>
    public SlotTable SlotTable => Volatile.Read(ref this.slotTable) ?? this.BuildSlotTable();

    /// <summary>Gets a value indicating whether <see cref="SlotTable"/> has been built.</summary>
    public bool HasSlotTable => Volatile.Read(ref this.slotTable) is not null;

    /// <summary>Gets a value indicating whether neutral primitive spellings resolve to little-endian.</summary>
    public bool IsLittleEndian { get; }

    /// <summary>Gets the stored pointer width in bytes: 1, 2, 4, or 8.</summary>
    public byte PointerSize { get; }

    /// <summary>Gets the complete compiled layout text, including any prelude from the options.</summary>
    public string Source { get; }

    /// <summary>
    ///     Compiles a layout the one way the runtime's <c>CStruct</c> constructor, the source generator and the analyzer
    ///     all do: the options' prelude joins the text, the text and options are validated, and the pointer width is
    ///     checked. The catalog's custom codecs are type names like any primitive.
    /// </summary>
    /// <param name="layout">The layout text, without the prelude.</param>
    /// <param name="pointerSize">The stored pointer width in bytes: 1, 2, 4, or 8.</param>
    /// <param name="aligned">Whether members are placed at their natural alignment.</param>
    /// <param name="isLittleEndian">The byte order neutral primitive spellings resolve to.</param>
    /// <param name="compilationOptions">The effective options.</param>
    /// <param name="catalog">
    ///     The primitive catalog for the options' byte order and <c>long</c> width, with any custom codecs
    ///     (<see cref="PrimitiveCatalog.WithCustomCodecs"/>); callers build it first so a codec error stays theirs.
    /// </param>
    /// <returns>The compiled layout.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="layout"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="pointerSize"/> is unsupported, or an option is out of range.</exception>
    /// <exception cref="CStructLayoutException">The layout text is empty, exceeds a limit, or is invalid.</exception>
    public static LayoutCompilation Create(
        string layout,
        int pointerSize,
        bool aligned,
        bool isLittleEndian,
        CStructCompilationOptions compilationOptions,
        PrimitiveCatalog catalog)
    {
        if (!string.IsNullOrEmpty(compilationOptions.Prelude))
        {
            if (layout is null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            // The prelude is simply earlier source text: one parse, one namespace, one cache entry.
            layout = compilationOptions.Prelude + "\n" + layout;
        }

        LayoutSourceValidator.ValidateLayoutSource(layout, compilationOptions);

        // Reject pointer widths that the primitive reader and writer cannot represent.
        if (pointerSize is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(pointerSize), "Pointer size must be 1, 2, 4, or 8 bytes.");
        }

        return new LayoutCompilation(layout, (byte)pointerSize, aligned, isLittleEndian, compilationOptions, catalog);
    }

    /// <summary>
    ///     Builds the slot table and publishes it; when two threads race, both build one and every caller then uses
    ///     the first one published (the tables are equal, so the loser's is simply dropped).
    /// </summary>
    /// <returns>The published table.</returns>
    private SlotTable BuildSlotTable()
    {
        SlotTable built = SlotTable.Create(this.CollectReferencedNames(), this.layoutVariableResolver, this.expressionEvaluator);
        return Interlocked.CompareExchange(ref this.slotTable, built, null) ?? built;
    }

    /// <summary>Finds a named struct or union declaration through its finite chain of aliases.</summary>
    /// <param name="name">The declaration or alias name to resolve.</param>
    /// <returns>The underlying composite declaration, without changing its storage shape.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="CStructPathException">The name is unknown or denotes something other than a composite object.</exception>
    internal Struct GetStruct(string name)
    {
        if (name is null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        // Look up the declaration first so callers get the same clear error for an unknown name or a non-struct name.
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(name, out CStructElement? value))
        {
            throw new CStructPathException("Unknown struct declaration: " + name);
        }

        // A typedef of a struct or union (`typedef struct _X { } X;`, `typedef X Y;`) names the same storage.
        // A finite chain cannot visit more aliases than this layout has declarations.
        int remainingDeclarations = this.cStructElements.Count;
        while (value is Typedef alias && remainingDeclarations-- > 0)
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

    /// <summary>
    ///     Returns the compiled engine's read program of a composite of this layout, compiling it (and the programs of the
    ///     structs it holds) on first request; see <see cref="ReadProgramCompiler"/>. Nothing is compiled by constructing
    ///     the layout.
    /// </summary>
    /// <param name="composite">A composite of this layout.</param>
    /// <returns>The program, or the reason the engine cannot read the composite.</returns>
    public ReadProgramOutcome GetReadProgram(CompiledCompositeType composite) => this.SlotTable.ReadPrograms.GetComposite(this, composite);

    /// <summary>
    ///     Returns the compiled engine's read program of a root, compiling it on first request. A root is eligible when
    ///     every struct it reaches compiled without a reason (engine plan 6.2).
    /// </summary>
    /// <param name="rootName">A declared root name, or a type spelling already registered as a root.</param>
    /// <returns>The program, or the reason the engine cannot read the root.</returns>
    public ReadProgramOutcome GetRootReadProgram(string rootName) => this.SlotTable.ReadPrograms.GetRoot(this, rootName);

    /// <summary>
    ///     Returns the compiled engine's debug read program of a root - the program a debug parse runs, which records every
    ///     value's byte range and path (<see cref="ReadProgramCache.Debug"/>) - compiling it on first request. It is
    ///     eligible exactly when <see cref="GetRootReadProgram"/> is: the debug program reads the same members.
    /// </summary>
    /// <param name="rootName">A declared root name, or a type spelling already registered as a root.</param>
    /// <returns>The program, or the reason the engine cannot read the root.</returns>
    public ReadProgramOutcome GetRootDebugReadProgram(string rootName) => this.SlotTable.DebugReadPrograms.GetRoot(this, rootName);

    /// <summary>
    ///     Returns the compiled engine's write program of a composite of this layout, compiling it on first request; see
    ///     <see cref="WriteProgramCompiler"/>.
    /// </summary>
    /// <param name="composite">A composite of this layout.</param>
    /// <returns>The program, or the reason the engine cannot write the composite.</returns>
    public WriteProgramOutcome GetWriteProgram(CompiledCompositeType composite) => this.SlotTable.WritePrograms.GetComposite(this, composite);

    /// <summary>
    ///     Returns the compiled engine's write program of a root, compiling it on first request. A root is eligible when
    ///     every struct it writes compiled without a reason.
    /// </summary>
    /// <param name="rootName">A declared root name, or a type spelling already registered as a root.</param>
    /// <returns>The program, or the reason the engine cannot write the root.</returns>
    public WriteProgramOutcome GetRootWriteProgram(string rootName) => this.SlotTable.WritePrograms.GetRoot(this, rootName);

    /// <summary>
    ///     Returns the compiled engine's write program of the member a nested path selects - written on its own from the
    ///     value at the path - compiling it on first request. The path's shape is
    ///     resolved without variables (a write checks its indexes against the counts when it runs), so the program depends
    ///     only on the member and the dimensions the indexes peel.
    /// </summary>
    /// <param name="root">The path's root declaration.</param>
    /// <param name="childSegments">The segments after the root; at least one.</param>
    /// <returns>The program, or the reason the engine cannot write the path: it selects no writable member, or the member cannot be written.</returns>
    public WriteProgramOutcome GetPathWriteProgram(CStructElement root, IReadOnlyList<PathSegment> childSegments)
    {
        CompiledField declared;
        int peeled;
        try
        {
            _ = this.ResolveElementPath(root, childSegments, null, out declared, out peeled);
        }
        catch (CStructException exception)
        {
            return WriteProgramOutcome.NotSupported(WriteProgramCompiler.UnresolvedPath + exception.Message);
        }

        return this.SlotTable.WritePrograms.GetMember(this, declared, peeled);
    }
}
