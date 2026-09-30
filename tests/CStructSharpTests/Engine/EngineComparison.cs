namespace CStructSharp.Tests;

using CStructSharp.Engine;

/// <summary>The result of one passed differential comparison.</summary>
/// <param name="Rendering">The canonical rendering, which matched the golden reference.</param>
/// <param name="Diagnostics">The recorder of the run: how many operations reached the compiled engine.</param>
internal sealed record EngineComparison(string Rendering, EngineDiagnostics Diagnostics);
