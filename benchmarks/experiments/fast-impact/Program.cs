namespace FastImpact;

using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Filters;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using BenchmarkDotNet.Toolchains.CsProj;
using BenchmarkDotNet.Toolchains.InProcess.Emit;
using CStructSharp.Benchmarks;
using Perfolizer.Horology;

/// <summary>Research-only worker and configurable BenchmarkDotNet host; all operations reuse existing cases.</summary>
internal static class Program
{
    /// <summary>Runs a JSON-lines worker or an isolated BenchmarkDotNet configuration.</summary>
    /// <param name="args">Worker, audit, or one of the BDN modes documented in the research README.</param>
    /// <returns>Zero on success; exceptions fail the process.</returns>
    public static int Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("PERF_CPU") is { } requestedCpu && requestedCpu != "none")
        {
            int cpu = int.Parse(requestedCpu);
            if (cpu < 0 || cpu >= 63)
            {
                throw new ArgumentOutOfRangeException(nameof(args), "PERF_CPU must be in 0..62.");
            }

            if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
            {
                Process.GetCurrentProcess().ProcessorAffinity = (nint)(1L << cpu);
            }
            else
            {
                throw new PlatformNotSupportedException("The affinity experiment requires Windows or Linux.");
            }
        }

        if (args[0] == "audit")
        {
            HarnessProbe.Audit();
            return 0;
        }

        if (args[0].Contains("bdn", StringComparison.Ordinal))
        {
            return Bdn(args);
        }

        var clock = Stopwatch.StartNew();
        List<Case> cases = Discover(args.Contains("controls", StringComparer.Ordinal));
        double discoveryMs = clock.Elapsed.TotalMilliseconds;
        double setupMs = 0;
        double firstCallMs = 0;
        foreach (Case item in cases)
        {
            long start = Stopwatch.GetTimestamp();
            item.Setup();
            setupMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            start = Stopwatch.GetTimestamp();
            item.Run(1);
            firstCallMs += Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        }

        Emit(new
        {
            ready = true, cases = cases.Select(Case.Identity), discoveryMs, setupMs, firstCallMs,
            jitMs = JitInfo.GetCompilationTime().TotalMilliseconds, readyMs = clock.Elapsed.TotalMilliseconds,
            timerHz = Stopwatch.Frequency, pid = Environment.ProcessId, runtime = Environment.Version.ToString(),
        });
        try
        {
            while (Console.ReadLine() is { } line)
            {
                using JsonDocument command = JsonDocument.Parse(line);
                JsonElement root = command.RootElement;
                string op = root.GetProperty("op").GetString()!;
                if (op == "quit")
                {
                    break;
                }

                if (op == "prepare")
                {
                    double targetMs = root.GetProperty("ms").GetDouble();
                    foreach (Case item in cases)
                    {
                        item.Calibrate(targetMs);
                    }

                    Emit(cases.Select(Case.Calibration));
                }
                else if (op == "batch")
                {
                    Case item = cases[root.GetProperty("index").GetInt32()];
                    long count = root.GetProperty("count").GetInt64();
                    Emit(item.Measure(count, root.TryGetProperty("gc", out JsonElement gc) && gc.GetBoolean()));
                }
                else if (op == "gc")
                {
                    long start = Stopwatch.GetTimestamp();
                    Collect();
                    Emit(new { gcMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds });
                }
                else
                {
                    throw new InvalidOperationException("Unknown command.");
                }
            }
        }
        finally
        {
            foreach (Case item in cases)
            {
                item.Cleanup();
            }
        }

        return 0;
    }

    /// <summary>Runs existing methods with controlled timing and GC settings, optionally instrumenting setup.</summary>
    /// <param name="args">Mode, milliseconds per iteration, iterations, force-GC, memory, artifact path.</param>
    /// <returns>One if any case fails, otherwise zero.</returns>
    private static int Bdn(string[] args)
    {
        var job = Job.Default.WithToolchain(InProcessEmitToolchain.Instance).WithLaunchCount(1)
            .WithWarmupCount(args[2] == "5" ? 1 : 3).WithIterationCount(int.Parse(args[2]))
            .WithIterationTime(TimeInterval.FromMilliseconds(double.Parse(args[1])))
            .WithEvaluateOverhead(false).WithGcForce(bool.Parse(args[3])).WithId("experiment");
        if (args.Contains("oop", StringComparer.Ordinal))
        {
            job = job.WithToolchain(CsProjCoreToolchain.NetCoreApp10_0);
        }

        if (args.Contains("user-power", StringComparer.Ordinal))
        {
            job = job.WithPowerPlan(PowerPlan.UserPowerPlan);
        }

        if (args.Contains("profile", StringComparer.Ordinal))
        {
            job = job.WithEngineFactory(new TimedFactory());
        }

        var config = ManualConfig.Create(DefaultConfig.Instance).AddJob(job).AddExporter(JsonExporter.Full);
        if (args.Contains("packet-only", StringComparer.Ordinal))
        {
            // An in-process confirmation must execute the chosen bundle, without rebuilding a different checkout.
            config.AddFilter(new SimpleFilter(c => c.Descriptor.Type == typeof(PacketBenchmarks) && c.Descriptor.WorkloadMethod.Name == "ParseSpan"));
        }

        if (args[0].StartsWith("controls", StringComparison.Ordinal))
        {
            string method = args[0].Contains("light", StringComparison.Ordinal) ? "ParseLight" : "Parse";

            // Separate the two validation costs without changing their fixtures or parameter expansion.
            config.AddFilter(new SimpleFilter(c => c.Descriptor.WorkloadMethod.Name == method));
        }

        if (bool.Parse(args[4]))
        {
            config.AddDiagnoser(MemoryDiagnoser.Default);
        }

        config.ArtifactsPath = args[5];
        var phases = new PhaseLogger();
        if (args.Contains("profile", StringComparer.Ordinal))
        {
            config.AddLogger(phases);
        }

        var clock = Stopwatch.StartNew();

        // Match the switcher's assembly discovery while excluding generated helper types.
        Type[] types = args[0].StartsWith("controls", StringComparison.Ordinal) ? [typeof(Controls)] : typeof(ImpactParseBenchmarks).Assembly.GetTypes()
            .Where(t => t.IsPublic && !t.IsAbstract && t.GetMethods().Any(m => m.IsDefined(typeof(BenchmarkAttribute)))).ToArray();
        string[] filters = args[0].StartsWith("controls", StringComparison.Ordinal)
            ? ["--filter", "*"]
            : ["--filter", "*", "--anyCategories", "Impact"];
        var summaries = BenchmarkSwitcher.FromTypes(types).Run(filters, config);
        if (args.Contains("profile", StringComparer.Ordinal))
        {
            File.WriteAllText(Path.Combine(args[5], "phases.json"), JsonSerializer.Serialize(phases.Events));
        }

        Console.WriteLine(JsonSerializer.Serialize(new
        {
            profile = true, totalMs = clock.Elapsed.TotalMilliseconds,
            setupMs = TimedFactory.SetupMs, engineCreationMs = TimedFactory.CreationMs,
            workloadMs = TimedFactory.WorkloadMs, workloadGcMs = TimedFactory.WorkloadGcMs,
            jitMs = JitInfo.GetCompilationTime().TotalMilliseconds, gcPauseMs = GC.GetTotalPauseDuration().TotalMilliseconds,
        }));

        // A missing report or statistics must fail instead of looking like a fast successful run.
        return !summaries.Any() || summaries.SelectMany(s => s.Reports).Any(r => r.ResultStatistics is null) ? 1 : 0;
    }

    /// <summary>Uses BenchmarkDotNet's own parameter expansion and category discovery, rejecting unsupported hooks.</summary>
    /// <param name="controls">Whether to use the equivalent-output validation controls.</param>
    /// <returns>The ordered operation list, without executing setup.</returns>
    private static List<Case> Discover(bool controls)
    {
        var config = ManualConfig.CreateEmpty().AddJob(Job.Dry);
        Type[] types = controls ? [typeof(Controls)] : typeof(ImpactParseBenchmarks).Assembly.GetTypes();
        var result = new List<Case>();
        foreach (Type type in types)
        {
            // Ignore helper types before asking BDN to expand methods and parameters.
            if (!type.IsPublic || !type.GetMethods().Any(m => m.IsDefined(typeof(BenchmarkAttribute))))
            {
                continue;
            }

            foreach (BenchmarkCase benchmark in BenchmarkConverter.TypeToBenchmarks(type, config).BenchmarksCases)
            {
                if (!controls && !benchmark.Descriptor.Categories.Contains("Impact", StringComparer.Ordinal))
                {
                    continue;
                }

                result.Add(new Case(benchmark));
            }
        }

        // Stable identities are checked across both workers by the orchestrator.
        return result.OrderBy(c => c.Id, StringComparer.Ordinal).ToList();
    }

    /// <summary>Writes one protocol response outside timed operations.</summary>
    /// <param name="value">The JSON-serializable response.</param>
    private static void Emit(object value) => Console.WriteLine(JsonSerializer.Serialize(value));

    /// <summary>Collects and waits for finalizers using the same sequence as BenchmarkDotNet.</summary>
    internal static void Collect()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }
}
