namespace CStructSharp.Comparison;

using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Diagnosers;
using BenchmarkDotNet.Exporters.Json;
using BenchmarkDotNet.Reports;
using BenchmarkDotNet.Running;

/// <summary>
///     Entry point. <c>--verify [--sizes &lt;path&gt;]</c> checks every case once without measuring (and records the
///     encoded sizes); any other arguments go to BenchmarkDotNet, for example
///     <c>--filter * --job short --artifacts &lt;directory&gt;</c>.
/// </summary>
internal static class Program
{
    /// <summary>Runs verification or the benchmarks.</summary>
    /// <param name="args">The command-line arguments.</param>
    /// <returns>0 on success; 1 when a check fails or a benchmark produced no result.</returns>
    public static int Main(string[] args)
    {
        if (args.Contains("--verify"))
        {
            int sizesIndex = Array.IndexOf(args, "--sizes");
            return Verification.Run(sizesIndex >= 0 && sizesIndex + 1 < args.Length ? args[sizesIndex + 1] : null);
        }

        // Allocation per operation is a table column, and the full JSON export is what the README renderer reads.
        ManualConfig config = ManualConfig.Create(DefaultConfig.Instance)
                                          .AddDiagnoser(MemoryDiagnoser.Default)
                                          .AddExporter(JsonExporter.Full);
        IEnumerable<Summary> summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args, config);
        return summaries.SelectMany(summary => summary.Reports).Any(report => report.ResultStatistics is null) ? 1 : 0;
    }
}
