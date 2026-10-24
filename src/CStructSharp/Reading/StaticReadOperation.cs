namespace CStructSharp.Reading;

using System.Threading;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>One step of a <see cref="StaticReadPlan"/>.</summary>
internal sealed class StaticReadOperation
{
    // Marks a reason not yet looked up: the reason itself may legitimately be null (the field is an integer).
    private const string ReasonUnknown = "\0unknown";

    // The slot the field's captured value goes to in one slot table, found once. One reference, replaced whole, so a
    // reader never sees a table with another table's slot; a layout has one table, so it is bound once in practice.
    private CaptureBinding? captureBinding;

    // Why the field is not an integer, found once: the answer depends on the compiled field alone, and a capture asks per read.
    private string? notANumberReason = ReasonUnknown;

    /// <summary>Creates one fixed-offset read step.</summary>
    /// <param name="kind">How the step decodes its bytes.</param>
    /// <param name="slot">The index of the result slot the value fills in the struct's shape.</param>
    /// <param name="offset">The field's byte offset from the start of the plan's root struct.</param>
    /// <param name="field">The compiled field the step reads.</param>
    /// <param name="nestedDeclaration">The nested struct declaration, for nested steps only.</param>
    /// <param name="nestedComposite">The nested compiled struct, for nested steps only.</param>
    /// <param name="nestedPlan">The nested struct's own cached plan, for nested steps only.</param>
    /// <param name="count">The fixed element count, for array steps; 0 for a scalar.</param>
    public StaticReadOperation(StaticReadKind kind, int slot, int offset, CompiledField field, Struct? nestedDeclaration, CompiledCompositeType? nestedComposite, StaticReadPlan? nestedPlan, int count = 0)
    {
        this.Kind = kind;
        this.Slot = slot;
        this.Offset = offset;
        this.Field = field;
        this.NestedDeclaration = nestedDeclaration;
        this.NestedComposite = nestedComposite;
        this.NestedPlan = nestedPlan;
        this.Count = count;
    }

    /// <summary>Gets how the step decodes its bytes.</summary>
    public StaticReadKind Kind { get; }

    /// <summary>Gets the index of the result slot the value fills in the struct's shape.</summary>
    public int Slot { get; }

    /// <summary>Gets the field's byte offset from the start of the plan's root struct.</summary>
    public int Offset { get; }

    /// <summary>Gets the compiled field the step reads.</summary>
    public CompiledField Field { get; }

    /// <summary>Gets the nested struct declaration, or <see langword="null"/> for a primitive step.</summary>
    public Struct? NestedDeclaration { get; }

    /// <summary>Gets the nested compiled struct, or <see langword="null"/> for a primitive step.</summary>
    public CompiledCompositeType? NestedComposite { get; }

    /// <summary>Gets the nested struct's cached plan, or <see langword="null"/> for a primitive step.</summary>
    public StaticReadPlan? NestedPlan { get; }

    /// <summary>Gets the fixed element count of an array step, or 0 for a scalar step.</summary>
    public int Count { get; }

    /// <summary>
    ///     Gets the index of the field among the members of the composite that declares it (the index that composite's read
    ///     program uses). For a field of an anonymous member flattened into the plan, <see cref="HolderPath"/> names that
    ///     member's chain. Set once while the plan is built.
    /// </summary>
    public int MemberIndex { get; internal set; }

    /// <summary>
    ///     Gets the member indexes, from the plan's root composite inward, of the anonymous structs the field was flattened
    ///     out of; empty for a member the root composite declares itself. Set once while the plan is built.
    /// </summary>
    public int[] HolderPath { get; internal set; } = System.Array.Empty<int>();

    /// <summary>Gets a value indicating whether the field carries an <c>@expect</c> or <c>@supports</c> condition.</summary>
    public bool HasConstraints => !this.Field.Constraints.IsDefaultOrEmpty;

    /// <summary>Converts the value this operation read into the slot value its layout variable holds (<see cref="LayoutVariableCapture"/>).</summary>
    /// <param name="value">The decoded value.</param>
    /// <returns>The slot value.</returns>
    public SlotValue ToSlotValue(object? value)
    {
        string? reason = Volatile.Read(ref this.notANumberReason);
        if (ReferenceEquals(reason, ReasonUnknown))
        {
            reason = this.Field.NotANumberReason;
            Volatile.Write(ref this.notANumberReason, reason);
        }

        return reason is null ? LayoutVariableCapture.ToSlotValue(value) : LayoutVariableCapture.ToSlotValue(this.Field, value);
    }

    /// <summary>Returns the slot of the field's captured value in <paramref name="table"/>, or -1 when the table has none.</summary>
    /// <param name="table">The slot table of the read in progress.</param>
    /// <returns>The slot index, found by name on the first call for a table.</returns>
    public int GetCaptureSlot(SlotTable table)
    {
        CaptureBinding? binding = Volatile.Read(ref this.captureBinding);
        if (binding is null || !ReferenceEquals(binding.Table, table))
        {
            binding = new CaptureBinding(table, table.TryGetSlot(this.Field.Declaration.Name.Name, out int slot) ? slot : -1);
            Volatile.Write(ref this.captureBinding, binding);
        }

        return binding.Slot;
    }

    /// <summary>A slot table and the slot a name has in it.</summary>
    /// <param name="Table">The table.</param>
    /// <param name="Slot">The slot, or -1.</param>
    private sealed record CaptureBinding(SlotTable Table, int Slot);
}
