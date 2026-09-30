namespace CStructSharp.Tests;

using CStructSharp.Fuzzing;

/// <summary>
///     One operation the harness runs: <paramref name="Run"/> performs the call under an execution path on its own fresh
///     input and destination, and appends everything observable to the rendering - the result or failure, final stream
///     positions, returned counts, and destination contents.
/// </summary>
/// <param name="Name">The operation and its case, for failure messages and golden keys, such as <c>Parse(Span) root</c>.</param>
/// <param name="Run">
///     Runs the operation with every option it passes set to the given execution path (<see cref="ExecutionPaths.Read"/>,
///     <see cref="ExecutionPaths.Write"/>, <see cref="ExecutionPaths.Update"/>) and renders its outcome.
/// </param>
internal sealed record GoldenOperation(string Name, Action<ExecutionPath, CanonicalText> Run);
