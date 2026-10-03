namespace FastImpact;

using System.Diagnostics;
using BenchmarkDotNet.Loggers;

/// <summary>Retains benchmark phase markers with elapsed milliseconds for diagnostic accounting.</summary>
internal sealed class PhaseLogger : ILogger
{
    private readonly Stopwatch clock = Stopwatch.StartNew();
    private readonly List<object> events = [];

    /// <inheritdoc/>
    public string Id => "ResearchPhaseLogger";

    /// <inheritdoc/>
    public int Priority => 0;

    /// <summary>Gets phase markers; per-iteration output is intentionally omitted.</summary>
    public IReadOnlyList<object> Events => this.events;

    /// <inheritdoc/>
    public void Write(LogKind logKind, string text)
    {
        if (text.StartsWith("//", StringComparison.Ordinal) || text.StartsWith("Global total", StringComparison.Ordinal) ||
            text.StartsWith("Setup power plan", StringComparison.Ordinal) || text.StartsWith("Successfully reverted", StringComparison.Ordinal))
        {
            this.events.Add(new { ms = this.clock.Elapsed.TotalMilliseconds, text });
        }
    }

    /// <inheritdoc/>
    public void WriteLine(LogKind logKind, string text) => this.Write(logKind, text);

    /// <inheritdoc/>
    public void WriteLine()
    {
    }

    /// <inheritdoc/>
    public void Flush()
    {
    }
}
