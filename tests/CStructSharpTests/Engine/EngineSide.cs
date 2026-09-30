namespace CStructSharp.Tests;

using CStructSharp.Engine;

/// <summary>
///     One run of a differential comparison: an execution path applied to every option an operation passes, and a fresh
///     recorder that counts the operations that reach the compiled engine while the harness runs this side inside a
///     recording (<see cref="EngineDiagnostics.Record"/>).
/// </summary>
internal sealed class EngineSide
{
    /// <summary>Creates a side.</summary>
    /// <param name="path">The execution path every operation of this side uses.</param>
    public EngineSide(ExecutionPath path)
    {
        this.Path = path;
    }

    /// <summary>Gets the execution path every operation of this side uses.</summary>
    public ExecutionPath Path { get; }

    /// <summary>Gets the recorder of the operations that reached the engine while this side ran.</summary>
    public EngineDiagnostics Diagnostics { get; } = new();

    /// <summary>Returns <paramref name="options"/> (or the defaults) set to this side's path.</summary>
    /// <param name="options">The case's read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The options this side passes.</returns>
    public ReadOptions Read(ReadOptions? options)
        => (options ?? new ReadOptions()) with { ExecutionPath = this.Path, };

    /// <summary>Returns <paramref name="options"/> (or the defaults) set to this side's path.</summary>
    /// <param name="options">The case's write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The options this side passes.</returns>
    public WriteOptions Write(WriteOptions? options)
        => (options ?? new WriteOptions()) with { ExecutionPath = this.Path, };

    /// <summary>Returns <paramref name="options"/> (or the defaults) set to this side's path.</summary>
    /// <param name="options">The case's update options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The options this side passes.</returns>
    public UpdateOptions Update(UpdateOptions? options)
        => (options ?? new UpdateOptions()) with { ExecutionPath = this.Path, };
}
