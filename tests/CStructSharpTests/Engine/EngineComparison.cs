namespace CStructSharp.Tests;

using CStructSharp.Engine;

/// <summary>The result of one passed differential comparison.</summary>
/// <param name="Rendering">The canonical rendering both sides produced.</param>
/// <param name="Interpreter">The recorder of the side that forced the interpreter.</param>
/// <param name="Automatic">The recorder of the side that used automatic selection.</param>
internal sealed record EngineComparison(string Rendering, EngineDiagnostics Interpreter, EngineDiagnostics Automatic);
