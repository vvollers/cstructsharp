namespace CStructSharp;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Generated;

/// <summary>Immutable runtime recognition decisions, reusable with independent execution state on any thread.</summary>
/// <remarks>Only selected magic rules, or evaluable expect rules when no magic rule is evaluable, participate. Supports and dynamic positions remain unchecked. Input starts at the record; unrelated trailing fields need not be present. No bytes, caller variables or execution context are retained.</remarks>
public sealed class PreparedMatcher
{
    private readonly SlotTable table;
    private readonly SlotValue[] initial;
    private readonly Numeric[] numbers;
    private readonly Bytes[] comparisons;
    private readonly ProgramExpression[] conditions;

    /// <summary>Stores prepared conditions and typed decode slots.</summary>
    /// <param name="table">Shared expression semantics and slot names.</param>
    /// <param name="initial">Resolved layout constants, without caller overrides.</param>
    /// <param name="numbers">Required fixed integer fields.</param>
    /// <param name="comparisons">Required fixed byte comparisons.</param>
    /// <param name="conditions">Selected recognition programs.</param>
    /// <param name="rules">Checked and unchecked declaration rules.</param>
    internal PreparedMatcher(SlotTable table, SlotValue[] initial, Numeric[] numbers, Bytes[] comparisons, ProgramExpression[] conditions, IReadOnlyList<MatchRule> rules)
    {
        this.table = table;
        this.initial = initial;
        this.numbers = numbers;
        this.comparisons = comparisons;
        this.conditions = conditions;
        this.Rules = rules;
    }

    /// <summary>Gets every direct rule in declaration order, including explicitly excluded rules.</summary>
    public IReadOnlyList<MatchRule> Rules { get; }

    /// <summary>Recognizes a record without materializing fields or allocating scalar boxes.</summary>
    /// <param name="source">Record bytes starting at offset zero. A short required field returns false.</param>
    /// <param name="variables">Integer overrides; never mutated or retained. Unknown extra names are ignored.</param>
    /// <returns>Whether all selected conditions evaluate to nonzero.</returns>
    /// <exception cref="ArgumentException">A selected condition uses a missing caller variable.</exception>
    /// <exception cref="CStructLayoutException">Expression work or depth limits are exceeded.</exception>
    public bool Matches(ReadOnlySpan<byte> source, IReadOnlyDictionary<string, int>? variables = null)
    {
        SlotValue[] values = ArrayPool<SlotValue>.Shared.Rent(this.initial.Length);
        try
        {
            this.initial.CopyTo(values, 0);
            if (variables is not null)
            {
                foreach (KeyValuePair<string, int> variable in variables)
                {
                    if (this.table.TryGetSlot(variable.Key, out int slot))
                    {
                        values[slot] = SlotValue.FromLiteral(variable.Value);
                    }
                }
            }

            foreach (Numeric number in this.numbers)
            {
                if (number.Offset > source.Length || number.Codec.Size > source.Length - number.Offset)
                {
                    return false;
                }

                values[number.Slot] = SlotValue.FromLiteral(ReadInteger(number.Codec, source.Slice(number.Offset, number.Codec.Size)));
            }

            foreach (Bytes comparison in this.comparisons)
            {
                if (comparison.Offset > source.Length || comparison.Value.Length > source.Length - comparison.Offset)
                {
                    return false;
                }

                values[comparison.Slot] = SlotValue.FromLiteral(source.Slice(comparison.Offset, comparison.Value.Length).SequenceEqual(comparison.Value) ? 1 : 0);
            }

            try
            {
                foreach (ProgramExpression condition in this.conditions)
                {
                    if (condition.EvaluateUnmapped(values, null) == 0)
                    {
                        return false;
                    }
                }
            }
            catch (KeyNotFoundException exception)
            {
                throw new ArgumentException("A condition of Matches names a variable that 'variables' does not supply. " + exception.Message, nameof(variables), exception);
            }
            catch (Exception exception) when (LayoutExpressionEvaluator.IsExpressionFailure(exception))
            {
                return false;
            }

            return true;
        }
        finally
        {
            ArrayPool<SlotValue>.Shared.Return(values, clearArray: true);
        }
    }

    /// <summary>Decodes the integer codecs accepted by runtime recognition into the existing expression domain.</summary>
    /// <param name="codec">Prepared built-in integer codec.</param>
    /// <param name="bytes">Complete storage of the integer.</param>
    /// <returns>Exact signed 128-bit operand, preserving all unsigned 64-bit values.</returns>
    private static Int128 ReadInteger(PrimitiveCodec codec, ReadOnlySpan<byte> bytes) => codec.Kind switch
    {
        PrimitiveCodecKind.UInt8 => bytes[0],
        PrimitiveCodecKind.Int8 => unchecked((sbyte)bytes[0]),
        PrimitiveCodecKind.Bool => bytes[0] == 0 ? 0 : 1,
        PrimitiveCodecKind.Int16 => Codec.ReadInt16(bytes, codec.LittleEndian),
        PrimitiveCodecKind.UInt16 => Codec.ReadUInt16(bytes, codec.LittleEndian),
        PrimitiveCodecKind.Int24 => Codec.ReadInt24(bytes, codec.LittleEndian),
        PrimitiveCodecKind.UInt24 => Codec.ReadUInt24(bytes, codec.LittleEndian),
        PrimitiveCodecKind.Int32 => Codec.ReadInt32(bytes, codec.LittleEndian),
        PrimitiveCodecKind.UInt32 => Codec.ReadUInt32(bytes, codec.LittleEndian),
        PrimitiveCodecKind.Int64 => Codec.ReadInt64(bytes, codec.LittleEndian),
        PrimitiveCodecKind.UInt64 => Codec.ReadUInt64(bytes, codec.LittleEndian),
        _ => throw new InvalidOperationException("Recognition requires a fixed-width integer codec."),
    };

    /// <summary>One fixed field decoded directly into an expression slot.</summary>
    /// <param name="Slot">Expression slot.</param>
    /// <param name="Offset">Byte offset relative to record start.</param>
    /// <param name="Codec">Prepared integer codec.</param>
    internal readonly record struct Numeric(int Slot, int Offset, PrimitiveCodec Codec);

    /// <summary>One byte comparison decoded directly into a boolean expression slot.</summary>
    /// <param name="Slot">Expression slot.</param>
    /// <param name="Offset">Byte offset relative to record start.</param>
    /// <param name="Value">Immutable expected bytes.</param>
    internal readonly record struct Bytes(int Slot, int Offset, byte[] Value);
}
