namespace CStructSharp.Tests;

/// <summary>The outcome of one engine run that matched its golden outcome (<see cref="EngineDifferential.AssertGolden"/>).</summary>
/// <param name="Rendering">The canonical rendering of the operation's outcome, without the engine-run count.</param>
/// <param name="EngineRuns">How many operations reached the compiled engine during the run.</param>
internal sealed record EngineOutcome(string Rendering, long EngineRuns);
