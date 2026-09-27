namespace CStructSharp.Benchmarks;

using System.Diagnostics;
using CStructSharp.Benchmarks.Scenarios;

/// <summary>
///     Manual steady-state loop for sampling profilers (perf, dotnet-trace). Invoked as
///     <c>dotnet run -- --profile &lt;scenario&gt; [seconds]</c>; it warms for two seconds, then loops the scenario for
///     the requested duration so an external profiler can attach or wrap the process. Each scenario runs one benchmark
///     case: compilation, array, nested, conditional, runtime-count and real-format parses, typed reads and writes.
/// </summary>
internal static class ProfileDriver
{
    /// <summary>Runs the scenario named in the arguments: two seconds of warm-up, then the measured loop.</summary>
    /// <param name="args">The command line: <c>--profile</c>, the scenario (default <c>ParsePrimitiveArray1KiB</c>) and the seconds (default 10).</param>
    /// <returns>The process exit code, zero.</returns>
    /// <exception cref="ArgumentException">The scenario is unknown.</exception>
    public static int Run(string[] args)
    {
        string scenario = args.Length > 1 ? args[1] : "ParsePrimitiveArray1KiB";
        double seconds = args.Length > 2 ? double.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture) : 10;
        Func<object> action = CreateAction(scenario);

        Console.WriteLine($"profile: {scenario} pid={Environment.ProcessId} warming 2 s then running {seconds} s");
        RunFor(action, 2);
        long calls = RunFor(action, seconds);
        Console.WriteLine($"profile: {scenario} completed {calls} calls");
        return 0;
    }

    /// <summary>Calls an action repeatedly for a duration.</summary>
    /// <param name="action">The scenario's operation.</param>
    /// <param name="seconds">The wall-clock duration.</param>
    /// <returns>The number of calls made.</returns>
    private static long RunFor(Func<object> action, double seconds)
    {
        long calls = 0;
        object? sink = null;
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed.TotalSeconds < seconds)
        {
            sink = action();
            calls++;
        }

        GC.KeepAlive(sink);
        return calls;
    }

    /// <summary>Sets up the benchmark class behind a scenario and returns its operation.</summary>
    /// <param name="scenario">The scenario name.</param>
    /// <returns>The operation to loop.</returns>
    /// <exception cref="ArgumentException">The scenario is unknown.</exception>
    private static Func<object> CreateAction(string scenario)
    {
        switch (scenario)
        {
        case "CompileSmall":
            {
                var benchmark = new Scenarios.CompileBenchmarks { Fixture = "compile-small", };
                benchmark.Setup();
                return () => benchmark.Compile();
            }

        case "CompileMedium":
            {
                var benchmark = new Scenarios.CompileBenchmarks { Fixture = "compile-medium-128", };
                benchmark.Setup();
                return () => benchmark.Compile();
            }

        case "ParsePrimitiveArray1KiB":
            {
                var benchmark = new ReadBenchmarks();
                benchmark.Setup();
                return () => benchmark.ParsePrimitiveArray1KiB();
            }

        case "ParseNestedUnaligned":
            {
                var benchmark = new ReadBenchmarks();
                benchmark.Setup();
                return () => benchmark.ParseNestedUnaligned();
            }

        case "SerializeNested256":
            {
                var benchmark = new Scenarios.WriteBenchmarks();
                benchmark.Setup();
                return () => benchmark.Serialize_Nested256_Expando_ToArray();
            }

        case "SerializePocoToSpan":
            {
                var benchmark = new Scenarios.WriteBenchmarks();
                benchmark.Setup();
                return () => benchmark.Serialize_Prim_Poco_ToSpan();
            }

        case "ReadTypedNested256":
            {
                var benchmark = new Scenarios.PathAndTypedBenchmarks();
                benchmark.Setup();
                return () => benchmark.ReadTyped_Nested256();
            }

        case "ParseCondIf128":
            {
                FixtureCase fixture = FixtureCase.Load("cond-if128");
                return () => fixture.ParseSpan();
            }

        case "ParseDynamic1024":
            {
                FixtureCase fixture = FixtureCase.Load("dynamic-1024");
                return () => fixture.ParseSpan();
            }

        case "ParseRealPng":
            {
                FixtureCase fixture = FixtureCase.Load("real-png");
                return () => fixture.ParseSpan();
            }

        case "ParseArrayU32Be":
            {
                FixtureCase fixture = FixtureCase.Load("array-u32-be-16384");
                return () => fixture.ParseSpan();
            }

        default:
            throw new ArgumentException("Unknown profile scenario: " + scenario);
        }
    }
}
