namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

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
    private readonly BitfieldPacking bitfieldPacking;
    private readonly bool highBitFirst;
    private readonly IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols;
    private readonly LayoutExpressionEvaluator expressionEvaluator;

    /// <summary>Creates size queries over a composite-symbol table and the layout's placement settings.</summary>
    /// <param name="compositeSymbols">
    ///     The live map from parsed struct/union declarations to their compiled symbols; read on each query, not
    ///     copied.
    /// </param>
    /// <param name="aligned">Whether fields are placed at their natural alignment (C-style padding).</param>
    /// <param name="bitfieldPacking">How adjacent bit-fields share storage units.</param>
    /// <param name="highBitFirst">Whether bit-fields are allocated from the most significant bit of their unit.</param>
    /// <param name="expressionEvaluator">The evaluator for array count and conditional-field expressions.</param>
    public CompiledSizeQueries(
        IReadOnlyDictionary<Struct, CompiledTypeSymbol> compositeSymbols,
        bool aligned,
        BitfieldPacking bitfieldPacking,
        bool highBitFirst,
        LayoutExpressionEvaluator expressionEvaluator)
    {
        this.compositeSymbols = compositeSymbols;
        this.aligned = aligned;
        this.bitfieldPacking = bitfieldPacking;
        this.highBitFirst = highBitFirst;
        this.expressionEvaluator = expressionEvaluator;
    }

    /// <summary>Returns the immutable composite descriptor for an exact parsed struct declaration.</summary>
    /// <param name="strct">The parsed struct or union declaration, matched by reference.</param>
    /// <returns>The compiled composite bound to <paramref name="strct"/>.</returns>
    /// <exception cref="InvalidOperationException">The composite definition is not yet bound.</exception>
    public CompiledCompositeType GetCompiledComposite(Struct strct)
    {
        CompiledTypeSymbol symbol = this.compositeSymbols[strct];
        return symbol.Definition as CompiledCompositeType ??
               throw new InvalidOperationException("Composite type is not bound: " + strct.Name.Name);
    }

    /// <summary>Calculates one composite extent from compiled field/type facts and runtime count expressions only.</summary>
    /// <param name="composite">The compiled struct or union to measure.</param>
    /// <param name="variables">
    ///     Values for identifiers in count expressions, such as already-read sibling fields; empty for fixed-size
    ///     queries.
    /// </param>
    /// <param name="requireFixedSize">
    ///     True when the size must be fixed by the layout alone, so evaluation failures are layout errors; false
    ///     when it runs inside a read or address operation, so failures are read errors.
    /// </param>
    /// <returns>
    ///     The composite's size in bytes including trailing padding; for a union, its largest member rounded to its
    ///     alignment; 0 when it has no fields.
    /// </returns>
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

            return checked((int)PlacementCursor.UnionEnd(largest, composite.Symbol.Alignment, this.aligned));
        }

        // Drives the same cursor CStructAddressResolver/CStructReader/CStructWriter use, but sources each field's
        // extent from pure arithmetic (GetCompiledFieldStorageSize) rather than a stream - this method must stay
        // callable with no Stream/operation context, both mid-compilation and from variables-only callers.
        var cursor = new CompositeFieldPlacementCursor(0, this.aligned, this.bitfieldPacking, this.highBitFirst);
        var selection = composite.HasDirectConditionalFields ? new ConditionalFieldSelection(this.expressionEvaluator, composite.ConditionalGroupCount, requireFixedSize ? ExpressionFailureDomain.Layout : ExpressionFailureDomain.Read) : null;
        foreach (CompiledField field in composite.Fields)
        {
            if (selection?.IsActive(field, variables) == false)
            {
                continue;
            }

            (long fieldStart, _, _) = cursor.AdvanceToField(field);
            if (!field.BitStorageSize.HasValue)
            {
                cursor.CompleteField(
                    checked(fieldStart + this.GetCompiledFieldStorageSize(field, variables, requireFixedSize)));
            }
        }

        return checked((int)cursor.FinishComposite(composite.Symbol.Alignment));
    }

    /// <summary>Calculates one compiled field's complete storage without resolving its parsed type name.</summary>
    /// <param name="field">The compiled field to measure.</param>
    /// <param name="variables">
    ///     Values for identifiers in count expressions, such as already-read sibling fields; empty for fixed-size
    ///     queries.
    /// </param>
    /// <param name="requireFixedSize">
    ///     True when the size must be fixed by the layout alone, so evaluation failures are layout errors; false
    ///     when it runs inside a read or address operation, so failures are read errors.
    /// </param>
    /// <returns>The field's storage in bytes: its element size times its total element count.</returns>
    public int GetCompiledFieldStorageSize(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        int elementSize = this.GetCompiledFieldElementSize(field, variables, requireFixedSize);
        Int128 count = this.GetCompiledFieldTotalElementCount(field, variables, requireFixedSize);

        // A storage size is an int; a larger product fails as the checked multiplication always has.
        Int128 bytes = elementSize * count;
        return bytes > int.MaxValue ? throw new OverflowException() : (int)bytes;
    }

    /// <summary>Calculates one compiled element footprint from its direct pointer, codec, enum, or composite target.</summary>
    /// <param name="field">The compiled field whose single element is measured.</param>
    /// <param name="variables">
    ///     Values for identifiers in count expressions, such as already-read sibling fields; empty for fixed-size
    ///     queries.
    /// </param>
    /// <param name="requireFixedSize">
    ///     True when the size must be fixed by the layout alone, so evaluation failures are layout errors; false
    ///     when it runs inside a read or address operation, so failures are read errors.
    /// </param>
    /// <returns>The size in bytes of one element of the field (not the whole array).</returns>
    /// <exception cref="CStructLayoutException">The element type has no fixed storage size.</exception>
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
            "Variable-length type has no fixed storage size: " + field.TypeSpelling);
    }

    /// <summary>Evaluates one compiled array strategy while preserving fixed/flexible error semantics.</summary>
    /// <param name="field">The compiled field whose outermost array count is evaluated.</param>
    /// <param name="variables">
    ///     Values for identifiers in count expressions, such as already-read sibling fields; empty for fixed-size
    ///     queries.
    /// </param>
    /// <param name="requireFixedSize">
    ///     True when the size must be fixed by the layout alone, so evaluation failures are layout errors; false
    ///     when it runs inside a read or address operation, so failures are read errors.
    /// </param>
    /// <returns>
    ///     The outermost dimension's element count, or 1 for a scalar field; non-negative, and exact in the expression
    ///     domain, so a caller's element limit reports a count read from a <c>uint64</c> field with its real value.
    /// </returns>
    /// <exception cref="CStructLayoutException">The array is flexible, read to the end, or terminated.</exception>
    public Int128 GetCompiledArrayCount(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return 1;
        }

        if (field.Array.Kind is CompiledArrayKind.Flexible or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            throw new CStructLayoutException(
                "Flexible array has no fixed storage size: " + field.Name);
        }

        Expr expression = field.Array.CountExpression ??
                          throw new InvalidOperationException(
                              "Compiled array strategy has no count expression: " + field.Name);
        return this.EvaluateDimensionCount(expression, field.Name, variables, requireFixedSize);
    }

    /// <summary>
    ///     Evaluates the total element count across every dimension of a (possibly multidimensional)
    ///     array strategy - the product of each dimension's own independently re-evaluated count. For a
    ///     one-dimensional field, whose <see cref="CompiledArrayShape.Dimensions"/> has one entry, this equals
    ///     <see cref="GetCompiledArrayCount"/>.
    /// </summary>
    /// <param name="field">The compiled field whose dimensions are multiplied.</param>
    /// <param name="variables">
    ///     Values for identifiers in count expressions, such as already-read sibling fields; empty for fixed-size
    ///     queries.
    /// </param>
    /// <param name="requireFixedSize">
    ///     True when the size must be fixed by the layout alone, so evaluation failures are layout errors; false
    ///     when it runs inside a read or address operation, so failures are read errors.
    /// </param>
    /// <returns>The product of every dimension's count in elements, or 1 for a scalar field; exact in the expression domain.</returns>
    /// <exception cref="CStructLayoutException">The array is flexible, read to the end, or terminated.</exception>
    public Int128 GetCompiledFieldTotalElementCount(
        CompiledField field,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return 1;
        }

        if (field.Array.Kind is CompiledArrayKind.Flexible or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated)
        {
            throw new CStructLayoutException(
                "Flexible array has no fixed storage size: " + field.Name);
        }

        Int128 total = 1;
        foreach (CompiledArrayDimension dimension in field.Array.Dimensions)
        {
            Expr expression = dimension.CountExpression ??
                              throw new InvalidOperationException(
                                  "Compiled array dimension has no count expression: " + field.Name);
            Int128 count = this.EvaluateDimensionCount(expression, field.Name, variables, requireFixedSize);
            total = checked(total * count);
        }

        return total;
    }

    /// <summary>
    ///     Evaluates one dimension's own count expression, preserving both the fixed/require-static error wrapping
    ///     and the negative-length rejection shared identically by <see cref="GetCompiledArrayCount"/> (the
    ///     current/outermost dimension only) and <see cref="GetCompiledFieldTotalElementCount"/> (once per
    ///     dimension, accumulated into a product).
    /// </summary>
    private Int128 EvaluateDimensionCount(
        Expr expression,
        string fieldName,
        IReadOnlyDictionary<string, Expr> variables,
        bool requireFixedSize)
    {
        // A fixed-size query has no data, so any failure is the layout's; a variables-driven query runs inside a
        // read or address operation and its failure belongs to that data.
        Int128 count;
        try
        {
            count = this.expressionEvaluator.Evaluate(
                expression,
                variables,
                "array length for " + fieldName,
                requireFixedSize ? ExpressionFailureDomain.Layout : ExpressionFailureDomain.Read);
        }
        catch (Exception exception) when (requireFixedSize)
        {
            throw new CStructLayoutException("Cannot calculate fixed array size for field: " + fieldName, exception);
        }

        if (count < 0)
        {
            throw requireFixedSize
                      ? new CStructLayoutException(LayoutFailures.NegativeArrayLength(fieldName))
                      : new CStructReadException(LayoutFailures.NegativeArrayLength(fieldName));
        }

        return count;
    }
}
