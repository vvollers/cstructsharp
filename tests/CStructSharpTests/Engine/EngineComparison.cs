namespace CStructSharp.Tests;

using CStructSharp.Engine;

/// <summary>The result of one passed differential comparison.</summary>
/// <param name="Rendering">The canonical rendering, which matched the golden reference.</param>
/// <param name="Automatic">The recorder of the run that used automatic selection.</param>
internal sealed record EngineComparison(string Rendering, EngineDiagnostics Automatic);
