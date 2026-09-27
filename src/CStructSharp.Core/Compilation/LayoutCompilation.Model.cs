namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;

using System.Linq;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>Builds and queries the immutable compiled layout model from validated parsed declarations.</summary>
internal sealed partial class LayoutCompilation
{
    private readonly CompiledLayoutModel compiledLayout;

    /// <summary>Gets the immutable internal model for invariant tests and later compiled-executor migrations.</summary>
    internal CompiledLayoutModel CompiledModel => this.compiledLayout;

    /// <summary>The codec id a synthetic root of <paramref name="symbol"/> reads with (a primitive's own, an enum's underlying, else none).</summary>
    internal static int CodecIdOf(CompiledTypeSymbol symbol)
    {
        return GetCompiledCodecId(symbol);
    }

    /// <summary>Compiles the array strategy of a synthetic root field spelled in a path (<c>uint16[EOF]</c>).</summary>
    /// <param name="field">The parsed root field.</param>
    /// <returns>The array shape.</returns>
    /// <exception cref="CStructLayoutException">The count expression is invalid.</exception>
    internal CompiledArrayShape CompileRootArrayShape(Field field)
    {
        return this.CompileArrayShape(field);
    }

    /// <summary>Marks whether a field is named by any layout expression, and collects the named non-integer fields.</summary>
    /// <param name="field">The compiled field.</param>
    /// <param name="referenced">Every name the layout's expressions read, or <see langword="null"/> when none.</param>
    /// <param name="nonInteger">Receives each named field that cannot be an integer; created on the first one.</param>
    private static void MarkField(CompiledField field, HashSet<string>? referenced, ref List<CompiledField>? nonInteger)
    {
        field.CapturesLayoutVariable = referenced is not null && referenced.Contains(field.Declaration.Name.Name);
        if (field.CapturesLayoutVariable && field.NotANumberReason is not null)
        {
            (nonInteger ??= []).Add(field);
        }
    }

    /// <summary>Returns whether any field declared with <paramref name="name"/> holds an integer.</summary>
    /// <param name="symbols">Every compiled type.</param>
    /// <param name="rootFields">The compiled root fields.</param>
    /// <param name="name">The declared field name.</param>
    /// <returns>Whether an integer field of that name exists.</returns>
    private static bool HasIntegerField(HashSet<CompiledTypeSymbol> symbols, ImmutableDictionary<CStructElement, CompiledField>.Builder rootFields, string name)
    {
        foreach (CompiledTypeSymbol symbol in symbols)
        {
            if (symbol.Definition is CompiledCompositeType composite)
            {
                foreach (CompiledField field in composite.Fields)
                {
                    if (field.Declaration.Name.Name == name && field.NotANumberReason is null)
                    {
                        return true;
                    }
                }
            }
        }

        foreach (CompiledField field in rootFields.Values)
        {
            if (field.Declaration.Name.Name == name && field.NotANumberReason is null)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Converts a placement position to a build-time offset, which must fit an Int32; an unknown position stays unknown.</summary>
    /// <param name="position">The position, or <see langword="null"/>.</param>
    /// <returns>The offset, or <see langword="null"/>.</returns>
    /// <exception cref="OverflowException">The position exceeds the Int32 range.</exception>
    private static int? ToOffset(long? position) => position is long value ? checked((int)value) : null;

    /// <summary>
    ///     Adds the names every expression of one declaration can read: a struct's own and nested field expressions,
    ///     a typedef's struct, a <c>#define</c> value, and enum member values.
    /// </summary>
    /// <param name="element">The declaration.</param>
    /// <param name="referenced">The collected names, created on first use.</param>
    /// <param name="pending">The work stack for nested expressions, created on first use.</param>
    private static void CollectExpressionReferences(CStructElement element, ref HashSet<string>? referenced, ref Stack<Expr>? pending)
    {
        switch (element)
        {
        case Struct strct:
            CollectFieldReferences(strct, ref referenced, ref pending);
            foreach (Field member in strct.Fields)
            {
                if (member is Struct nested)
                {
                    CollectExpressionReferences(nested, ref referenced, ref pending);
                }
                else
                {
                    CollectFieldReferences(member, ref referenced, ref pending);
                }
            }

            break;
        case Typedef typedef when typedef.Struct is not null:
            CollectExpressionReferences(typedef.Struct, ref referenced, ref pending);
            break;
        case Defines defines:
            AddReferences(defines.Value, ref referenced, ref pending);
            break;
        case CstructEnum enm:
            foreach (EnumValue value in enm.DeclaredValues)
            {
                AddReferences(value.Value, ref referenced, ref pending);
            }

            break;
        }
    }

    /// <summary>Adds the names every expression of one field can read: dimensions, bit width, conditions, and the <c>@count</c> element count.</summary>
    /// <param name="field">The field declaration.</param>
    /// <param name="referenced">The collected names, created on first use.</param>
    /// <param name="pending">The work stack for nested expressions, created on first use.</param>
    private static void CollectFieldReferences(Field field, ref HashSet<string>? referenced, ref Stack<Expr>? pending)
    {
        IReadOnlyList<Expr> dimensions = field.ArrayCount;
        for (int index = 0; index < dimensions.Count; index++)
        {
            AddReferences(dimensions[index], ref referenced, ref pending);
        }

        AddReferences(field.BitSizeExpression, ref referenced, ref pending);
        AddReferences(field.Condition, ref referenced, ref pending);
        AddReferences(field.PointerCountExpression, ref referenced, ref pending);
        IReadOnlyList<ConditionalBranch> branches = field.BranchConditions;
        for (int index = 0; index < branches.Count; index++)
        {
            ConditionalGroup group = branches[index].Group;
            AddReferences(group.Selector, ref referenced, ref pending);
            if (group.CaseLabels is not null)
            {
                foreach (Expr label in group.CaseLabels)
                {
                    AddReferences(label, ref referenced, ref pending);
                }
            }
        }
    }

    /// <summary>
    ///     Walks the tree directly instead of compiling a program for it: a per-field condition would otherwise be
    ///     compiled here purely to read its identifiers, growing layout-compilation allocation.
    /// </summary>
    private static void AddReferences(Expr? expression, ref HashSet<string>? referenced, ref Stack<Expr>? pending)
    {
        if (expression is null || ReferenceEquals(expression, NoneExpr.Instance) || expression is Literal)
        {
            return;
        }

        if (expression is Identifier direct)
        {
            (referenced ??= new HashSet<string>(StringComparer.Ordinal)).Add(direct.Name);
            return;
        }

        pending ??= new Stack<Expr>();
        pending.Push(expression);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
            case Identifier identifier:
                (referenced ??= new HashSet<string>(StringComparer.Ordinal)).Add(identifier.Name);
                break;
            case UnaryOp unary:
                pending.Push(unary.Expr);
                break;
            case BinaryOp binary:
                pending.Push(binary.Left);
                pending.Push(binary.Right);
                break;
            case ConditionalExpr conditional:
                pending.Push(conditional.Condition);
                pending.Push(conditional.WhenTrue);
                pending.Push(conditional.WhenFalse);
                break;
            case Call call:
                pending.Push(call.Expr);
                foreach (Expr argument in call.Arguments)
                {
                    pending.Push(argument);
                }

                break;
            }
        }
    }

    /// <summary>
    ///     Records on every bitfield the bit length of its run of adjacent positive-width bitfields. A zero-width
    ///     separator ends the run; placement aligns its successor before starting the next independent run.
    ///     The value depends only on the run's declarations, so the runtime cursor can clamp packed units without
    ///     looking ahead.
    /// </summary>
    /// <param name="fields">The composite's compiled fields in declaration order.</param>
    private static void MeasureBitfieldRuns(ImmutableArray<CompiledField> fields)
    {
        int index = 0;
        while (index < fields.Length)
        {
            if (!fields[index].BitStorageSize.HasValue || fields[index].IsZeroWidthBitfield || fields[index].Declaration.Condition is not null)
            {
                index++;
                continue;
            }

            int start = index;
            long bits = 0;
            while (index < fields.Length && fields[index].BitStorageSize.HasValue && !fields[index].IsZeroWidthBitfield && fields[index].Declaration.Condition is null)
            {
                // Separator padding belongs to placement, not to either neighboring run's storage window.
                CompiledField field = fields[index];
                bits += field.EffectiveField.BitSize;
                index++;
            }

            for (int member = start; member < index; member++)
            {
                fields[member].BitRunBits = checked((int)bits);
            }
        }
    }

    /// <summary>
    ///     A dotted reference (<c>hdr.n</c>, <c>a.b.n</c>) names a field of a nested struct. Each head becomes a
    ///     qualified-publishing field and each remainder joins the referenced set, so <c>n</c> is captured and the
    ///     struct field <c>hdr</c> republishes it as <c>hdr.n</c>; a name that never matches a struct field stays an
    ///     undefined identifier at evaluation time. Enum members (<c>E.N</c>) were already folded to constants.
    /// </summary>
    private static List<string>? ExpandQualifiedReferences(HashSet<string>? referenced)
    {
        if (referenced is null)
        {
            return null;
        }

        List<string>? heads = null;
        var pending = new Stack<string>();
        foreach (string name in referenced)
        {
            if (name.Contains('.'))
            {
                pending.Push(name);
            }
        }

        while (pending.Count > 0)
        {
            string name = pending.Pop();
            int dot = name.IndexOf('.');
            string head = name[..dot];
            string rest = name[(dot + 1)..];
            (heads ??= []).Add(head);
            if (referenced.Add(rest) && rest.Contains('.'))
            {
                pending.Push(rest);
            }
        }

        return heads;
    }

    /// <summary>Returns a primitive's codec id directly or the compiled underlying primitive's id for an enum.</summary>
    private static int GetCompiledCodecId(CompiledTypeSymbol symbol)
    {
        return symbol.Kind switch
        {
            CompiledTypeKind.Primitive => symbol.CodecId,
            CompiledTypeKind.Enum when symbol.Definition is CompiledEnumType enm => enm.Underlying.Symbol.CodecId,
            _ => PrimitiveCatalog.NoCodec,
        };
    }

    /// <summary>
    ///     Checks that a type written with a <c>struct</c>, <c>union</c> or <c>enum</c> keyword names that kind of type.
    /// </summary>
    /// <param name="keyword">The keyword written before the type name, or <see langword="null"/> when none was.</param>
    /// <param name="type">The resolved type.</param>
    /// <param name="subject">The declaration, for the diagnostic (for example <c>Field 'x'</c>).</param>
    /// <param name="typeName">The written type name; its source offset locates the diagnostic.</param>
    /// <exception cref="CStructLayoutException">The keyword names a different kind of type.</exception>
    private static void CheckTypeKeyword(string? keyword, CompiledTypeReference type, string subject, Identifier typeName)
    {
        if (keyword is null)
        {
            return;
        }

        string actualKind = type.Symbol.Kind.ToString().ToLowerInvariant();
        if (!string.Equals(keyword, actualKind, StringComparison.Ordinal))
        {
            throw new CStructLayoutException($"{subject} declared as '{keyword}' but '{typeName.Name}' is a {actualKind}.")
            {
                SourceOffset = typeName.SourceOffset,
            };
        }
    }

    /// <summary>Builds the operation-time model after parsed declarations have passed all layout validation.</summary>
    private CompiledLayoutModel CompileIntermediateRepresentation()
    {
        try
        {
            return this.BuildCompiledLayout();
        }
        catch (CStructLayoutException)
        {
            throw;
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or
                                          InvalidOperationException or KeyNotFoundException)
        {
            throw new CStructLayoutException(
                "Layout could not be converted to the compiled intermediate representation.",
                exception);
        }
    }

    /// <summary>Creates all type symbols first, then binds immutable enum, field, and composite definitions.</summary>
    private CompiledLayoutModel BuildCompiledLayout()
    {
        // The catalog's symbols include its custom codecs.
        var namedTypes = this.catalog.Symbols.ToBuilder();

        // A layout declaration shadows a built-in alias spelling of the same name (SymbolValidation lets those
        // through); drop the built-in entry so the declaration is what the name resolves to.
        foreach (string declaredName in this.CStructElements.Keys)
        {
            if (PrimitiveSpellings.IsAlias(declaredName))
            {
                namedTypes.Remove(declaredName);
            }
        }

        var compositeSymbols = new Dictionary<Struct, CompiledTypeSymbol>(ReferenceEqualityComparer.Instance);
        var enumSymbols = new Dictionary<CstructEnum, CompiledTypeSymbol>(ReferenceEqualityComparer.Instance);

        foreach (CStructElement declaration in this.CStructElements.Values)
        {
            switch (declaration)
            {
            case Struct strct:
                this.DiscoverCompositeSymbols(strct, compositeSymbols);
                break;
            case Typedef { Struct: not null, } typedef:
                this.DiscoverCompositeSymbols(typedef.Struct, compositeSymbols);
                break;
            case CstructEnum enm:
                enumSymbols.Add(
                    enm,
                    new CompiledTypeSymbol(
                        enm.Name.Name,
                        CompiledTypeKind.Enum,
                        enm,
                        this.enumIntegerCodecs.Get(enm.Name.Name).SizeInBytes,
                        this.enumIntegerCodecs.Get(enm.Name.Name).SizeInBytes,
                        PrimitiveCatalog.NoCodec));
                break;
            }
        }

        // Every composite symbol is discovered by this point and compositeSymbols never gains further entries, so a
        // size-query view built now stays valid for the rest of construction - including the union-storage check
        // below, before the final immutable CompiledLayoutModel exists.
        var sizeQueries = new CompiledSizeQueries(compositeSymbols, this.Aligned, this.BitfieldPacking, this.highBitFirst, this.layoutExpressionEvaluator);

        foreach (KeyValuePair<string, CStructElement> declaration in this.CStructElements)
        {
            if (declaration.Value is Struct strct)
            {
                namedTypes.Add(
                    declaration.Key,
                    new CompiledTypeReference(
                        compositeSymbols[strct],
                        0,
                        declaration.Key));
            }
        }

        foreach (KeyValuePair<CstructEnum, CompiledTypeSymbol> enm in enumSymbols)
        {
            namedTypes.Add(enm.Key.Name.Name, new CompiledTypeReference(enm.Value, 0, enm.Key.Name.Name));
        }

        var resolvingAliases = new HashSet<string>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, CStructElement> declaration in this.CStructElements)
        {
            if (declaration.Value is Typedef)
            {
                _ = this.ResolveCompiledTypeReference(declaration.Key, namedTypes, compositeSymbols, resolvingAliases);
            }
        }

        foreach (KeyValuePair<CstructEnum, CompiledTypeSymbol> enm in enumSymbols)
        {
            CompiledTypeReference underlying = this.ResolveCompiledTypeReference(
                enm.Key.Type.Name,
                namedTypes,
                compositeSymbols,
                resolvingAliases);
            if (underlying.PointerDepth != 0 || underlying.Symbol.Kind != CompiledTypeKind.Primitive)
            {
                throw new CStructLayoutException(
                    "Enum storage type must resolve to a scalar primitive codec: " + enm.Key.Type.Name);
            }

            var members = ImmutableArray.CreateBuilder<CompiledEnumMember>(enm.Key.Values.Length);
            foreach (EnumValue value in enm.Key.Values)
            {
                if (value.Value is not Literal literal)
                {
                    throw new CStructLayoutException(
                        "Compiled enum member is not an evaluated literal: " + value.Name.Name);
                }

                members.Add(
                    new CompiledEnumMember(
                        value.Name.Name,
                        this.enumIntegerCodecs.Get(enm.Key.Name.Name).ToRawBits(literal.ExactValue)));
            }

            enm.Value.Bind(
                new CompiledEnumType(
                    enm.Value,
                    underlying,
                    this.enumIntegerCodecs.Get(enm.Key.Name.Name),
                    members.ToImmutable(),
                    enm.Key.IsFlag));
        }

        var compilingComposites = new HashSet<Struct>(ReferenceEqualityComparer.Instance);
        foreach (Struct declaration in compositeSymbols.Keys)
        {
            this.CompileComposite(
                declaration,
                compositeSymbols,
                namedTypes,
                resolvingAliases,
                compilingComposites,
                sizeQueries);
        }

        var rootFields = ImmutableDictionary.CreateBuilder<CStructElement, CompiledField>(
            ReferenceEqualityComparer.Instance);
        foreach (CStructElement declaration in this.CStructElements.Values)
        {
            if (declaration is not (Typedef or CstructEnum))
            {
                continue;
            }

            CompiledTypeReference type = namedTypes[declaration.Name.Name];

            // A `typedef T name[N];` root reads its whole fixed shape.
            IReadOnlyList<Expr> rootShape = declaration is Typedef { ArrayShape.Count: > 0, } arrayAlias
                                                ? arrayAlias.ArrayShape
                                                : Field.NoArray;
            var field = new Field(
                new Identifier(type.TerminalName),
                declaration.Name,
                rootShape,
                0,
                type.PointerDepth);
            int codecId = GetCompiledCodecId(type.Symbol);
            int terminatedCodecId = PrimitiveCatalog.NoCodec;
            if (type.PointerDepth > 0 && CharacterFieldTypes.IsStringPointerType(field.Type))
            {
                terminatedCodecId = this.catalog.CodecIdOf(CharacterFieldTypes.GetStringPointerHandlerKey(field.Type));
            }

            int alignment = type.PointerDepth > 0 ? this.PointerSize : type.Symbol.Alignment;
            int? elementSize = type.PointerDepth > 0 ? this.PointerSize : type.Symbol.FixedSize;
            CompiledArrayShape rootArrayShape = this.CompileArrayShape(field);
            int? rootStorageSize = elementSize.HasValue && rootArrayShape.TotalFixedElementCount.HasValue
                                       ? checked(elementSize.Value * rootArrayShape.TotalFixedElementCount.Value)
                                       : elementSize;
            var compiledRoot = new CompiledField(
                field,
                field,
                type,
                codecId,
                terminatedCodecId,
                alignment,
                elementSize,
                rootArrayShape,
                rootStorageSize,
                false,
                null,
                null,
                0,
                0,
                this.IsLittleEndian);
            rootFields.Add(declaration, compiledRoot);
        }

        var publishedSymbols = new HashSet<CompiledTypeSymbol>(ReferenceEqualityComparer.Instance);
        foreach (CompiledTypeSymbol symbol in compositeSymbols.Values.Concat(enumSymbols.Values))
        {
            if (!publishedSymbols.Add(symbol))
            {
                continue;
            }

            if (!symbol.IsBound)
            {
                throw new CStructLayoutException("Compiled type symbol was not bound: " + symbol.Name);
            }

            if (symbol.Definition is CompiledCompositeType composite)
            {
                composite.CompleteConditionalScope();
            }

            symbol.Freeze();
        }

        this.MarkReferencedLayoutVariables(publishedSymbols, rootFields);

        return new CompiledLayoutModel(
            this.cStructElements.ToImmutableDictionary(StringComparer.Ordinal),
            this.cStructElements.ToImmutableArray(),
            namedTypes.ToImmutable(),
            compositeSymbols.ToImmutableDictionary(ReferenceEqualityComparer.Instance),
            rootFields.ToImmutable());
    }

    /// <summary>Predeclares exact composite identities so recursive pointers can refer to a symbol before it is bound.</summary>
    private void DiscoverCompositeSymbols(
        Struct strct,
        Dictionary<Struct, CompiledTypeSymbol> compositeSymbols)
    {
        if (compositeSymbols.ContainsKey(strct))
        {
            return;
        }

        compositeSymbols.Add(
            strct,
            CompiledTypeSymbol.PredeclareComposite(strct));
        foreach (Field field in strct.Fields)
        {
            if (field is Struct nested)
            {
                this.DiscoverCompositeSymbols(nested, compositeSymbols);
            }
        }
    }

    /// <summary>
    ///     Resolves a field's declared type and, when the spelling is unknown, reports it with the field, the
    ///     containing declaration, and the most likely cause: a multi-word spelling whose first word is itself a
    ///     type is almost always a missing <c>;</c> between two declarators.
    /// </summary>
    private CompiledTypeReference ResolveFieldTypeReference(
        Field field,
        Struct owner,
        ImmutableDictionary<string, CompiledTypeReference>.Builder namedTypes,
        IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols,
        HashSet<string> resolvingAliases)
    {
        try
        {
            return this.ResolveCompiledTypeReference(field.Type.Name, namedTypes, compositeSymbols, resolvingAliases);
        }
        catch (CStructLayoutException exception) when (exception.SourceOffset < 0 && exception.Message.StartsWith("Unknown field type: ", StringComparison.Ordinal))
        {
            string spelling = field.Type.Name;
            string where = owner.Name.Name.Length == 0 ? "an anonymous " + (owner.IsUnion ? "union" : "struct") : (owner.IsUnion ? "union '" : "struct '") + owner.Name.Name + "'";
            string hint = string.Empty;
            int space = spelling.IndexOf(' ');
            if (space > 0)
            {
                string firstWord = spelling[..space];
                if (this.catalog.IsKnownName(firstWord) || namedTypes.ContainsKey(firstWord) || this.CStructElements.ContainsKey(firstWord))
                {
                    int secondSpace = spelling.IndexOf(' ', space + 1);
                    string secondWord = secondSpace > 0 ? spelling[(space + 1)..secondSpace] : spelling[(space + 1)..];
                    hint = $"; a ';' may be missing after '{secondWord}'";
                }
            }

            throw new CStructLayoutException(
                $"Unknown type '{spelling}' for field '{field.Name.Name}' in {where}{hint}.",
                exception)
            {
                SourceOffset = field.Type.SourceOffset,
            };
        }
    }

    /// <summary>Resolves one exported or primitive name once, accumulating pointer depth across typedef chains.</summary>
    private CompiledTypeReference ResolveCompiledTypeReference(
        string name,
        ImmutableDictionary<string, CompiledTypeReference>.Builder namedTypes,
        IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols,
        HashSet<string> resolvingAliases)
    {
        if (namedTypes.TryGetValue(name, out CompiledTypeReference known))
        {
            return known;
        }

        if (!this.CStructElements.TryGetValue(name, out CStructElement? declaration) ||
            declaration is not Typedef alias)
        {
            // `size_t` and its relatives are as wide as this layout's pointers: resolved here, on use, rather than
            // seeded into every layout's type table (a dozen immutable-dictionary mutations per compile). A layout
            // declaration of the same name was found above and wins.
            if (declaration is null && PrimitiveSpellings.PointerSizedIsUnsigned.TryGetValue(name, out bool pointerUnsigned))
            {
                return namedTypes[PrimitiveSpellings.PointerSizedCanonical(pointerUnsigned, this.PointerSize)];
            }

            throw new CStructLayoutException("Unknown field type: " + name);
        }

        if (!resolvingAliases.Add(name))
        {
            throw new CStructLayoutException("Circular typedef dependency detected at: " + name);
        }

        try
        {
            CompiledTypeReference resolved;
            if (alias.Struct is not null)
            {
                resolved = new CompiledTypeReference(
                    compositeSymbols[alias.Struct],
                    0,
                    name);
            }
            else
            {
                CompiledTypeReference target = this.ResolveCompiledTypeReference(
                    alias.Type.Name,
                    namedTypes,
                    compositeSymbols,
                    resolvingAliases);
                CheckTypeKeyword(alias.TypeKeywordHint, target, $"Typedef '{name}'", alias.Type);

                resolved = new CompiledTypeReference(
                    target.Symbol,
                    checked(target.PointerDepth + alias.Type.PointerDepth),
                    target.TerminalName);
            }

            namedTypes.Add(name, resolved);
            return resolved;
        }
        finally
        {
            resolvingAliases.Remove(name);
        }
    }

    /// <summary>
    ///     Replaces every <c>sizeof(T)</c> and <c>offsetof(T, f)</c> in an expression with its literal value. T may be
    ///     a primitive, typedef, enum, or composite (compiled on demand, so it must be complete and fixed-size); a
    ///     pointer type has the pointer width.
    /// </summary>
    private Expr FoldCalls(
        Expr expression,
        string fieldName,
        IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols,
        ImmutableDictionary<string, CompiledTypeReference>.Builder namedTypes,
        HashSet<string> resolvingAliases,
        HashSet<Struct> compiling,
        CompiledSizeQueries sizeQueries)
    {
        switch (expression)
        {
        case Call call:
            {
                if (call.Expr is not Identifier { Name: "sizeof" or "offsetof", } function)
                {
                    throw new CStructLayoutException(
                        $"Only sizeof(type) and offsetof(type, field) are supported as expression calls: {fieldName}");
                }

                bool isSizeof = function.Name == "sizeof";
                if (call.Arguments.Length != (isSizeof ? 1 : 2) || call.Arguments.Any(argument => argument is not Identifier))
                {
                    throw new CStructLayoutException(
                        $"{function.Name} expects {(isSizeof ? "one type name" : "a type name and a field name")}: {fieldName}");
                }

                var typeName = (Identifier)call.Arguments[0];
                CompiledTypeReference target = this.ResolveCompiledTypeReference(typeName.Name, namedTypes, compositeSymbols, resolvingAliases);
                int totalPointerDepth = target.PointerDepth + typeName.PointerDepth;
                if (isSizeof)
                {
                    if (totalPointerDepth > 0)
                    {
                        return new Literal(this.PointerSize);
                    }

                    if (target.Symbol.Declaration is Struct sized)
                    {
                        _ = this.CompileComposite(sized, compositeSymbols, namedTypes, resolvingAliases, compiling, sizeQueries);
                        return new Literal(
                            sizeQueries.GetCompiledStructSizeInBytes(sizeQueries.GetCompiledComposite(sized), this.staticLayoutVariables, true));
                    }

                    return new Literal(
                        target.Symbol.FixedSize ??
                        throw new CStructLayoutException($"sizeof({typeName.Name}) has no fixed size: {fieldName}"));
                }

                if (totalPointerDepth > 0 || target.Symbol.Declaration is not Struct composite)
                {
                    throw new CStructLayoutException($"offsetof needs a struct or union type: {fieldName}");
                }

                CompiledCompositeType compiled = this.CompileComposite(composite, compositeSymbols, namedTypes, resolvingAliases, compiling, sizeQueries);
                string memberName = ((Identifier)call.Arguments[1]).Name;
                CompiledField? member = compiled.Fields.FirstOrDefault(item => item.Declaration.Name.Name == memberName);
                if (member is null)
                {
                    throw new CStructLayoutException($"offsetof: '{composite.Name.Name}' has no field named '{memberName}': {fieldName}");
                }

                return new Literal(
                    member.FixedOffset ??
                    throw new CStructLayoutException($"offsetof({composite.Name.Name}, {memberName}) is not statically placed: {fieldName}"));
            }

        case UnaryOp unary:
            return new UnaryOp(unary.Type, this.FoldCalls(unary.Expr, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiling, sizeQueries));
        case BinaryOp binary:
            return new BinaryOp(
                binary.Type,
                this.FoldCalls(binary.Left, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiling, sizeQueries),
                this.FoldCalls(binary.Right, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiling, sizeQueries));
        case ConditionalExpr conditional:
            return new ConditionalExpr(
                this.FoldCalls(conditional.Condition, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiling, sizeQueries),
                this.FoldCalls(conditional.WhenTrue, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiling, sizeQueries),
                this.FoldCalls(conditional.WhenFalse, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiling, sizeQueries));
        default:
            return expression;
        }
    }

    /// <summary>Compiles one composite's fields, fixed placements, bit slices, and size strategy exactly once.</summary>
    private CompiledCompositeType CompileComposite(
        Struct strct,
        IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols,
        ImmutableDictionary<string, CompiledTypeReference>.Builder namedTypes,
        HashSet<string> resolvingAliases,
        HashSet<Struct> compiling,
        CompiledSizeQueries sizeQueries)
    {
        CompiledTypeSymbol symbol = compositeSymbols[strct];
        if (symbol.Definition is CompiledCompositeType known)
        {
            return known;
        }

        if (!compiling.Add(strct))
        {
            throw new CStructLayoutException(
                "By-value recursive struct declarations are not supported: " + strct.Name.Name)
            {
                SourceOffset = strct.Name.SourceOffset,
            };
        }

        try
        {
            int? compositeAlignmentOverride = null;
            if (strct.CompositeAlignmentOverrideExpression is not null)
            {
                int explicitCompositeAlignment = this.layoutExpressionEvaluator.Evaluate(
                    strct.CompositeAlignmentOverrideExpression,
                    this.staticLayoutVariables,
                    "alignment override for " + strct.Name.Name);
                compositeAlignmentOverride = LayoutMath.ValidateExplicitAlignment(explicitCompositeAlignment, strct.Name.Name);
            }

            var fields = ImmutableArray.CreateBuilder<CompiledField>(strct.Fields.Count);
            foreach (Field field in strct.Fields)
            {
                CompiledTypeReference type = field is Struct inlineStruct
                                                 ? new CompiledTypeReference(
                                                     compositeSymbols[inlineStruct],
                                                     0,
                                                     inlineStruct.Name.Name)
                                                 : this.ResolveFieldTypeReference(field, strct, namedTypes, compositeSymbols, resolvingAliases);
                CheckTypeKeyword(field.TypeKeywordHint, type, $"Field '{field.Name.Name}'", field.Type);

                int pointerDepth = checked(field.PointerDepth + type.PointerDepth);
                if (pointerDepth == 0 && type.TerminalName == "void")
                {
                    throw new CStructLayoutException(
                        "void has no storage of its own; declare a pointer to it: " + field.Name.Name);
                }

                if (pointerDepth == 0 && type.Symbol.Declaration is Struct nested)
                {
                    if (compiling.Contains(nested))
                    {
                        throw new CStructLayoutException(
                            "By-value recursive struct declarations are not supported: " + nested.Name.Name)
                        {
                            SourceOffset = field.Name.SourceOffset,
                        };
                    }

                    _ = this.CompileComposite(
                        nested,
                        compositeSymbols,
                        namedTypes,
                        resolvingAliases,
                        compiling,
                        sizeQueries);
                }

                IReadOnlyList<Expr> arrayCount = field.ArrayCount;
                if (arrayCount.Count > 0 && arrayCount.Any(ExpressionEvaluator.ContainsCall))
                {
                    // sizeof(T) / offsetof(T, f) fold to literals now that T can be compiled on demand.
                    var folded = new Expr[arrayCount.Count];
                    for (int index = 0; index < folded.Length; index++)
                    {
                        folded[index] = ReferenceEquals(arrayCount[index], Field.UnknownArraysize)
                                            ? arrayCount[index]
                                            : this.FoldCalls(arrayCount[index], field.Name.Name, compositeSymbols, namedTypes, resolvingAliases, compiling, sizeQueries);
                        if (!ReferenceEquals(folded[index], Field.UnknownArraysize))
                        {
                            this.expressionEvaluator.Compile(folded[index]);
                        }
                    }

                    arrayCount = folded;
                }

                var effectiveField = new Field(
                    new Identifier(type.TerminalName),
                    field.Name,
                    arrayCount,
                    field.BitSize,
                    pointerDepth,
                    hasBitfieldDeclarator: field.HasBitfieldDeclarator);

                // An unsized dimension can only ever be the sole entry of a one-dimensional
                // ArrayCount list - the grammar already rejects it as an inner dimension of a multidimensional
                // field, so a still-empty check here is simply never true for N >= 2.
                bool isUnsizedArray = effectiveField.ArrayCount.Count == 1 &&
                                       ReferenceEquals(effectiveField.ArrayCount[0], Field.UnknownArraysize);
                if (BoundedTextCodec.IsType(effectiveField.Type.Name) && effectiveField.ArrayCount.Count > 1)
                {
                    throw new CStructLayoutException(
                        "Encoded text buffers support one byte-length dimension; use an array of structs for multiple strings: " + field.Name.Name);
                }

                bool isUnsizedCharacterArray = isUnsizedArray && CharacterFieldTypes.IsCharArrayField(effectiveField);

                int codecId = GetCompiledCodecId(type.Symbol);
                int terminatedCodecId = PrimitiveCatalog.NoCodec;
                if (isUnsizedCharacterArray || (pointerDepth > 0 && CharacterFieldTypes.IsStringPointerType(effectiveField.Type)))
                {
                    terminatedCodecId = this.catalog.CodecIdOf(CharacterFieldTypes.GetStringPointerHandlerKey(effectiveField.Type));
                }

                int alignment = pointerDepth > 0 ? this.PointerSize : type.Symbol.Alignment;
                if (field.AlignmentOverrideExpression is not null)
                {
                    int explicitAlignment = this.layoutExpressionEvaluator.Evaluate(
                        field.AlignmentOverrideExpression,
                        this.staticLayoutVariables,
                        "alignment override for " + field.Name.Name);
                    alignment = LayoutMath.ValidateExplicitAlignment(explicitAlignment, field.Name.Name);
                }
                else if (compositeAlignmentOverride.HasValue)
                {
                    // The composite's own @align(N) clamps a field relying on its natural alignment, matching
                    // #pragma pack(N) semantics; a field's own explicit override always wins outright instead.
                    alignment = Math.Min(alignment, compositeAlignmentOverride.Value);
                }

                int? elementSize = pointerDepth > 0 ? this.PointerSize : type.Symbol.FixedSize;
                CompiledArrayShape arrayShape = this.CompileArrayShape(effectiveField);
                if (arrayShape.Kind is CompiledArrayKind.Terminated or CompiledArrayKind.ToEnd &&
                    (!elementSize.HasValue || (pointerDepth == 0 && BoundedTextCodec.IsType(type.Symbol.Name))))
                {
                    // The count comes from the data, so every element must have one fixed size to step by; a bounded
                    // text codec sizes its buffer by characters, not elements, so it keeps needing an explicit capacity.
                    throw new CStructLayoutException(
                        "A data-sized array needs a fixed-size, non-text element type: " + field.Name.Name);
                }

                CompiledArrayShape? pointerElements = this.CompilePointerCount(field, effectiveField, type, pointerDepth, arrayShape);

                int? storageSize = elementSize.HasValue && arrayShape.TotalFixedElementCount.HasValue
                                       ? checked(elementSize.Value * arrayShape.TotalFixedElementCount.Value)
                                       : null;
                if (field.Name.Name.Length == 0 && field.BitSize == 0 && field is not Struct &&
                    (!storageSize.HasValue || type.Symbol.Kind is CompiledTypeKind.Struct or CompiledTypeKind.Union))
                {
                    // A `_` padding field is written as zeroes without a caller value, so it needs a fixed
                    // primitive shape to know how many.
                    throw new CStructLayoutException(
                        "A padding field (`_`) needs a fixed-size primitive type in: " + strct.Name.Name);
                }

                BitfieldCodecTable.Entry? bitfieldStorage = null;
                bool zeroWidthBitfield = effectiveField.HasBitfieldDeclarator && field.BitSize == 0;
                if (field.BitSize > 0 || zeroWidthBitfield)
                {
                    try
                    {
                        // Validated against the resolved type so an alias or typedef of an integer codec is
                        // acceptable storage, exactly as in C; an enum or flag stores its bits in its backing type.
                        // A zero-width separator is validated as a one-bit field of its type: only the unit size matters.
                        Field storageField = type.Symbol.Definition is CompiledEnumType enumStorage && pointerDepth == 0
                                                 ? new Field(new Identifier(enumStorage.Underlying.TerminalName), field.Name, field.ArrayCount, Math.Max(field.BitSize, 1), 0)
                                                 : zeroWidthBitfield
                                                     ? new Field(effectiveField.Type, field.Name, field.ArrayCount, 1, 0)
                                                     : effectiveField;
                        bitfieldStorage = this.bitfieldCodecs.ValidateBitField(storageField);
                    }
                    catch (InvalidOperationException exception)
                    {
                        throw new CStructLayoutException(
                            "Invalid bitfield declaration: " + field.Name.Name,
                            exception);
                    }

                    if (field.OffsetAssertionExpression is not null)
                    {
                        throw new CStructLayoutException(
                            "An explicit offset assertion is not supported on a bitfield declarator: " + field.Name.Name);
                    }
                }

                // Like @align(N), @N is a constant: it is evaluated once, with the #define constants only.
                int? assertedOffset = field.OffsetAssertionExpression is null
                                          ? null
                                          : OffsetAssertion.Validate(
                                              this.layoutExpressionEvaluator.Evaluate(
                                                  field.OffsetAssertionExpression,
                                                  this.staticLayoutVariables,
                                                  "offset assertion for " + field.Name.Name),
                                              field.Name.Name);

                var compiledField = new CompiledField(
                    field,
                    effectiveField,
                    type,
                    codecId,
                    terminatedCodecId,
                    alignment,
                    elementSize,
                    arrayShape,
                    storageSize,
                    isUnsizedCharacterArray,
                    bitfieldStorage?.ByteSize,
                    bitfieldStorage?.IsLittleEndian,
                    null,
                    0,
                    this.IsLittleEndian)
                {
                    PointerElements = pointerElements,
                    AssertedOffset = assertedOffset,
                };
                fields.Add(compiledField);
            }

            // A SysV zero-width separator moves bits without joining the alignment computation (x86-64 psABI);
            // MSVC lets its type's unit boundary count.
            int compositeAlignment = fields.Count == 0
                                         ? 1
                                         : fields.Max(field => field.IsZeroWidthBitfield && this.BitfieldPacking == BitfieldPacking.SysV ? 1 : field.Alignment);
            ImmutableArray<CompiledField> placedFields =
                this.PlaceCompiledFields(strct, fields.ToImmutable(), compositeAlignment, sizeQueries, out int? fixedSize);
            symbol.CompleteLayout(compositeAlignment, fixedSize);
            var definition = new CompiledCompositeType(symbol, placedFields);
            symbol.Bind(definition);
            return definition;
        }
        finally
        {
            compiling.Remove(strct);
        }
    }

    /// <summary>
    ///     Calculates immutable fixed offsets and bit offsets without making runtime-sized offsets look static.
    /// </summary>
    /// <remarks>
    ///     Uses the same <see cref="PlacementCursor"/> as the runtime, from offset 0; its position becomes unknown after
    ///     the first conditional or runtime-sized field. A field with a known offset has its <c>@N</c> assertion checked
    ///     here, and a known <see cref="CompiledField.FixedOffset"/> tells the runtime cursor and generated code that
    ///     the check is done; any other asserted field is checked where an operation places it.
    /// </remarks>
    private ImmutableArray<CompiledField> PlaceCompiledFields(
        Struct strct,
        ImmutableArray<CompiledField> fields,
        int compositeAlignment,
        CompiledSizeQueries sizeQueries,
        out int? fixedSize)
    {
        var result = ImmutableArray.CreateBuilder<CompiledField>(fields.Length);
        if (strct.IsUnion)
        {
            int? largest = 0;
            foreach (CompiledField field in fields)
            {
                if (!field.FixedStorageSize.HasValue)
                {
                    try
                    {
                        _ = sizeQueries.GetCompiledFieldStorageSize(field, this.staticLayoutVariables, true);
                    }
                    catch (CStructLayoutException exception)
                    {
                        throw new CStructLayoutException(
                            $"Union member '{field.Declaration.Name.Name}' must have fixed storage.",
                            exception);
                    }
                }

                result.Add(field.WithPlacement(0, 0));
                largest = largest.HasValue && field.FixedStorageSize.HasValue
                              ? Math.Max(largest.Value, field.FixedStorageSize.Value)
                              : null;
            }

            fixedSize = largest.HasValue ? ToOffset(PlacementCursor.UnionEnd(largest.Value, compositeAlignment, this.Aligned)) : null;
            return result.ToImmutable();
        }

        MeasureBitfieldRuns(fields);
        var cursor = new PlacementCursor(0, this.Aligned, this.BitfieldPacking, this.highBitFirst);
        foreach (CompiledField field in fields)
        {
            if (field.Declaration.Condition is not null)
            {
                cursor.CompleteField(null);
                if (field.BitStorageSize.HasValue)
                {
                    throw new CStructLayoutException("Place conditional bitfields inside a named struct group.");
                }
            }

            if (field.IsZeroWidthBitfield)
            {
                // A separator has no storage; it only moves the bit position for the next bitfield.
                result.Add(field.WithPlacement(ToOffset(cursor.AdvanceToSeparator(field.BitStorageSize ?? 1, field.Alignment, field.BitRunBits)), 0));
                continue;
            }

            if (field.BitStorageSize.HasValue)
            {
                // Bit runs never span a variable-length field, so an unknown position means the run is unreachable
                // statically; the runtime cursor places it.
                (long UnitStart, int UnitSize, int BitOffset)? unit = cursor.AdvanceToBitfield(field.BitStorageSize.Value, field.Alignment, field.EffectiveField.BitSize, field.BitRunBits, field.BitStorageIsLittleEndian ?? true, field.Declaration.Name.Name);
                result.Add(unit is { } placed ? field.WithPlacement(ToOffset(placed.UnitStart), placed.BitOffset, placed.UnitSize) : field.WithPlacement(null, 0));
                continue;
            }

            int? offset = ToOffset(cursor.AdvanceToField(field.Alignment));
            if (offset is int known && field.AssertedOffset is int asserted &&
                cursor.CheckAssertedOffset(known, asserted, field.Declaration.Name.Name) is { } failure)
            {
                throw new CStructLayoutException(failure);
            }

            cursor.CompleteField(offset.HasValue && field.FixedStorageSize.HasValue
                                     ? checked(offset.Value + field.FixedStorageSize.Value)
                                     : null);
            result.Add(field.WithPlacement(offset, 0));
        }

        fixedSize = ToOffset(cursor.Finish(compositeAlignment));
        return result.ToImmutable();
    }

    /// <summary>
    ///     Compiles a pointer declarator's <c>@count(N)</c> into the one-dimensional array shape of its final target:
    ///     <c>uint8 *iv @count(iv_len)</c> points at <c>iv_len</c> consecutive <c>uint8</c> values.
    /// </summary>
    /// <param name="field">The declared field, carrying the count expression.</param>
    /// <param name="effectiveField">The field after typedef resolution, which knows the real pointer depth.</param>
    /// <param name="type">The resolved target type.</param>
    /// <param name="pointerDepth">The resolved pointer depth; a typedef such as <c>CK_BYTE_PTR</c> contributes to it.</param>
    /// <param name="arrayShape">The declarator's own array shape, which must be scalar.</param>
    /// <returns>The target array shape, or <see langword="null"/> when the declarator has no <c>@count</c>.</returns>
    /// <exception cref="CStructLayoutException">The count is on a non-pointer, an array of pointers, or a <c>void</c> target, or it is not a plain count.</exception>
    private CompiledArrayShape? CompilePointerCount(Field field, Field effectiveField, CompiledTypeReference type, int pointerDepth, CompiledArrayShape arrayShape)
    {
        if (field.PointerCountExpression is not { } count)
        {
            return null;
        }

        string name = field.Name.Name;
        if (pointerDepth == 0)
        {
            throw new CStructLayoutException("@count applies only to a pointer declarator: " + name);
        }

        if (arrayShape.Kind != CompiledArrayKind.Scalar)
        {
            throw new CStructLayoutException("@count cannot be combined with an array of pointers: " + name);
        }

        if (type.TerminalName == "void")
        {
            throw new CStructLayoutException("@count needs a typed target; a void pointer has no element type: " + name);
        }

        if (ReferenceEquals(count, Field.UnknownArraysize) || count is Identifier { Name: "EOF", })
        {
            throw new CStructLayoutException("@count needs an element count expression: " + name);
        }

        // The target is compiled like a one-dimensional array declarator `T name[N]`, so a literal count is fixed and
        // a named count is evaluated from the operation's variables when the pointer is followed.
        this.expressionEvaluator.Compile(count);
        var targetArray = new Field(effectiveField.Type, field.Name, [count,], 0, 0);
        return this.CompileSingleArrayDimension(targetArray, count);
    }

    /// <summary>Compiles one scalar, fixed, runtime-counted, or flexible array strategy.</summary>
    private CompiledArrayShape CompileArrayShape(Field field)
    {
        if (ReferenceEquals(field.ArrayCount, Field.NoArray))
        {
            return CompiledArrayShape.Scalar;
        }

        if (field.ArrayCount.Count == 1)
        {
            return this.CompileSingleArrayDimension(field, field.ArrayCount[0]);
        }

        // A multidimensional array has fixed dimensions only: a data-dependent count is supported for a
        // one-dimensional array, where the element layout does not depend on it.
        var dimensions = ImmutableArray.CreateBuilder<CompiledArrayDimension>(field.ArrayCount.Count);
        foreach (Expr dimensionExpression in field.ArrayCount)
        {
            ImmutableArray<string> dependencies =
                this.expressionEvaluator.GetDependencies(dimensionExpression).ToImmutableArray();
            if (dependencies.Length != 0)
            {
                throw new CStructLayoutException(
                    "Every dimension of a multidimensional array must be a compile-time-fixed count; a " +
                    "runtime-sized dimension is supported only in a one-dimensional array: " + field.Name.Name);
            }

            int dimensionCount = this.layoutExpressionEvaluator.Evaluate(
                dimensionExpression,
                this.staticLayoutVariables,
                "array length for " + field.Name.Name);
            dimensions.Add(new CompiledArrayDimension(dimensionExpression, dimensionCount));
        }

        ImmutableArray<CompiledArrayDimension> dimensionList = dimensions.MoveToImmutable();
        return new CompiledArrayShape(
            CompiledArrayKind.Fixed,
            ImmutableArray<string>.Empty,
            dimensionList);
    }

    /// <summary>Compiles the sole dimension of a one-dimensional array field - unchanged from the single-dimension days.</summary>
    private CompiledArrayShape CompileSingleArrayDimension(Field field, Expr dimensionExpression)
    {
        if (ReferenceEquals(dimensionExpression, Field.UnknownArraysize))
        {
            // `char name[]` is a terminated string; `T values[]` on any other type is an array terminated by an
            // all-zero element (C's flexible member has no extent of its own, dissect's convention gives it one).
            return new CompiledArrayShape(
                CharacterFieldTypes.IsCharArrayField(field) ? CompiledArrayKind.Flexible : CompiledArrayKind.Terminated,
                ImmutableArray<string>.Empty,
                ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, null)));
        }

        if (dimensionExpression is Identifier { Name: "EOF", } && !this.staticLayoutVariables.ContainsKey("EOF"))
        {
            // `T values[EOF]`: every whole element to the end of the input. A layout that defines EOF itself keeps
            // the ordinary count meaning.
            return new CompiledArrayShape(
                CompiledArrayKind.ToEnd,
                ImmutableArray<string>.Empty,
                ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, null)));
        }

        ImmutableArray<string> dependencies = this.expressionEvaluator.GetDependencies(dimensionExpression).
            OrderBy(name => name, StringComparer.Ordinal).
            ToImmutableArray();
        if (dependencies.Length != 0)
        {
            return new CompiledArrayShape(
                CompiledArrayKind.Runtime,
                dependencies,
                ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, null)));
        }

        int count = this.layoutExpressionEvaluator.Evaluate(
            dimensionExpression,
            this.staticLayoutVariables,
            "array length for " + field.Name.Name);
        return new CompiledArrayShape(
            CompiledArrayKind.Fixed,
            ImmutableArray<string>.Empty,
            ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, count)));
    }

    /// <summary>
    ///     Marks the fields whose values an expression can read back. The evaluator resolves identifiers only
    ///     through the layout's own expressions - array dimensions, conditions, switch selectors and cases, bit sizes,
    ///     <c>#define</c>s, enum values - so their identifier dependencies are the complete set of capturable names.
    ///     Placement suffixes (<c>@align(N)</c> and <c>@N</c>) are constants evaluated with the <c>#define</c>s only,
    ///     so they never read a field. A name that only non-integer fields can supply fails construction. Allocation-
    ///     conscious on purpose: the release gate budgets a small layout's compilation to the byte, so the sets are
    ///     created only when the first identifier appears and the fields are walked through their composites' arrays
    ///     rather than through LINQ.
    /// </summary>
    /// <exception cref="CStructLayoutException">An expression names a field that can only be a non-integer value.</exception>
    private void MarkReferencedLayoutVariables(
        HashSet<CompiledTypeSymbol> symbols,
        ImmutableDictionary<CStructElement, CompiledField>.Builder rootFields)
    {
        HashSet<string>? referenced = null;
        Stack<Expr>? pending = null;
        foreach (CStructElement element in this.cStructElements.Values)
        {
            CollectExpressionReferences(element, ref referenced, ref pending);
        }

        List<string>? qualifiedHeads = ExpandQualifiedReferences(referenced);

        List<CompiledField>? nonInteger = null;
        foreach (CompiledTypeSymbol symbol in symbols)
        {
            if (symbol.Definition is CompiledCompositeType composite)
            {
                foreach (CompiledField field in composite.Fields)
                {
                    MarkField(field, referenced, ref nonInteger);
                    if (qualifiedHeads is not null && field.Type.Symbol.Kind is CompiledTypeKind.Struct or CompiledTypeKind.Union &&
                        field.PointerDepth == 0 && field.Array.Kind == CompiledArrayKind.Scalar &&
                        qualifiedHeads.Contains(field.Declaration.Name.Name))
                    {
                        // `hdr.n`: the nested fields of `hdr` are published under the qualified name as well.
                        field.HasQualifiedPrefix = true;
                    }
                }
            }
        }

        foreach (CompiledField field in rootFields.Values)
        {
            MarkField(field, referenced, ref nonInteger);
        }

        // An expression can only use an integer. A name that only non-integer fields (text, arrays, structs,
        // floating-point values, ...) supply, with no definition of that name, is a mistake in the layout; a name a
        // numeric field or a definition shares stays valid, and the non-integer field makes it unusable while in effect.
        if (nonInteger is null)
        {
            return;
        }

        foreach (CompiledField field in nonInteger)
        {
            string name = field.Declaration.Name.Name;
            bool defined = this.cStructElements.TryGetValue(name, out CStructElement? element) && element is Defines;
            if (!defined && !HasIntegerField(symbols, rootFields, name))
            {
                throw new CStructLayoutException(
                    $"Field '{name}' is {field.NotANumberReason}, but a layout expression uses it; layout expressions can only use integer fields (integers, characters, bool, enums and pointers).")
                {
                    SourceOffset = field.Declaration.Name.SourceOffset,
                };
            }
        }
    }

    /// <summary>The compiled enum of a declaration, for the browser bridge's static plan description.</summary>
    internal CompiledEnumType GetCompiledEnumForInterop(Syntax.Enum declaration)
    {
        return this.compiledModelQueries.GetCompiledEnum(declaration);
    }
}
