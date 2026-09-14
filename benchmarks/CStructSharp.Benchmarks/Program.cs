namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Engines;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

internal static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--profile")
        {
            return ProfileDriver.Run(args);
        }

        string artifactsPath = Path.GetFullPath(
            Environment.GetEnvironmentVariable("CSTRUCTSHARP_BENCHMARK_ARTIFACTS") ??
            Path.Combine("artifacts", "baseline", "benchmarks"));
        string requestedJob = Environment.GetEnvironmentVariable("CSTRUCTSHARP_BENCHMARK_JOB") ?? "Short";

        // Runtimes are selected independently of the job shape so the same case list can be measured on every
        // packaged target framework. The default stays net10.0 so existing release-gate runs are unchanged.
        string requestedRuntimes = Environment.GetEnvironmentVariable("CSTRUCTSHARP_BENCHMARK_RUNTIMES") ?? "net10.0";
        var runtimes = new List<Runtime>();
        foreach (string moniker in requestedRuntimes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            switch (moniker.ToLowerInvariant())
            {
            case "net8.0":
                runtimes.Add(CoreRuntime.Core80);
                break;
            case "net10.0":
                runtimes.Add(CoreRuntime.Core10_0);
                break;
            default:
                Console.Error.WriteLine(
                    $"Unknown CSTRUCTSHARP_BENCHMARK_RUNTIMES entry '{moniker}'. Expected net8.0 and/or net10.0.");
                return 2;
            }
        }

        var jobs = new List<Job>();
        foreach (Runtime runtime in runtimes)
        {
            string suffix = runtime.Name;
            Job job;
            if (requestedJob.Equals("Dry", StringComparison.OrdinalIgnoreCase))
            {
                job = Job.Dry.WithRuntime(runtime).WithId($"{suffix}-dry");
            }
            else if (requestedJob.Equals("Gate", StringComparison.OrdinalIgnoreCase))
            {
                job = Job.Default
                         .WithRuntime(runtime)
                         .WithId($"{suffix}-release-gate")
                         .WithLaunchCount(3)
                         .WithWarmupCount(5)
                         .WithIterationCount(8)
                         .WithUnrollFactor(1);
            }
            else if (requestedJob.Equals("Short", StringComparison.OrdinalIgnoreCase))
            {
                job = Job.Default
                         .WithRuntime(runtime)
                         .WithId($"{suffix}-baseline")
                         .WithLaunchCount(1)
                         .WithWarmupCount(3)
                         .WithIterationCount(5)
                         .WithUnrollFactor(1);
            }
            else if (requestedJob.Equals("ColdStart", StringComparison.OrdinalIgnoreCase))
            {
                // One measured invocation per fresh process: the first call pays JIT, type loading, and static
                // initialization. Five launches give five independent cold samples.
                job = Job.Default
                         .WithRuntime(runtime)
                         .WithId($"{suffix}-cold-start")
                         .WithStrategy(RunStrategy.ColdStart)
                         .WithLaunchCount(5)
                         .WithWarmupCount(0)
                         .WithIterationCount(1)
                         .WithInvocationCount(1)
                         .WithUnrollFactor(1);
            }
            else
            {
                Console.Error.WriteLine(
                    $"Unknown CSTRUCTSHARP_BENCHMARK_JOB '{requestedJob}'. Expected Dry, Short, Gate, or ColdStart.");
                return 2;
            }

            jobs.Add(job);
        }

        // Fixture ids longer than 20 characters were being abbreviated ("malfo(...)count") in the report keys the
        // contracts are keyed on; keep the full parameter text.
        var config = ManualConfig.Create(DefaultConfig.Instance)
                                 .AddDiagnoser(MemoryDiagnoser.Default)
                                 .AddExporter(JsonExporter.Full)
                                 .WithSummaryStyle(SummaryStyle.Default.WithMaxParameterColumnWidth(48));
        foreach (Job job in jobs)
        {
            config = config.AddJob(job);
        }

        // Opt-in CPU sampling: writes a .nettrace per benchmark next to the results for hot-path attribution.
        // Off by default because it slows every run and produces large artifacts.
        string? profile = Environment.GetEnvironmentVariable("CSTRUCTSHARP_BENCHMARK_PROFILE");
        if (string.Equals(profile, "cpu", StringComparison.OrdinalIgnoreCase))
        {
            config = config.AddDiagnoser(new EventPipeProfiler(EventPipeProfile.CpuSampling));
        }

        config.ArtifactsPath = artifactsPath;

        IEnumerable<Summary> summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return summaries.SelectMany(summary => summary.Reports).Any(report => report.ResultStatistics is null) ? 1 : 0;
    }
}
