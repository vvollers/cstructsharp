namespace CStructSharp.Tests;

using CStructSharp.Engine;

/// <summary>
///     One side of a differential comparison: an engine selection and an execution path applied to every option an
///     operation passes, and a fresh recorder that counts the selector's decisions while the harness runs this side
///     inside a recording (<see cref="EngineDiagnostics.Record"/>).
/// </summary>
internal sealed class EngineSide
{
    /// <summary>Creates a side.</summary>
    /// <param name="selection">The engine selection every operation of this side uses.</param>
    /// <param name="path">The execution path every operation of this side uses.</param>
    public EngineSide(EngineSelection selection, ExecutionPath path)
    {
        this.Selection = selection;
        this.Path = path;
    }

    /// <summary>Gets the engine selection every operation of this side uses.</summary>
    public EngineSelection Selection { get; }

    /// <summary>Gets the execution path every operation of this side uses.</summary>
    public ExecutionPath Path { get; }

    /// <summary>Gets the recorder of the decisions made while this side ran.</summary>
    public EngineDiagnostics Diagnostics { get; } = new();

    /// <summary>Returns <paramref name="options"/> (or the defaults) set to this side's selection and path.</summary>
    /// <param name="options">The case's read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The options this side passes.</returns>
    public ReadOptions Read(ReadOptions? options)
        => (options ?? new ReadOptions()) with { ExecutionPath = this.Path, EngineSelection = this.Selection, };

    /// <summary>Returns <paramref name="options"/> (or the defaults) set to this side's selection and path.</summary>
    /// <param name="options">The case's write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The options this side passes.</returns>
    public WriteOptions Write(WriteOptions? options)
        => (options ?? new WriteOptions()) with { ExecutionPath = this.Path, EngineSelection = this.Selection, };

    /// <summary>Returns <paramref name="options"/> (or the defaults) set to this side's selection and path.</summary>
    /// <param name="options">The case's update options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The options this side passes.</returns>
    public UpdateOptions Update(UpdateOptions? options)
        => (options ?? new UpdateOptions()) with { ExecutionPath = this.Path, EngineSelection = this.Selection, };
}
