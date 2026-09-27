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

/// <summary>The capture stage: which fields a layout expression can read, so a read or write publishes exactly those as layout variables, and the rule that such a field is an integer.</summary>
internal sealed partial class LayoutCompilation
{
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
        AddReferences(field.PointerCountExpression, ref referenced, ref pending);
        IReadOnlyList<ConditionalBranch> branches = field.BranchConditions;
        for (int index = 0; index < branches.Count; index++)
        {
            // A normalized group's case labels are literals; only its selector can name a field.
            AddReferences(branches[index].Group.Selector, ref referenced, ref pending);
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
}
