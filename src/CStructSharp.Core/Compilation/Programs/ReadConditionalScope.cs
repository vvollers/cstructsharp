namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
///     A composite's <see cref="CompiledConditionalScope"/> in slot terms, for the <see cref="ReadOpCode.EnterConditionalScope"/>
///     and <see cref="ReadOpCode.CompleteMember"/> steps. The interpreter's <c>ConditionalVariableScope</c> keeps one
///     local value per kept name; here each kept name is a <em>local</em> index with the slot it mirrors.
/// </summary>
/// <remarks>
///     <para>
///         A kept name without a slot is dropped from every list: no expression of the layout can read it, so removing,
///         saving or restoring it is not observable. The executor holds one <see cref="SlotValue"/> per local for each
///         composite instance, all <see cref="SlotValue.Undefined"/> at entry (the interpreter's "no saved value", which
///         a restore turns into a removal).
///     </para>
///     <para>Immutable and shared by every thread.</para>
/// </remarks>
internal sealed class ReadConditionalScope
{
    private readonly int[][] captured;
    private readonly int[][] restored;

    /// <summary>Maps a composite's scope through the layout's slot table.</summary>
    /// <param name="scope">The composite's conditional scope.</param>
    /// <param name="table">The layout's slot table.</param>
    public ReadConditionalScope(CompiledConditionalScope scope, SlotTable table)
    {
        this.LocalSlots = table.MapNames(scope.LocalNames);
        var cleared = new List<int>(this.LocalSlots.Length);
        foreach (int slot in this.LocalSlots)
        {
            if (slot >= 0)
            {
                cleared.Add(slot);
            }
        }

        this.ClearedSlots = cleared.ToArray();
        this.captured = new int[scope.CapturedLocalSlots.Length][];
        this.restored = new int[scope.RestoredLocalSlots.Length][];
        for (int member = 0; member < this.captured.Length; member++)
        {
            this.captured[member] = this.Slotted(scope.CapturedLocalSlots[member]);
            this.restored[member] = this.Slotted(scope.RestoredLocalSlots[member]);
        }
    }

    /// <summary>Gets the slot each local mirrors, by local index; -1 for a kept name without a slot.</summary>
    public int[] LocalSlots { get; }

    /// <summary>Gets the slots <see cref="ReadOpCode.EnterConditionalScope"/> makes undefined: every kept name that has one.</summary>
    public int[] ClearedSlots { get; }

    /// <summary>Gets the number of locals, the length of an instance's saved-value array.</summary>
    public int LocalCount => this.LocalSlots.Length;

    /// <summary>Returns the locals a member saves once it is read: its own visible names that have slots.</summary>
    /// <param name="member">The member's index in its composite.</param>
    /// <returns>Local indexes; empty when the member saves nothing observable.</returns>
    public IReadOnlyList<int> GetCaptured(int member) => this.captured[member];

    /// <summary>Returns the locals a member restores once it is read: the composite's names a nested declaration inside it may have replaced.</summary>
    /// <param name="member">The member's index in its composite.</param>
    /// <returns>Local indexes; empty when the member restores nothing observable.</returns>
    public IReadOnlyList<int> GetRestored(int member) => this.restored[member];

    /// <summary>Whether completing a member has an observable effect, so the compiler emits its <see cref="ReadOpCode.CompleteMember"/> step.</summary>
    /// <param name="member">The member's index in its composite.</param>
    /// <returns><see langword="true"/> when the member saves or restores a slotted name.</returns>
    public bool HasEffect(int member) => this.captured[member].Length > 0 || this.restored[member].Length > 0;

    /// <summary>Keeps the locals whose names have slots.</summary>
    /// <param name="locals">Local indexes from the compiled scope.</param>
    /// <returns>The slotted ones, in the same order.</returns>
    private int[] Slotted(ImmutableArray<int> locals)
    {
        var kept = new List<int>(locals.Length);
        foreach (int local in locals)
        {
            if (this.LocalSlots[local] >= 0)
            {
                kept.Add(local);
            }
        }

        return kept.ToArray();
    }
}
