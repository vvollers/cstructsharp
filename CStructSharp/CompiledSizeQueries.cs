namespace CStructSharp;

using System;
using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>
///     Calculates compiled composite, field, and array sizes from a live composite-symbol table. Two lifetimes use
///     this type: one instance is built locally inside <c>BuildCompiledLayout</c>, as soon as every composite symbol
///     has been discovered but before any composite's fields or placement are compiled - so it can safely answer a
///     union member's fixed-storage validation before the final immutable model exists. A second, equivalent
///     instance is built right after construction from the frozen model, for every runtime traversal.
/// </summary>
internal sealed class CompiledSizeQueries
{
    private readonly bool aligned;
    private readonly IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols;
    private readonly LayoutExpressionEvaluator expressionEvaluator;

    public CompiledSizeQueries(
        IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols,
        bool aligned,
        LayoutExpressionEvaluator expressionEvaluator)
    {
        this.compositeSymbols = compositeSymbols;
        this.aligned = aligned;
        this.expressionEvaluator = expressionEvaluator;
    }

    /// <summary>Returns the immutable composite descriptor for an exact parsed struct declaration.</summary>
    public CompiledCompositeType GetCompiledComposite(Struct strct)
    {
        CompiledTypeSymbol symbol = this.compositeSymbols[strct];
        return symbol.Definition as CompiledCompositeType ??
               throw new InvalidOperationException("Composite type is not bound: " + strct.Name.Name);
    }

    /// <summary>Calculates one composite extent from compiled field/type facts and runtime count expressions only.</summary>
    public int GetCompiledStructSizeInBytes(
        CompiledCompositeType composite,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        if (composite.Fields.Length == 0)
        {
            return 0;
        }

        if (composite.Symbol.Kind == CompiledTypeKind.Union)
        {
            int largest = 0;
            foreach (CompiledField field in composite.Fields)
            {
                largest = Math.Max(
                    largest,
                    this.GetCompiledFieldStorageSize(field, variables, requireFixedSize));
            }

            return this.aligned ? LayoutMath.AlignUp(largest, composite.Symbol.Alignment) : largest;
        }

        // Drives the same cursor CStructAddressResolver/CStructReader/CStructWriter use, but sources each field's
        // extent from pure arithmetic (GetCompiledFieldStorageSize) rather than a stream - this method must stay
        // callable with no Stream/operation context, both mid-compilation and from variables-only callers.
        var cursor = new CompositeFieldPlacementCursor(0, this.aligned);
        foreach (CompiledField field in composite.Fields)
        {
            (long fieldStart, _) = cursor.AdvanceToField(field);
            if (!field.BitStorageSize.HasValue)
            {
                cursor.CompleteField(
                    checked(fieldStart + this.GetCompiledFieldStorageSize(field, variables, requireFixedSize)));
            }
        }

        return checked((int)cursor.FinishComposite(composite.Symbol.Alignment));
    }

    /// <summary>Calculates one compiled field's complete storage without resolving its parsed type name.</summary>
    public int GetCompiledFieldStorageSize(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        int elementSize = this.GetCompiledFieldElementSize(field, variables, requireFixedSize);
        int count = this.GetCompiledFieldTotalElementCount(field, variables, requireFixedSize);
        return checked(elementSize * count);
    }

    /// <summary>Calculates one compiled element footprint from its direct pointer, codec, enum, or composite target.</summary>
    public int GetCompiledFieldElementSize(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        if (field.FixedElementSize.HasValue)
        {
            return field.FixedElementSize.Value;
        }

        if (field.Type.Symbol.Declaration is Struct nested)
        {
            return this.GetCompiledStructSizeInBytes(
                this.GetCompiledComposite(nested),
                variables,
                requireFixedSize);
        }

        throw new CStructLayoutException(
            "Variable-length type has no fixed storage size: " + field.EffectiveField.Type.Name);
    }

    /// <summary>Evaluates one compiled array strategy while preserving fixed/flexible error semantics.</summary>
    public int GetCompiledArrayCount(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return 1;
        }

        if (field.Array.Kind == CompiledArrayKind.Flexible)
        {
            throw new CStructLayoutException(
                "Flexible array has no fixed storage size: " + field.EffectiveField.Name.Name);
        }

        int count;
        try
        {
            Expr expression = field.Array.CountExpression ??
                              throw new InvalidOperationException(
                                  "Compiled array strategy has no count expression: " +
                                  field.EffectiveField.Name.Name);
            count = this.expressionEvaluator.Evaluate(
                expression,
                variables,
                "array length for " + field.EffectiveField.Name.Name);
        }
        catch (Exception exception) when (requireFixedSize)
        {
            throw new CStructLayoutException(
                "Cannot calculate fixed array size for field: " + field.EffectiveField.Name.Name,
                exception);
        }

        if (count < 0)
        {
            throw new CStructLayoutException(
                "Array length cannot be negative: " + field.EffectiveField.Name.Name);
        }

        return count;
    }

    /// <summary>
    ///     Evaluates the total element count across every dimension of a (possibly multidimensional, LANG-05)
    ///     array strategy - the product of each dimension's own independently re-evaluated count, mirroring
    ///     <see cref="GetCompiledArrayCount"/>'s existing single-dimension evaluation exactly for every field this
    ///     codebase supported before LANG-05, since a 1-D field's <see cref="CompiledArrayShape.Dimensions"/> has
    ///     exactly the one entry <see cref="GetCompiledArrayCount"/> already evaluates.
    /// </summary>
    public int GetCompiledFieldTotalElementCount(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return 1;
        }

        if (field.Array.Kind == CompiledArrayKind.Flexible)
        {
            throw new CStructLayoutException(
                "Flexible array has no fixed storage size: " + field.EffectiveField.Name.Name);
        }

        int total = 1;
        foreach (CompiledArrayDimension dimension in field.Array.Dimensions)
        {
            int count;
            try
            {
                Expr expression = dimension.CountExpression ??
                                  throw new InvalidOperationException(
                                      "Compiled array dimension has no count expression: " +
                                      field.EffectiveField.Name.Name);
                count = this.expressionEvaluator.Evaluate(
                    expression,
                    variables,
                    "array length for " + field.EffectiveField.Name.Name);
            }
            catch (Exception exception) when (requireFixedSize)
            {
                throw new CStructLayoutException(
                    "Cannot calculate fixed array size for field: " + field.EffectiveField.Name.Name,
                    exception);
            }

            if (count < 0)
            {
                throw new CStructLayoutException(
                    "Array length cannot be negative: " + field.EffectiveField.Name.Name);
            }

            total = checked(total * count);
        }

        return total;
    }
}
