namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>Builds and queries the immutable compiled layout model from validated parsed declarations.</summary>
public partial class CStruct
{
    private readonly CompiledLayoutModel compiledLayout;

    /// <summary>Gets the immutable internal model for invariant tests and later compiled-executor migrations.</summary>
    internal CompiledLayoutModel CompiledModel => this.compiledLayout;

    /// <summary>The operation-variable resolver, exposed for tests that exercise supplied-variable resolution directly.</summary>
    internal LayoutVariableResolver CompiledLayoutVariables => this.layoutVariableResolver;

    private static bool MayCaptureText(CompiledField field)
    {
        if (field.PointerDepth > 0 || field.Type.Symbol.Kind is CompiledTypeKind.Struct or CompiledTypeKind.Union or CompiledTypeKind.Enum)
        {
            return false;
        }

        PrimitiveCodec codec = field.Codec;
        return !(codec.IsFixedWidthNumeric || codec.IsLeb128 || codec.IsFixedPoint);
    }

    /// <summary>Marks one field; returns whether it is referenced and can hold text (which widens capture to every field).</summary>
    private static bool MarkField(CompiledField field, HashSet<string>? referenced)
    {
        bool isReferenced = referenced is not null && referenced.Contains(field.Declaration.Name.Name);
        field.CapturesLayoutVariable = isReferenced;
        return isReferenced && MayCaptureText(field);
    }

    private static void CollectExpressionReferences(CStructElement element, ref HashSet<string>? referenced, ref Stack<Expr>? pending)
    {
        switch (element)
        {
        case Struct strct:
            AddReferences(strct.CompositeAlignmentOverrideExpression, ref referenced, ref pending);
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

    private static void CollectFieldReferences(Field field, ref HashSet<string>? referenced, ref Stack<Expr>? pending)
    {
        IReadOnlyList<Expr> dimensions = field.ArrayCount;
        for (int index = 0; index < dimensions.Count; index++)
        {
            AddReferences(dimensions[index], ref referenced, ref pending);
        }

        AddReferences(field.BitSizeExpression, ref referenced, ref pending);
        AddReferences(field.Condition, ref referenced, ref pending);
        AddReferences(field.AlignmentOverrideExpression, ref referenced, ref pending);
        AddReferences(field.OffsetAssertionExpression, ref referenced, ref pending);
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
        var namedTypes = this.primitiveRegistry.Symbols.ToBuilder();

        foreach (KeyValuePair<string, CompiledTypeReference> custom in this.customSymbols)
        {
            namedTypes[custom.Key] = custom.Value;
        }

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
                        null,
                        null));
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

        var compiledFields = ImmutableDictionary.CreateBuilder<Field, CompiledField>(
            ReferenceEqualityComparer.Instance);
        var compilingComposites = new HashSet<Struct>(ReferenceEqualityComparer.Instance);
        foreach (Struct declaration in compositeSymbols.Keys)
        {
            this.CompileComposite(
                declaration,
                compositeSymbols,
                namedTypes,
                resolvingAliases,
                compiledFields,
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
            Func<Stream, object>? reader = this.GetCompiledReader(type.Symbol);
            Action<Stream, object>? writer = this.GetCompiledWriter(type.Symbol);
            Func<Stream, object>? terminatedReader = null;
            Action<Stream, object>? terminatedWriter = null;
            if (type.PointerDepth > 0 && CharacterFieldTypes.IsStringPointerType(field.Type))
            {
                string handler = CharacterFieldTypes.GetStringPointerHandlerKey(field.Type);
                terminatedReader = this.fieldHandlers[handler];
                terminatedWriter = this.writeHandlers[handler];
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
                reader,
                writer,
                terminatedReader,
                terminatedWriter,
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
            compiledFields.ToImmutable(),
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

    /// <summary>Resolves one exported or primitive name once, accumulating pointer depth across typedef chains.</summary>
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
                if (this.fieldHandlers.ContainsKey(firstWord) || namedTypes.ContainsKey(firstWord) || this.CStructElements.ContainsKey(firstWord))
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
                if (alias.TypeKeywordHint is not null)
                {
                    string actualKind = target.Symbol.Kind.ToString().ToLowerInvariant();
                    if (!string.Equals(alias.TypeKeywordHint, actualKind, StringComparison.Ordinal))
                    {
                        throw new CStructLayoutException(
                            $"Typedef '{name}' declared as '{alias.TypeKeywordHint}' but '{alias.Type.Name}' is a {actualKind}.");
                    }
                }

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
        ImmutableDictionary<Field, CompiledField>.Builder compiledFields,
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
                        _ = this.CompileComposite(sized, compositeSymbols, namedTypes, resolvingAliases, compiledFields, compiling, sizeQueries);
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

                CompiledCompositeType compiled = this.CompileComposite(composite, compositeSymbols, namedTypes, resolvingAliases, compiledFields, compiling, sizeQueries);
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
            return new UnaryOp(unary.Type, this.FoldCalls(unary.Expr, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiledFields, compiling, sizeQueries));
        case BinaryOp binary:
            return new BinaryOp(
                binary.Type,
                this.FoldCalls(binary.Left, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiledFields, compiling, sizeQueries),
                this.FoldCalls(binary.Right, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiledFields, compiling, sizeQueries));
        case ConditionalExpr conditional:
            return new ConditionalExpr(
                this.FoldCalls(conditional.Condition, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiledFields, compiling, sizeQueries),
                this.FoldCalls(conditional.WhenTrue, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiledFields, compiling, sizeQueries),
                this.FoldCalls(conditional.WhenFalse, fieldName, compositeSymbols, namedTypes, resolvingAliases, compiledFields, compiling, sizeQueries));
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
        ImmutableDictionary<Field, CompiledField>.Builder compiledFields,
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
                if (field.TypeKeywordHint is not null)
                {
                    string actualKind = type.Symbol.Kind.ToString().ToLowerInvariant();
                    if (!string.Equals(field.TypeKeywordHint, actualKind, StringComparison.Ordinal))
                    {
                        throw new CStructLayoutException(
                            $"Field '{field.Name.Name}' declared as '{field.TypeKeywordHint}' but '{field.Type.Name}' is a {actualKind}.")
                        {
                            SourceOffset = field.Type.SourceOffset,
                        };
                    }
                }

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
                        compiledFields,
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
                                            : this.FoldCalls(arrayCount[index], field.Name.Name, compositeSymbols, namedTypes, resolvingAliases, compiledFields, compiling, sizeQueries);
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

                // An unsized dimension (LANG-05 decision 6) can only ever be the sole entry of a one-dimensional
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

                Func<Stream, object>? reader = this.GetCompiledReader(type.Symbol);
                Action<Stream, object>? writer = this.GetCompiledWriter(type.Symbol);
                Func<Stream, object>? terminatedReader = null;
                Action<Stream, object>? terminatedWriter = null;
                if (isUnsizedCharacterArray || (pointerDepth > 0 && CharacterFieldTypes.IsStringPointerType(effectiveField.Type)))
                {
                    string handler = CharacterFieldTypes.GetStringPointerHandlerKey(effectiveField.Type);
                    terminatedReader = this.fieldHandlers[handler];
                    terminatedWriter = this.writeHandlers[handler];
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

                var compiledField = new CompiledField(
                    field,
                    effectiveField,
                    type,
                    reader,
                    writer,
                    terminatedReader,
                    terminatedWriter,
                    alignment,
                    elementSize,
                    arrayShape,
                    storageSize,
                    isUnsizedCharacterArray,
                    bitfieldStorage?.ByteSize,
                    bitfieldStorage?.IsLittleEndian,
                    null,
                    0,
                    this.IsLittleEndian);
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
            foreach (CompiledField field in placedFields)
            {
                compiledFields.Add(field.Declaration, field);
            }

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
    ///     Deliberately not consolidated onto <see cref="CompositeFieldPlacementCursor"/> (see ADR-013 and the
    ///     placement-arithmetic consolidation this session): this method runs once at compile time, before any
    ///     runtime <c>variables</c> exist, and needs a nullable, one-way "permanently unknown from here on" position
    ///     the moment any field's size is statically undeterminable - the cursor's non-nullable <c>long</c> position
    ///     has no such state, since every other consumer only ever drives it when a concrete field end is always
    ///     computable (a live stream, or pure-math storage sizes). Its output, <see cref="CompiledField.FixedOffset"/>,
    ///     is the "already checked statically at construction time" signal
    ///     <see cref="CStruct.ValidateOffsetAssertionAtRuntime"/> reads to skip re-validating a
    ///     field's LANG-15 <c>@N</c> offset assertion whose placement was already known here - a field left with
    ///     no <see cref="CompiledField.FixedOffset"/> (because its own or a preceding sibling's size is
    ///     runtime-dependent) is instead checked the first time any operation actually reaches it.
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

            if (largest.HasValue && this.Aligned)
            {
                largest = LayoutMath.AlignUp(largest.Value, compositeAlignment);
            }

            fixedSize = largest;
            return result.ToImmutable();
        }

        MeasureBitfieldRuns(fields);
        int? current = 0;
        var bitfields = new BitfieldPlacement(this.BitfieldPacking, this.Aligned, this.highBitFirst);
        foreach (CompiledField field in fields)
        {
            if (field.Declaration.Condition is not null)
            {
                current = null;
                if (field.BitStorageSize.HasValue)
                {
                    throw new CStructLayoutException("Place conditional bitfields inside a named struct group.");
                }
            }

            if (field.IsZeroWidthBitfield)
            {
                // A separator has no storage; it only moves the bit position for the next bitfield.
                if (current.HasValue)
                {
                    bitfields.PlaceSeparator(current.Value, field.BitStorageSize ?? 1, field.Alignment, field.BitRunBits);
                    current = checked((int)bitfields.RunEnd);
                }

                result.Add(field.WithPlacement(current, 0));
                continue;
            }

            if (field.BitStorageSize.HasValue)
            {
                if (!current.HasValue)
                {
                    // Bit runs never span a variable-length field, so an unknown position means the run is
                    // unreachable statically; the runtime cursor places it.
                    result.Add(field.WithPlacement(null, 0));
                    continue;
                }

                (long unitStart, int unitSize, int bitOffset) = bitfields.Place(current.Value, field.BitStorageSize.Value, field.Alignment, field.EffectiveField.BitSize, field.BitRunBits, field.BitStorageIsLittleEndian ?? true, field.Declaration.Name.Name);
                current = checked((int)bitfields.RunEnd);
                result.Add(field.WithPlacement(checked((int)unitStart), bitOffset, unitSize));
                continue;
            }

            bitfields.Close();
            if (current.HasValue && this.Aligned)
            {
                current = LayoutMath.AlignUp(current.Value, field.Alignment);
            }

            int? offset = current;
            if (offset.HasValue && field.Declaration.OffsetAssertionExpression is not null)
            {
                int asserted = this.layoutExpressionEvaluator.Evaluate(
                    field.Declaration.OffsetAssertionExpression,
                    this.staticLayoutVariables,
                    "offset assertion for " + field.Declaration.Name.Name);
                if (asserted < 0)
                {
                    throw new CStructLayoutException(
                        "Explicit offset assertion must be non-negative: " + field.Declaration.Name.Name + " = " + asserted);
                }

                if (asserted != offset.Value)
                {
                    throw new CStructLayoutException(
                        $"Field '{field.Declaration.Name.Name}' asserts offset {asserted} but computed offset is {offset.Value}.");
                }
            }

            current = current.HasValue && field.FixedStorageSize.HasValue
                          ? checked(current.Value + field.FixedStorageSize.Value)
                          : null;
            result.Add(field.WithPlacement(offset, 0));
        }

        if (current.HasValue && this.Aligned)
        {
            current = LayoutMath.AlignUp(current.Value, compositeAlignment);
        }

        fixedSize = current;
        return result.ToImmutable();
    }

    /// <summary>
    ///     Records on every bitfield the bit length of its run of adjacent bitfields, measured with the packed SysV
    ///     rule from the run's first bit: contiguous widths, a zero-width separator rounding up to its type's size.
    ///     The value depends only on the run's declarations, so the runtime cursor can clamp packed units without
    ///     looking ahead.
    /// </summary>
    private static void MeasureBitfieldRuns(ImmutableArray<CompiledField> fields)
    {
        int index = 0;
        while (index < fields.Length)
        {
            if (!fields[index].BitStorageSize.HasValue || fields[index].Declaration.Condition is not null)
            {
                index++;
                continue;
            }

            int start = index;
            long bits = 0;
            while (index < fields.Length && fields[index].BitStorageSize.HasValue && fields[index].Declaration.Condition is null)
            {
                CompiledField field = fields[index];
                bits = field.IsZeroWidthBitfield
                           ? LayoutMath.AlignUp(bits, field.BitStorageSize!.Value * 8L)
                           : bits + field.EffectiveField.BitSize;
                index++;
            }

            for (int member = start; member < index; member++)
            {
                fields[member].BitRunBits = checked((int)bits);
            }
        }
    }

    /// <summary>Returns a primitive reader directly or the compiled underlying reader for an enum.</summary>
    private Func<Stream, object>? GetCompiledReader(CompiledTypeSymbol symbol)
    {
        return symbol.Kind switch
        {
            CompiledTypeKind.Primitive => symbol.Reader,
            CompiledTypeKind.Enum when symbol.Definition is CompiledEnumType enm => enm.Underlying.Symbol.Reader,
            _ => null,
        };
    }

    /// <summary>Returns a primitive writer directly or the compiled underlying writer for an enum.</summary>
    private Action<Stream, object>? GetCompiledWriter(CompiledTypeSymbol symbol)
    {
        return symbol.Kind switch
        {
            CompiledTypeKind.Primitive => symbol.Writer,
            CompiledTypeKind.Enum when symbol.Definition is CompiledEnumType enm => enm.Underlying.Symbol.Writer,
            _ => null,
        };
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

        // A multidimensional array (LANG-05, fixed-dimensions-only first slice, ADR-016 decision 9): every
        // dimension must be a compile-time-fixed count - a runtime-sized outermost dimension is a deliberately
        // separate, smaller follow-on this slice does not implement, and an inner dimension can never be
        // runtime-sized even once that follow-on lands (ADR-016 decision 2).
        var dimensions = ImmutableArray.CreateBuilder<CompiledArrayDimension>(field.ArrayCount.Count);
        foreach (Expr dimensionExpression in field.ArrayCount)
        {
            ImmutableArray<string> dependencies =
                this.expressionEvaluator.GetDependencies(dimensionExpression).ToImmutableArray();
            if (dependencies.Length != 0)
            {
                throw new CStructLayoutException(
                    "Every dimension of a multidimensional array must be a compile-time-fixed count; a " +
                    "runtime-sized dimension is not yet supported for two or more dimensions: " + field.Name.Name);
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
            dimensionList[0].CountExpression,
            dimensionList[0].FixedCount,
            ImmutableArray<string>.Empty,
            dimensionList);
    }

    /// <summary>Compiles the sole dimension of a one-dimensional array field - unchanged from before LANG-05.</summary>
    private CompiledArrayShape CompileSingleArrayDimension(Field field, Expr dimensionExpression)
    {
        if (ReferenceEquals(dimensionExpression, Field.UnknownArraysize))
        {
            // `char name[]` is a terminated string; `T values[]` on any other type is an array terminated by an
            // all-zero element (C's flexible member has no extent of its own, dissect's convention gives it one).
            return new CompiledArrayShape(
                CharacterFieldTypes.IsCharArrayField(field) ? CompiledArrayKind.Flexible : CompiledArrayKind.Terminated,
                dimensionExpression,
                null,
                ImmutableArray<string>.Empty,
                ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, null)));
        }

        if (dimensionExpression is Identifier { Name: "EOF", } && !this.staticLayoutVariables.ContainsKey("EOF"))
        {
            // `T values[EOF]`: every whole element to the end of the input. A layout that defines EOF itself keeps
            // the ordinary count meaning.
            return new CompiledArrayShape(
                CompiledArrayKind.ToEnd,
                dimensionExpression,
                null,
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
                dimensionExpression,
                null,
                dependencies,
                ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, null)));
        }

        int count = this.layoutExpressionEvaluator.Evaluate(
            dimensionExpression,
            this.staticLayoutVariables,
            "array length for " + field.Name.Name);
        return new CompiledArrayShape(
            CompiledArrayKind.Fixed,
            dimensionExpression,
            count,
            ImmutableArray<string>.Empty,
            ImmutableArray.Create(new CompiledArrayDimension(dimensionExpression, count)));
    }

    /// <summary>
    ///     Marks the fields whose values an expression can read back (E2.6). The evaluator resolves identifiers only
    ///     through the layout's own expressions - array dimensions, conditions, switch selectors and cases, bit sizes,
    ///     alignment/offset assertions, <c>#define</c>s, enum values - so their identifier dependencies are the complete
    ///     set of capturable names. One exception makes the set unbounded: a text field is captured as
    ///     <c>Identifier(text)</c>, and evaluating it resolves the *text* as another name. If any referenced field can
    ///     hold text, every field keeps its capture. Allocation-conscious on purpose: the release gate budgets a small
    ///     layout's compilation to the byte, so the sets are created only when the first identifier appears and the
    ///     fields are walked through their composites' arrays rather than through LINQ.
    /// </summary>
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

        bool captureAll = false;
        foreach (CompiledTypeSymbol symbol in symbols)
        {
            if (symbol.Definition is CompiledCompositeType composite)
            {
                foreach (CompiledField field in composite.Fields)
                {
                    captureAll |= MarkField(field, referenced);
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
            captureAll |= MarkField(field, referenced);
        }

        if (!captureAll)
        {
            return;
        }

        foreach (CompiledTypeSymbol symbol in symbols)
        {
            if (symbol.Definition is CompiledCompositeType composite)
            {
                foreach (CompiledField field in composite.Fields)
                {
                    field.CapturesLayoutVariable = true;
                }
            }
        }

        foreach (CompiledField field in rootFields.Values)
        {
            field.CapturesLayoutVariable = true;
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

    /// <summary>The compiled enum of a declaration, for the browser bridge's static plan description (E3.9).</summary>
    internal CompiledEnumType GetCompiledEnumForInterop(Syntax.Enum declaration)
    {
        return this.compiledModelQueries.GetCompiledEnum(declaration);
    }
}
