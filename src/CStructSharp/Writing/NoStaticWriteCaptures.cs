namespace CStructSharp.Writing;

using CStructSharp.Compilation;

/// <summary>
///     The capture sink of a direct root write: nothing is written after the root, so its fields publish no variables and
///     no qualified prefix is ever active.
/// </summary>
internal readonly struct NoStaticWriteCaptures : IStaticWriteCaptures
{
    /// <summary>Gets <see langword="null"/>; setting it does nothing.</summary>
    public string? QualifiedPrefix
    {
        get => null;
        set
        {
            // No later field reads a qualified name, so the prefix is not kept.
        }
    }

    /// <summary>Returns <see langword="false"/>: no field is captured.</summary>
    /// <param name="field">The field.</param>
    /// <returns><see langword="false"/>.</returns>
    public bool Captures(CompiledField field) => false;

    /// <summary>Does nothing; never called because <see cref="Captures"/> is false.</summary>
    /// <param name="name">The field's name.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The supplied value.</param>
    public void Capture(string name, CompiledField field, object value)
    {
    }
}
