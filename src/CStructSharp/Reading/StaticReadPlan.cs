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

    public StaticReadPlan(int size, StaticReadOperation[] operations)
    {
        this.Size = size;
        this.Operations = operations;
        int depth = 1;
        int arrayCount = 0;
        bool supportsWrite = true;
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

    /// <summary>Whether every operation has a span writer; character buffers stay on the general writer for now.</summary>
    public bool SupportsWrite { get; }

    /// <summary>The end of the last member; an aligned layout's writer zero-fills from here to <see cref="Size"/>.</summary>
    public int TailStart { get; }

    /// <summary>The bytes the general writer charges against <c>MaxTotalBytesWritten</c> for this composite in an unaligned layout.</summary>
    public int ChargedFieldBytes { get; }

    /// <summary>The same charge in an aligned layout, where every struct's tail padding is written as zeroes.</summary>
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
    public static StaticReadPlan? TryBuild(CompiledCompositeType composite)
    {
        if (composite.Symbol.FixedSize is not int size || composite.HasDirectConditionalFields || composite.Symbol.Declaration is Struct { IsUnion: true })
        {
            return null;
        }

        var operations = new List<StaticReadOperation>();
        return TryAppend(composite, composite.Shape, 0, operations, 0) ? new StaticReadPlan(size, operations.ToArray()) : null;
    }

    private static bool TryAppend(CompiledCompositeType composite, StructShape shape, int baseOffset, List<StaticReadOperation> operations, int depth)
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
                // absolute offset, so it simply has no operation.
                continue;
            }

            int absoluteOffset = baseOffset + offset;
            if (composite.PromotedFields.Contains(field))
            {
                // An anonymous promoted member reads its fields into the parent's own slots.
                if (field.Type.Symbol.Definition is not CompiledCompositeType promoted || promoted.Symbol.Declaration is Struct { IsUnion: true } ||
                    !TryAppend(promoted, shape, absoluteOffset, operations, depth + 1))
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
                    if (!TryAppend(nested, nested.Shape, 0, nestedOperations, depth + 1))
                    {
                        return false;
                    }

                    var nestedPlan = new StaticReadPlan(nestedSize, nestedOperations.ToArray());
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
