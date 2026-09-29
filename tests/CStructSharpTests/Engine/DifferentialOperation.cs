namespace CStructSharp.Tests;

using CStructSharp.Fuzzing;

/// <summary>
///     One operation a differential comparison runs once per side: <paramref name="Run"/> performs the call with the
///     side's options on its own fresh input and destination, and appends everything observable to the rendering -
///     the result or failure, final stream positions, returned counts, and destination contents.
/// </summary>
/// <param name="Name">The operation and its case, for failure messages, such as <c>Parse(Span) root</c>.</param>
/// <param name="Run">Runs the operation for one side and renders its outcome.</param>
internal sealed record DifferentialOperation(string Name, Action<EngineSide, CanonicalText> Run);
