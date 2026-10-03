namespace FastImpact;

using System.Diagnostics;
using BenchmarkDotNet.Engines;

/// <summary>Diagnostic-only wrapper around the original BDN factory, measuring setup without copying its engine.</summary>
public sealed class TimedFactory : IEngineFactory
{
    /// <summary>Gets accumulated fixture setup milliseconds.</summary>
    public static double SetupMs { get; private set; }

    /// <summary>Gets accumulated engine construction, setup and initial JIT milliseconds.</summary>
    public static double CreationMs { get; private set; }

    /// <summary>Gets wall time inside workload delegates, including allocation-diagnostic batches.</summary>
    public static double WorkloadMs { get; private set; }

    /// <summary>Gets collection pauses inside workload delegates; external pauses largely reflect forced collections.</summary>
    public static double WorkloadGcMs { get; private set; }

    /// <summary>Times global setup and delegates every engine decision to BenchmarkDotNet.</summary>
    /// <param name="engineParameters">The unmodified workload and configuration.</param>
    /// <returns>The standard BenchmarkDotNet engine.</returns>
    public IEngine CreateReadyToRun(EngineParameters engineParameters)
    {
        Action setup = engineParameters.GlobalSetupAction;

        // Isolate fixture initialization while preserving the engine's lifecycle order.
        engineParameters.GlobalSetupAction = () =>
        {
            long tick = Stopwatch.GetTimestamp();
            setup();
            SetupMs += Stopwatch.GetElapsedTime(tick).TotalMilliseconds;
        };
        engineParameters.WorkloadActionNoUnroll = Wrap(engineParameters.WorkloadActionNoUnroll);
        engineParameters.WorkloadActionUnroll = Wrap(engineParameters.WorkloadActionUnroll);
        long start = Stopwatch.GetTimestamp();
        IEngine engine = new EngineFactory().CreateReadyToRun(engineParameters);
        CreationMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        return engine;
    }

    /// <summary>Measures workload time and natural GC pauses at batch boundaries, outside the operation loop.</summary>
    /// <param name="action">The original emitted workload.</param>
    /// <returns>The instrumented batch delegate.</returns>
    private static Action<long> Wrap(Action<long> action)
    {
        // One timestamp pair per entire batch introduces diagnostic overhead, not a per-operation cost.
        return count =>
        {
            TimeSpan gc = GC.GetTotalPauseDuration();
            long start = Stopwatch.GetTimestamp();
            action(count);
            WorkloadMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            WorkloadGcMs += (GC.GetTotalPauseDuration() - gc).TotalMilliseconds;
        };
    }
}
