namespace CStructSharp.Reading;

using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     The flat operation list that reads one fully fixed composite from a span: every offset, size and
///     value kind is decided when the layout is compiled, so a parse of such a composite is a loop over
///     <see cref="Operations"/> writing straight into <see cref="StructValue"/> slots instead of an interpretation
///     of the declaration tree per field. Built lazily on the first parse of a composite; <see langword="null"/>
///     on <see cref="CompiledCompositeType.StaticPlan"/> means the composite needs the general reader.
/// </summary>
internal sealed class StaticReadPlan
{
    /// <summary>Largest composite a stream source reads as one pooled block before executing its plan.</summary>
    public const int MaximumBlockSize = 64 * 1024;

    /// <summary>Test hook: disables plan execution on the current thread so the general reader can be compared against it.</summary>
    [System.ThreadStatic]
    private static bool disabledForTesting;

    /// <summary>Builds operation limits and write eligibility for a fixed composite read plan.</summary>
    /// <param name="size">The complete composite storage size in bytes.</param>
    /// <param name="operations">Named field reads at offsets relative to the composite start.</param>
    /// <param name="hasUnnamedPadding">Whether omitted padding needs explicit zero writes and budget checks.</param>
    /// <param name="maximumPaddingArrayCount">The largest omitted padding array's total element count, or zero.</param>
    public StaticReadPlan(int size, StaticReadOperation[] operations, bool hasUnnamedPadding, int maximumPaddingArrayCount)
    {
        this.Size = size;
        this.Operations = operations;
        int depth = 1;
        int arrayCount = maximumPaddingArrayCount;
        bool supportsWrite = !hasUnnamedPadding;
        long charged = 0;
        long chargedAligned = 0;
        int lastFieldEnd = 0;
        foreach (StaticReadOperation operation in operations)
        {
            int fieldBytes;
            switch (operation.Kind)
            {
            case StaticReadKind.Nested:
                fieldBytes = operation.NestedPlan!.Size;
                charged += operation.NestedPlan.ChargedFieldBytes;
                chargedAligned += operation.NestedPlan.ChargedAlignedBytes;
                break;
            case StaticReadKind.NestedArray:
                fieldBytes = operation.NestedPlan!.Size * operation.Count;
                charged += (long)operation.NestedPlan.ChargedFieldBytes * operation.Count;
                chargedAligned += (long)operation.NestedPlan.ChargedAlignedBytes * operation.Count;
                break;
            case StaticReadKind.NumericArray:
                fieldBytes = operation.Field.Codec.Size * operation.Count;
                charged += fieldBytes;
                chargedAligned += fieldBytes;
                break;
            case StaticReadKind.CharArray:
                fieldBytes = operation.Count;
                charged += fieldBytes;
                chargedAligned += fieldBytes;
                supportsWrite = false;
                break;
            default:
                fieldBytes = operation.Field.Codec.Size;
                charged += fieldBytes;
                chargedAligned += fieldBytes;
                break;
            }

            lastFieldEnd = System.Math.Max(lastFieldEnd, operation.Offset + fieldBytes);
            if (operation.NestedPlan is StaticReadPlan nested)
            {
                depth = System.Math.Max(depth, 1 + nested.NestingDepth);
                arrayCount = System.Math.Max(arrayCount, nested.MaximumArrayCount);
                supportsWrite &= nested.SupportsWrite;
            }

            arrayCount = System.Math.Max(arrayCount, operation.Count);
        }

        // The general writer charges its budget for field bytes and, in an aligned layout, the zero-filled tail of
        // every struct - never for the padding it seeks over between fields; a plan write charges the same amount.
        this.NestingDepth = depth;
        this.MaximumArrayCount = arrayCount;
        this.SupportsWrite = supportsWrite;
        this.TailStart = lastFieldEnd;
        this.ChargedFieldBytes = checked((int)charged);
        this.ChargedAlignedBytes = checked((int)(chargedAligned + (size - lastFieldEnd)));
    }

    /// <summary>Whether every field has a span writer; character buffers and unnamed padding use the general writer.</summary>
    public bool SupportsWrite { get; }

    /// <summary>The end of the last member; an aligned layout's writer zero-fills from here to <see cref="Size"/>.</summary>
    public int TailStart { get; }

    /// <summary>For a write-enabled plan, the bytes charged against <c>MaxTotalBytesWritten</c> in an unaligned layout.</summary>
    public int ChargedFieldBytes { get; }

    /// <summary>For a write-enabled plan, the aligned charge including each struct's zero-filled tail padding.</summary>
    public int ChargedAlignedBytes { get; }

    /// <summary>Structure levels the plan enters, itself included - checked against the nesting limit before any byte is consumed.</summary>
    public int NestingDepth { get; }

    /// <summary>The largest fixed array count in the plan - checked against the array limit before any byte is consumed.</summary>
    public int MaximumArrayCount { get; }

    /// <summary>Gets or sets the per-thread test switch that routes every composite through the general reader.</summary>
    internal static bool DisabledForTesting
    {
        get => disabledForTesting;
        set => disabledForTesting = value;
    }

    /// <summary>The composite's fixed storage size, including trailing padding.</summary>
    public int Size { get; }

    public StaticReadOperation[] Operations { get; }

    /// <summary>Builds the plan for <paramref name="composite"/>, or returns null when any member needs the general reader.</summary>
    /// <param name="composite">The compiled composite whose offsets are relative to its own start.</param>
    /// <returns>A fixed-offset read plan, or null when interpretation is required.</returns>
    public static StaticReadPlan? TryBuild(CompiledCompositeType composite)
    {
        if (composite.Symbol.FixedSize is not int size || composite.HasDirectConditionalFields || composite.Symbol.Declaration is Struct { IsUnion: true })
        {
            return null;
        }

        var operations = new List<StaticReadOperation>();
        bool hasUnnamedPadding = false;
        int maximumPaddingArrayCount = 0;
        return TryAppend(composite, composite.Shape, 0, operations, 0, ref hasUnnamedPadding, ref maximumPaddingArrayCount)
            ? new StaticReadPlan(size, operations.ToArray(), hasUnnamedPadding, maximumPaddingArrayCount)
            : null;
    }

    /// <summary>Appends fixed reads, flattening promoted members while tracking storage omitted from the value shape.</summary>
    /// <param name="composite">The composite to inspect.</param>
    /// <param name="shape">The destination value slots, including any promoted members.</param>
    /// <param name="baseOffset">The composite start in bytes relative to the plan's root.</param>
    /// <param name="operations">The operation list extended on success; discarded by callers on failure.</param>
    /// <param name="depth">The number of enclosing composite levels.</param>
    /// <param name="hasUnnamedPadding">Set when this flattened operation list omits explicit padding.</param>
    /// <param name="maximumPaddingArrayCount">Tracks array limits for omitted storage in this flattened operation list.</param>
    /// <returns>Whether every field can be read through a fixed operation.</returns>
    private static bool TryAppend(CompiledCompositeType composite, StructShape shape, int baseOffset, List<StaticReadOperation> operations, int depth, ref bool hasUnnamedPadding, ref int maximumPaddingArrayCount)
    {
        if (depth > 64 || composite.HasDirectConditionalFields)
        {
            return false;
        }

        foreach (CompiledField field in composite.Fields)
        {
            Field declaration = field.Declaration;
            if (field.FixedOffset is not int offset || declaration.Condition is not null || declaration.BranchConditions.Count > 0 ||
                field.PointerDepth > 0 || declaration.BitSize != 0 || field.IsZeroWidthBitfield || declaration.OffsetAssertionExpression is not null)
            {
                return false;
            }

            if (declaration.Name.Name.Length == 0 && !composite.PromotedFields.Contains(field))
            {
                // An unnamed padding field has a fixed offset and size but no slot to fill; the plan reads by
                // absolute offset, so it needs no read operation. The writer must still zero and charge this storage.
                hasUnnamedPadding = true;
                if (field.Array.Kind != CompiledArrayKind.Scalar)
                {
                    // Skipping result construction must not bypass the general reader's array-element limit.
                    maximumPaddingArrayCount = System.Math.Max(maximumPaddingArrayCount, field.Array.TotalFixedElementCount ?? 0);
                }

                continue;
            }

            int absoluteOffset = baseOffset + offset;
            if (composite.PromotedFields.Contains(field))
            {
                // An anonymous promoted member reads its fields into the parent's own slots.
                if (field.Type.Symbol.Definition is not CompiledCompositeType promoted || promoted.Symbol.Declaration is Struct { IsUnion: true } ||
                    !TryAppend(promoted, shape, absoluteOffset, operations, depth + 1, ref hasUnnamedPadding, ref maximumPaddingArrayCount))
                {
                    return false;
                }

                continue;
            }

            if (!shape.TryGetIndex(declaration.Name.Name, out int slot))
            {
                return false;
            }

            switch (field.Type.Symbol.Kind)
            {
            case CompiledTypeKind.Struct:
                {
                    if (field.Type.Symbol.Definition is not CompiledCompositeType nested ||
                        nested.Symbol.Declaration is not Struct { IsUnion: false } nestedDeclaration || nested.Symbol.FixedSize is not int nestedSize)
                    {
                        return false;
                    }

                    var nestedOperations = new List<StaticReadOperation>();
                    bool nestedHasUnnamedPadding = false;
                    int nestedMaximumPaddingArrayCount = 0;
                    if (!TryAppend(nested, nested.Shape, 0, nestedOperations, depth + 1, ref nestedHasUnnamedPadding, ref nestedMaximumPaddingArrayCount))
                    {
                        return false;
                    }

                    var nestedPlan = new StaticReadPlan(nestedSize, nestedOperations.ToArray(), nestedHasUnnamedPadding, nestedMaximumPaddingArrayCount);
                    if (field.Array.Kind == CompiledArrayKind.Scalar)
                    {
                        operations.Add(new StaticReadOperation(StaticReadKind.Nested, slot, absoluteOffset, field, nestedDeclaration, nested, nestedPlan));
                    }
                    else if (field.Array.Dimensions.Length == 1 && field.Array.FixedCount is int count && field.FixedElementSize == nestedSize)
                    {
                        operations.Add(new StaticReadOperation(StaticReadKind.NestedArray, slot, absoluteOffset, field, nestedDeclaration, nested, nestedPlan, count));
                    }
                    else
                    {
                        return false;
                    }

                    break;
                }

            case CompiledTypeKind.Enum:
                // A flag's decomposition lives in FlagValueResult, which the span plan and its JavaScript twin do
                // not produce; a struct holding one takes the interpreter path.
                if (field.Array.Kind != CompiledArrayKind.Scalar || !field.Codec.IsFixedWidthNumeric || field.Type.Symbol.Definition is not CompiledEnumType { IsFlag: false })
                {
                    return false;
                }

                operations.Add(new StaticReadOperation(StaticReadKind.Enum, slot, absoluteOffset, field, null, null, null));
                break;
            case CompiledTypeKind.Union:
                return false;
            default:
                if (field.Array.Kind == CompiledArrayKind.Scalar)
                {
                    if (!field.Codec.IsFixedWidthNumeric)
                    {
                        return false;
                    }

                    operations.Add(new StaticReadOperation(StaticReadKind.Numeric, slot, absoluteOffset, field, null, null, null));
                }
                else if (field.Array.Dimensions.Length == 1 && field.Array.FixedCount is int count && !field.IsUnsizedCharacterArray)
                {
                    if (field.Codec.IsFixedWidthNumeric)
                    {
                        operations.Add(new StaticReadOperation(StaticReadKind.NumericArray, slot, absoluteOffset, field, null, null, null, count));
                    }
                    else if (field.Codec.Kind == PrimitiveCodecKind.Char && field.Codec.Size == 1)
                    {
                        operations.Add(new StaticReadOperation(StaticReadKind.CharArray, slot, absoluteOffset, field, null, null, null, count));
                    }
                    else
                    {
                        return false;
                    }
                }
                else
                {
                    return false;
                }

                break;
            }
        }

        return true;
    }
}
