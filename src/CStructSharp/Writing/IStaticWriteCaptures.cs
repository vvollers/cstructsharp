namespace CStructSharp.Writing;

using CStructSharp.Compilation;

/// <summary>
///     Where a static write plan publishes the layout variables its fields supply: the compiled engine's slots
///     (<see cref="Engine.SlotWriteCaptures"/>), or nowhere for a direct root write, which writes nothing afterwards
///     (<see cref="NoStaticWriteCaptures"/>). The plan is executed once, generically over this sink, so both encode a fixed
///     struct with the same code.
/// </summary>
internal interface IStaticWriteCaptures
{
    /// <summary>
    ///     Gets or sets the dotted prefix under which captures are also published while a nested struct that an
    ///     expression names through its path is written; <see langword="null"/> when none is active.
    /// </summary>
    string? QualifiedPrefix { get; set; }

    /// <summary>Whether a field's value is captured: an expression of the layout names it (or the operation captures every field).</summary>
    /// <param name="field">The field just encoded.</param>
    /// <returns>Whether <see cref="Capture"/> is called for it.</returns>
    bool Captures(CompiledField field);

    /// <summary>Captures a field's supplied value (an enum's exact number) by the shared rule and publishes it under the active prefix.</summary>
    /// <param name="name">The field's name.</param>
    /// <param name="field">The field, which decides whether the value is an integer at all.</param>
    /// <param name="value">The supplied value.</param>
    void Capture(string name, CompiledField field, object value);
}
