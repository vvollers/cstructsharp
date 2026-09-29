namespace CStructSharp.Writing;

using CStructSharp.Compilation;
using CStructSharp.Expressions;

/// <summary>The capture sink of the interpreter's writer: its operation's variable dictionary and qualified prefix.</summary>
internal readonly struct WriterStateCaptures : IStaticWriteCaptures
{
    private readonly CStructElementWriterState state;

    /// <summary>Wraps the operation's state.</summary>
    /// <param name="state">The writer state whose variables receive the captures.</param>
    public WriterStateCaptures(CStructElementWriterState state)
    {
        this.state = state;
    }

    /// <summary>Gets or sets the state's active qualified prefix.</summary>
    public string? QualifiedPrefix
    {
        get => this.state.QualifiedPrefix;
        set => this.state.QualifiedPrefix = value;
    }

    /// <summary>Whether an expression names the field, or the operation captures every field.</summary>
    /// <param name="field">The field.</param>
    /// <returns>Whether the field is captured.</returns>
    public bool Captures(CompiledField field) => field.CapturesLayoutVariable || this.state.CaptureAllLayoutVariables;

    /// <summary>Captures the value into the dictionary (<see cref="LayoutVariableCapture"/>) and republishes it under the active prefix.</summary>
    /// <param name="name">The field's name.</param>
    /// <param name="field">The field.</param>
    /// <param name="value">The supplied value.</param>
    public void Capture(string name, CompiledField field, object value)
    {
        LayoutVariableCapture.Capture(this.state.Variables, name, field, value);
        this.state.PublishQualified(name);
    }
}
