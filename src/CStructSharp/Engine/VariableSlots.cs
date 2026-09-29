namespace CStructSharp.Engine;

using System;
using System.Buffers;
using System.Collections.Generic;
using CStructSharp.Compilation.Programs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     One operation's layout variables as slots of the layout's <see cref="SlotTable"/>: a pooled
///     <see cref="SlotValue"/> array that starts as the table's resolved state with the caller's variables applied and
///     that the operation then updates as it captures field values. It replaces the operation's name dictionary.
/// </summary>
/// <remarks>
///     A mutable struct owned by one operation: keep it in a field or local and pass it by reference, and call
///     <see cref="Dispose"/> exactly once when the operation ends, which returns the array to the pool. Creating it for
///     an operation without caller variables allocates nothing once the pool holds an array of the table's size.
/// </remarks>
internal struct VariableSlots : IDisposable
{
    // One slot array per thread, taken for the length of an operation and put back cleared: an operation on this
    // thread that starts while another holds it (a nested read from a callback) rents from the shared pool instead.
    [ThreadStatic]
    private static SlotValue[]? spare;

    private readonly SlotTable table;
    private readonly Dictionary<string, Expr>? unslotted;
    private SlotValue[] values;

    /// <summary>Wraps an initialized slot array.</summary>
    /// <param name="table">The layout's table.</param>
    /// <param name="values">The slot array, owned from now on.</param>
    /// <param name="unslotted">The caller's variables without a slot, as literals, or <see langword="null"/>.</param>
    private VariableSlots(SlotTable table, SlotValue[] values, Dictionary<string, Expr>? unslotted)
    {
        this.table = table;
        this.values = values;
        this.unslotted = unslotted;
    }

    /// <summary>Gets the table the slots belong to.</summary>
    public readonly SlotTable Table => this.table;

    /// <summary>Gets the number of slots, the layout table's.</summary>
    public readonly int Count => this.table.Count;

    /// <summary>Creates the slots of an operation with public integer variables.</summary>
    /// <param name="table">The layout's table.</param>
    /// <param name="variables">
    ///     The caller's variables, or <see langword="null"/>; a name without a slot is kept beside the slots, for the count
    ///     of a root spelled at run time (<c>uint8[M]</c>) that no layout expression names.
    /// </param>
    /// <returns>The initialized slots.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">A definition cannot be resolved.</exception>
    public static VariableSlots Create(SlotTable table, IReadOnlyDictionary<string, int>? variables)
    {
        SlotValue[] values = Rent(table.Count);
        try
        {
            return new VariableSlots(table, values, table.Initialize(values, variables));
        }
        catch
        {
            Return(values, table.Count);
            throw;
        }
    }

    /// <summary>Creates the slots of an operation from its variable input.</summary>
    /// <param name="table">The layout's table.</param>
    /// <param name="input">The operation's variable input.</param>
    /// <returns>The initialized slots.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">A definition cannot be resolved.</exception>
    public static VariableSlots Create(SlotTable table, LayoutVariableInput input) => Create(table, input.Integers);

    /// <summary>
    ///     Creates independent slots holding the same values, as the interpreter copies its variable dictionary (an update's
    ///     layout captures each start from a copy); the copy is disposed separately.
    /// </summary>
    /// <returns>The copy.</returns>
    public readonly VariableSlots Clone()
    {
        SlotValue[] copy = Rent(this.table.Count);
        Array.Copy(this.values, copy, this.table.Count);
        Dictionary<string, Expr>? unslotted = this.unslotted is null ? null : new Dictionary<string, Expr>(this.unslotted, StringComparer.Ordinal);
        return new VariableSlots(this.table, copy, unslotted);
    }

    /// <summary>Copies every slot into <paramref name="destination"/> from <paramref name="offset"/> on (a union's entry values).</summary>
    /// <param name="destination">The array receiving <see cref="Count"/> values.</param>
    /// <param name="offset">The first index written.</param>
    public readonly void CopyTo(SlotValue[] destination, int offset) => Array.Copy(this.values, 0, destination, offset, this.table.Count);

    /// <summary>Replaces every slot with the values saved at <paramref name="offset"/> of <paramref name="source"/>.</summary>
    /// <param name="source">The array holding <see cref="Count"/> saved values.</param>
    /// <param name="offset">The first index read.</param>
    public readonly void CopyFrom(SlotValue[] source, int offset) => Array.Copy(source, offset, this.values, 0, this.table.Count);

    /// <summary>Returns a slot's value.</summary>
    /// <param name="slot">The slot.</param>
    /// <returns>The value.</returns>
    public readonly SlotValue Get(int slot) => this.values[slot];

    /// <summary>Replaces a slot's value, as capturing a field or removing its name does.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="value">The new value; <see cref="SlotValue.Undefined"/> removes the name.</param>
    public readonly void Set(int slot, SlotValue value) => this.values[slot] = value;

    /// <summary>Evaluates a program against the slots (<see cref="ProgramExpression.Evaluate"/>).</summary>
    /// <param name="program">A program of this table.</param>
    /// <param name="context">What is being evaluated, used in the failure message.</param>
    /// <param name="domain">Whether a failure is reported as a layout, read, or write exception.</param>
    /// <returns>The signed 128-bit value.</returns>
    public readonly Int128 Evaluate(ProgramExpression program, string context, ExpressionFailureDomain domain)
        => program.Evaluate(this.values, this.unslotted, context, domain);

    /// <summary>Evaluates a program whose consumer stores an <see cref="int"/> (<see cref="ProgramExpression.EvaluateInt32"/>).</summary>
    /// <param name="program">A program of this table.</param>
    /// <param name="context">What is being evaluated, used in the failure messages.</param>
    /// <param name="domain">Whether a failure is reported as a layout, read, or write exception.</param>
    /// <returns>The value as an <see cref="int"/>.</returns>
    public readonly int EvaluateInt32(ProgramExpression program, string context, ExpressionFailureDomain domain)
        => program.EvaluateInt32(this.values, this.unslotted, context, domain);

    /// <summary>Builds the name dictionary the slots stand for, for comparison with the dictionary model.</summary>
    /// <returns>A new dictionary.</returns>
    public readonly Dictionary<string, Expr> ToDictionary() => this.table.CreateDictionary(this.values, this.unslotted);

    /// <summary>Returns the slot array to the pool; the slots must not be used afterwards.</summary>
    public void Dispose()
    {
        SlotValue[] released = this.values;
        this.values = [];
        Return(released, this.table.Count);
    }

    /// <summary>Rents a slot array of at least <paramref name="count"/> entries: the thread's spare when it is free and large enough.</summary>
    /// <param name="count">The number of slots.</param>
    /// <returns>The array; its contents are overwritten by initialization.</returns>
    private static SlotValue[] Rent(int count)
    {
        if (count == 0)
        {
            return [];
        }

        SlotValue[]? values = spare;
        if (values is not null && values.Length >= count)
        {
            spare = null;
            return values;
        }

        return ArrayPool<SlotValue>.Shared.Rent(count);
    }

    /// <summary>Returns a rented array, cleared so no payload stays alive: kept as the thread's spare when it is larger than the current one, else to the pool.</summary>
    /// <param name="values">The array; an empty one is not kept.</param>
    /// <param name="used">The number of leading entries the operation used; the rest are already clear.</param>
    private static void Return(SlotValue[] values, int used)
    {
        if (values.Length == 0)
        {
            return;
        }

        // The thread keeps the larger of its spare and this array, so after the largest table it reads with, every
        // operation on the thread takes the spare and the shared pool is not touched.
        Array.Clear(values, 0, used);
        SlotValue[]? kept = spare;
        if (kept is null || kept.Length < values.Length)
        {
            spare = values;
            values = kept ?? [];
        }

        if (values.Length > 0)
        {
            ArrayPool<SlotValue>.Shared.Return(values);
        }
    }
}
