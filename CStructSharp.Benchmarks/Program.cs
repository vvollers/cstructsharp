namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Environments;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

internal static class Program
{
    public static int Main(string[] args)
    {
        string artifactsPath = Path.GetFullPath(
            Environment.GetEnvironmentVariable("CSTRUCTSHARP_BENCHMARK_ARTIFACTS") ??
            Path.Combine("artifacts", "baseline", "benchmarks"));
        string requestedJob = Environment.GetEnvironmentVariable("CSTRUCTSHARP_BENCHMARK_JOB") ?? "Short";

        Job job;
        if (requestedJob.Equals("Dry", StringComparison.OrdinalIgnoreCase))
        {
            job = Job.Dry.WithId("net10.0-dry");
        }
        else if (requestedJob.Equals("Gate", StringComparison.OrdinalIgnoreCase))
        {
            job = Job.Default
                     .WithRuntime(CoreRuntime.Core10_0)
                     .WithId("net10.0-release-gate")
                     .WithLaunchCount(3)
                     .WithWarmupCount(5)
                     .WithIterationCount(8)
                     .WithUnrollFactor(1);
        }
        else if (requestedJob.Equals("Short", StringComparison.OrdinalIgnoreCase))
        {
            job = Job.Default
                     .WithRuntime(CoreRuntime.Core10_0)
                     .WithId("net10.0-baseline")
                     .WithLaunchCount(1)
                     .WithWarmupCount(3)
                     .WithIterationCount(5)
                     .WithUnrollFactor(1);
        }
        else
        {
            Console.Error.WriteLine(
                $"Unknown CSTRUCTSHARP_BENCHMARK_JOB '{requestedJob}'. Expected Dry, Short, or Gate.");
            return 2;
        }

        var config = ManualConfig.Create(DefaultConfig.Instance)
                                 .AddJob(job)
                                 .AddDiagnoser(MemoryDiagnoser.Default)
                                 .AddExporter(JsonExporter.Full);
        config.ArtifactsPath = artifactsPath;

        IEnumerable<Summary> summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return summaries.SelectMany(summary => summary.Reports).Any(report => report.ResultStatistics is null) ? 1 : 0;
    }
}
