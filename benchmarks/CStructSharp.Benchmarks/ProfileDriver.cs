namespace CStructSharp.Benchmarks;

using System.Diagnostics;
using CStructSharp.Benchmarks.Baseline0;

/// <summary>
///     Manual steady-state loop for sampling profilers (perf, dotnet-trace). Invoked as
///     <c>dotnet run -- --profile &lt;scenario&gt; [seconds]</c>; it warms for two seconds, then loops the scenario for
///     the requested duration so an external profiler can attach or wrap the process. Scenarios mirror the four
///     benchmarks named in the Phase 0 plan plus the JS-facing real-format parse.
/// </summary>
internal static class ProfileDriver
{
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

    private static Func<object> CreateAction(string scenario)
    {
        switch (scenario)
        {
        case "CompileSmall":
            {
                var benchmark = new CompilationBenchmarks();
                benchmark.Setup();
                return () => benchmark.CompileSmall();
            }

        case "CompileMedium":
            {
                var benchmark = new CompilationBenchmarks();
                benchmark.Setup();
                return () => benchmark.CompileMedium();
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
                var benchmark = new Baseline0.WriteBenchmarks();
                benchmark.Setup();
                return () => benchmark.Serialize_Nested256_Expando_ToArray();
            }

        case "SerializePocoToSpan":
            {
                var benchmark = new WriteAndUpdateBenchmarks();
                benchmark.Setup();
                return () => benchmark.SerializePocoToSpan();
            }

        case "ReadTypedNested256":
            {
                var benchmark = new Baseline0.PathAndTypedBenchmarks();
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
