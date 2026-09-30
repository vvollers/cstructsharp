namespace CStructSharp.Compilation.Programs;

using System.Collections;
using System.Collections.Generic;
using CStructSharp.Syntax;

/// <summary>
///     The names an operation's slots stand for, as the read-only dictionary the dictionary-based evaluators take
///     (runtime sizes, counts and conditions measured through <see cref="CompiledSizeQueries"/>), without building it: a
///     lookup reads the name's slot directly, and only an enumeration materializes the whole dictionary
///     (<see cref="SlotTable.CreateDictionary"/>), which holds exactly the same entries.
/// </summary>
/// <remarks>
///     A lookup of a slot's name returns the entry its value stands for (<see cref="SlotValue.ToExpression"/>): none for
///     an undefined slot. Any other name is looked up in the caller variables that have no slot. The view reads the live
///     slot array, so it sees a capture made after it was created; it is used within one operation on one thread.
/// </remarks>
internal sealed class SlotDictionaryView : IReadOnlyDictionary<string, Expr>
{
    private readonly SlotTable table;
    private readonly SlotValue[] values;
    private readonly IReadOnlyDictionary<string, Expr>? unslotted;

    /// <summary>Creates a view over one operation's slots.</summary>
    /// <param name="table">The slot table the values are indexed by.</param>
    /// <param name="values">The operation's slot values; read, never changed.</param>
    /// <param name="unslotted">The caller variables that have no slot, or <see langword="null"/>.</param>
    public SlotDictionaryView(SlotTable table, SlotValue[] values, IReadOnlyDictionary<string, Expr>? unslotted)
    {
        this.table = table;
        this.values = values;
        this.unslotted = unslotted;
    }

    /// <inheritdoc/>
    public int Count => this.Materialize().Count;

    /// <inheritdoc/>
    public IEnumerable<string> Keys => this.Materialize().Keys;

    /// <inheritdoc/>
    public IEnumerable<Expr> Values => this.Materialize().Values;

    /// <inheritdoc/>
    /// <exception cref="KeyNotFoundException">The name has no entry.</exception>
    public Expr this[string key] => this.TryGetValue(key, out Expr value) ? value : throw new KeyNotFoundException(key);

    /// <inheritdoc/>
    public bool ContainsKey(string key) => this.TryGetValue(key, out _);

    /// <inheritdoc/>
    public bool TryGetValue(string key, out Expr value)
    {
        // A slot's name never appears among the unslotted variables, so the slot decides alone.
        if (this.table.TryGetSlot(key, out int slot))
        {
            Expr? slotValue = this.values[slot].ToExpression();
            value = slotValue!;
            return slotValue is not null;
        }

        if (this.unslotted is not null && this.unslotted.TryGetValue(key, out Expr? unslottedValue))
        {
            value = unslottedValue;
            return true;
        }

        value = null!;
        return false;
    }

    /// <inheritdoc/>
    public IEnumerator<KeyValuePair<string, Expr>> GetEnumerator() => this.Materialize().GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();

    /// <summary>Builds the whole dictionary the slots stand for, for an enumeration.</summary>
    /// <returns>A new dictionary.</returns>
    private Dictionary<string, Expr> Materialize() => this.table.CreateDictionary(this.values, this.unslotted);
}
