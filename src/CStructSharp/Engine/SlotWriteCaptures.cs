namespace CStructSharp.Engine;

using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Expressions;
using CStructSharp.Writing;

/// <summary>
///     The capture sink the compiled engine hands a static write plan: the operation's slots, and the qualified prefix
///     active where the plan runs. A capture is stored by the shared rule (<see cref="LayoutVariableCapture.ToSlotValue"/>)
///     in the name's slot and published under the prefix's qualified spellings; a name without a slot is not observable
///     by any expression and is not stored.
/// </summary>
/// <remarks>
///     The prefix is the sink's own copy: the plan sets it around nested structs and restores it, so after the plan it
///     equals the operation's prefix again and nothing needs to flow back.
/// </remarks>
internal struct SlotWriteCaptures : IStaticWriteCaptures
{
    private readonly VariableSlots slots;

    /// <summary>Creates the sink for one plan execution.</summary>
    /// <param name="slots">The operation's slots.</param>
    /// <param name="prefix">The qualified prefix active where the plan runs.</param>
    public SlotWriteCaptures(VariableSlots slots, string? prefix)
    {
        this.slots = slots;
        this.QualifiedPrefix = prefix;
    }

    /// <summary>Gets or sets the active qualified prefix.</summary>
    public string? QualifiedPrefix { get; set; }

    /// <summary>Whether an expression of the layout names the field (the engine never captures every field).</summary>
    /// <param name="field">The field.</param>
    /// <returns>Whether the field is captured.</returns>
    public readonly bool Captures(CompiledField field) => field.CapturesLayoutVariable;

    /// <summary>Stores the capture in the name's slot and publishes it under the active prefix.</summary>
    /// <param name="name">The field's name.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The supplied value.</param>
    public readonly void Capture(string name, CompiledField field, object value)
    {
        SlotValue captured = field.NotANumberReason is { } reason ? SlotValue.FromUnusable(new NotANumberVariable(reason)) : LayoutVariableCapture.ToSlotValue(value);
        SlotTable table = this.slots.Table;
        if (table.TryGetSlot(name, out int slot))
        {
            this.slots.Set(slot, captured);
        }

        if (this.QualifiedPrefix is not { } prefix)
        {
            return;
        }

        foreach (ReadProgram.QualifiedTarget target in table.ReadPrograms.GetQualifiedTargets(name))
        {
            if (QualifiedPublication.Covers(prefix, target.Prefix))
            {
                this.slots.Set(target.Slot, captured);
            }
        }
    }
}
