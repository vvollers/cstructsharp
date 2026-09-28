namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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

/// <summary>The normalization stage: declarations with their constant expressions evaluated - case labels, typedef array shapes, enum values, qualified member constants, and <c>#define</c> values.</summary>
internal sealed partial class LayoutCompilation
{
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
    /// <param name="declaration">The parsed declaration, including aliases not used by a field.</param>
    /// <returns>The declaration with validated expressions and supported alias shapes.</returns>
    /// <exception cref="CStructLayoutException">An expression or pointer-to-array alias is unsupported.</exception>
    private CStructElement NormalizeDeclarationExpressions(CStructElement declaration)
    {
        try
        {
            if (declaration is Typedef { Struct: null, Type.PointerDepth: > 0, } pointerAlias)
            {
                // Validate the pointer where it is declared: following aliases only from a later member
                // can stop at this pointer and silently lose the target array's dimensions.
                var pointerField = new Field(pointerAlias.Type, pointerAlias.Name, Field.NoArray, NoneExpr.Instance, pointerAlias.Type.PointerDepth);
                _ = this.ResolveTypedefArrayShape(pointerField);
            }

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

    /// <summary>Validates every alias dimension as a nonnegative count that fits an Int32, including literal and unused aliases.</summary>
    /// <param name="typedef">The alias whose fixed dimensions are evaluated against static layout variables.</param>
    /// <returns>One validated literal count per dimension, in declaration order.</returns>
    /// <exception cref="CStructLayoutException">A dimension is negative, outside Int32, or not a valid static expression.</exception>
    private Expr[] EvaluateTypedefShape(Typedef typedef)
    {
        var dimensions = new Expr[typedef.ArrayShape.Count];
        for (int index = 0; index < dimensions.Length; index++)
        {
            Expr dimension = typedef.ArrayShape[index];
            this.expressionEvaluator.Compile(dimension);
            string context = "array length for typedef " + typedef.Name.Name;
            Int128 count = this.layoutExpressionEvaluator.Evaluate(dimension, this.staticLayoutVariables, context);
            if (count < 0)
            {
                throw new CStructLayoutException(LayoutFailures.NegativeArrayLength(typedef.Name.Name));
            }

            dimensions[index] = new Literal(LayoutExpressionEvaluator.RequireInt32(count, context, ExpressionFailureDomain.Layout));
        }

        return dimensions;
    }

    /// <summary>
    ///     Follows a field's typedef chain and combines its fixed array dimensions with the final element type.
    ///     A field declared with a pointer to an array alias is rejected because the language has no pointer-to-array storage.
    /// </summary>
    /// <param name="field">The field whose declared type may refer to fixed array aliases.</param>
    /// <returns>The final element type and combined dimensions, or null when no array alias is followed.</returns>
    /// <exception cref="CStructLayoutException">An array count is invalid or a pointer targets an array alias.</exception>
    private (Identifier Type, IReadOnlyList<Expr> Shape)? ResolveTypedefArrayShape(Field field)
    {
        string name = field.Type.Name;
        List<Expr>? shape = null;

        // A valid chain visits each declaration at most once. The later compiled-type pass diagnoses cycles;
        // this declaration-count bound prevents cycling here without cutting off a longer valid chain.
        int remainingDeclarations = this.cStructElements.Count;
        while (remainingDeclarations-- > 0 &&
               this.cStructElements.TryGetValue(name, out CStructElement? element) && element is Typedef { Struct: null, } alias)
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

    /// <summary>Finds the complete definition closure that an enum may evaluate outside the 128-bit domain.</summary>
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

    /// <summary>Compiles a group's selector and evaluates its case labels once, preserving identity across all its arms.</summary>
    private ConditionalGroup NormalizeConditionalGroup(ConditionalGroup group, Dictionary<Expr, Expr>? constants, Dictionary<ConditionalGroup, ConditionalGroup> normalizedGroups)
    {
        if (normalizedGroups.TryGetValue(group, out ConditionalGroup? existing))
        {
            return existing;
        }

        Expr selector = NormalizeCaseConstants(group.Selector, constants)!;
        this.expressionEvaluator.Compile(selector);
        var normalized = new ConditionalGroup(selector, group.CaseLabels?.Select(label => constants![label]).ToArray());
        normalizedGroups.Add(group, normalized);
        return normalized;
    }

    /// <summary>Rebuilds a composite with precompiled array expressions and statically evaluated bit widths.</summary>
    private Struct NormalizeStructExpressions(Struct strct, Dictionary<Expr, Expr>? inheritedCaseConstants = null, Dictionary<ConditionalGroup, ConditionalGroup>? normalizedGroups = null)
    {
        if (normalizedGroups is null && (strct.IsConditional || strct.Groups.Length > 0))
        {
            normalizedGroups = new Dictionary<ConditionalGroup, ConditionalGroup>();
        }

        // Every switch's labels are evaluated here, an empty arm's too, so a duplicate or non-constant label fails
        // compilation whatever the data selects. A nested body inherits the constants: it may sit in an outer arm.
        Dictionary<Expr, Expr>? caseConstants = inheritedCaseConstants;
        bool copiedConstants = false;
        var fields = new List<Field>(strct.Fields.Count);
        foreach (ConditionalGroup group in strct.Groups)
        {
            if (group.CaseLabels is null)
            {
                continue;
            }

            if (!copiedConstants)
            {
                caseConstants = inheritedCaseConstants is null
                    ? new Dictionary<Expr, Expr>(ReferenceEqualityComparer.Instance)
                    : new Dictionary<Expr, Expr>(inheritedCaseConstants, ReferenceEqualityComparer.Instance);
                copiedConstants = true;
            }

            // Case labels use the whole expression domain, so a label such as 0xFFFFFFFFFFFFFFFF matches a uint64 tag.
            var values = new HashSet<Int128>();
            foreach (Expr tag in group.CaseLabels)
            {
                Int128 value = this.layoutExpressionEvaluator.Evaluate(
                    tag, this.staticLayoutVariables, "switch case constant");
                if (!values.Add(value))
                {
                    throw new CStructLayoutException("Duplicate switch case value: " + value.ToString(CultureInfo.InvariantCulture));
                }

                caseConstants!.Add(tag, new Literal(value));
            }
        }

        foreach (Field field in strct.Fields)
        {
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
                    string context = "array length for " + field.Name.Name;
                    Int128 count = this.layoutExpressionEvaluator.Evaluate(dimension, this.staticLayoutVariables, context);
                    if (count < 0)
                    {
                        throw new CStructLayoutException(LayoutFailures.NegativeArrayLength(field.Name.Name));
                    }

                    _ = LayoutExpressionEvaluator.RequireInt32(count, context, ExpressionFailureDomain.Layout);
                }
            }

            int bitSize = 0;
            if (!ReferenceEquals(field.BitSizeExpression, NoneExpr.Instance))
            {
                string context = "bitfield width for " + field.Name.Name;
                Int128 width = this.layoutExpressionEvaluator.Evaluate(field.BitSizeExpression, this.staticLayoutVariables, context);
                if (width < 0)
                {
                    throw new CStructLayoutException("Bitfield width cannot be negative: " + field.Name.Name);
                }

                bitSize = LayoutExpressionEvaluator.RequireInt32(width, context, ExpressionFailureDomain.Layout);
                if (bitSize == 0 && field.Name.Name.Length > 0)
                {
                    throw new CStructLayoutException(
                        "Bitfield width must be greater than zero (only an unnamed ': 0' separator may be zero): " + field.Name.Name);
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
                    Field.Width(bitSize, field.HasBitfieldDeclarator),
                    field.PointerDepth,
                    field.TypeKeywordHint,
                    field.AlignmentOverrideExpression,
                    field.OffsetAssertionExpression)
                {
                    PointerCountExpression = field.PointerCountExpression,
                    BranchConditions = field.BranchConditions.Count == 0 ? Array.Empty<ConditionalBranch>() : field.BranchConditions.Select(item =>
                        new ConditionalBranch(this.NormalizeConditionalGroup(item.Group, caseConstants, normalizedGroups!), item.Arm)).ToArray(),
                });
        }

        return new Struct(strct.Name, fields.ToImmutableList(), strct.IsUnion, strct.CompositeAlignmentOverrideExpression)
        {
            Groups = strct.Groups.Length == 0 ? strct.Groups : strct.Groups.Select(group => this.NormalizeConditionalGroup(group, caseConstants, normalizedGroups!)).ToImmutableArray(),
            BranchConditions = strct.BranchConditions.Count == 0 ? Array.Empty<ConditionalBranch>() : strct.BranchConditions.Select(item =>
                new ConditionalBranch(this.NormalizeConditionalGroup(item.Group, caseConstants, normalizedGroups!), item.Arm)).ToArray(),
        };
    }
}
