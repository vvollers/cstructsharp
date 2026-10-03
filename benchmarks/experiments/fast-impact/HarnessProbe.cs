namespace FastImpact;

using System.Diagnostics;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;

/// <summary>Measures adapter overhead and demonstrates allocations on an asynchronous continuation thread.</summary>
public class HarnessProbe
{
    private double value = 1;

    /// <summary>Reports nanoseconds of loop/consumer overhead and cross-thread allocation-counter differences.</summary>
    public static void Audit()
    {
        var config = ManualConfig.CreateEmpty().AddJob(Job.Dry);
        var item = new Case(BenchmarkConverter.TypeToBenchmarks(typeof(HarnessProbe), config).BenchmarksCases.Single());
        item.Run(100_000);
        var samples = new List<double>();
        for (int repeat = 0; repeat < 7; repeat++)
        {
            long start = Stopwatch.GetTimestamp();
            item.Run(10_000_000);
            samples.Add(Stopwatch.GetElapsedTime(start).TotalNanoseconds / 10_000_000);
        }

        AllocateElsewhere();
        long total = GC.GetTotalAllocatedBytes(true);
        long local = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 100; i++)
        {
            AllocateElsewhere();
        }

        long localDelta = GC.GetAllocatedBytesForCurrentThread() - local;
        long totalDelta = GC.GetTotalAllocatedBytes(true) - total;
        Console.WriteLine(JsonSerializer.Serialize(new { overheadNs = samples, asyncThreadBytes = localDelta, asyncTotalBytes = totalDelta }));
    }

    /// <summary>Returns a field without allocating; the worker still consumes the result.</summary>
    /// <returns>The probe value.</returns>
    [Benchmark]
    public double Read() => this.value;

    /// <summary>Waits for a real thread-pool allocation; the caller's counter cannot observe its payload.</summary>
    private static void AllocateElsewhere()
    {
        // The returned array is consumed after the continuation completes, so its allocation cannot disappear.
        byte[] bytes = Task.Run(() => new byte[4096]).GetAwaiter().GetResult();
        Sink<byte[]>.Consume(bytes);
    }
}
