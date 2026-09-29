namespace CStructSharp.Engine;

/// <summary>One operation the engine declined, and why; recorded by <see cref="EngineDiagnostics"/>.</summary>
/// <param name="Operation">The kind of operation that was declined.</param>
/// <param name="Reason">The decline reason, a sentence fragment such as <see cref="EngineSelector.ExpressionInputs"/>.</param>
internal readonly record struct EngineDecline(EngineOperation Operation, string Reason);
