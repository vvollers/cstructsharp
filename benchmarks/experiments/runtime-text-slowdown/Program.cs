namespace RuntimeTextSlowdown;

using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Benchmarks;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;
using CStructSharp.Values;

/// <summary>Observes sustained complete writes, JIT activity and GC pauses in separate fresh processes.</summary>
internal static class Program
{
    /// <summary>Writes existing fixture records in quarter-second windows and verifies complete output outside each window.</summary>
    /// <param name="args">Mode (runtime, tiny, unpadded, span or generated), followed by duration in seconds.</param>
    /// <returns>Zero after emitting all observations as JSON.</returns>
    /// <exception cref="ArgumentException">The mode or duration is unsupported.</exception>
    /// <exception cref="InvalidOperationException">Written bytes or owned-array independence differ.</exception>
    public static int Main(string[] args)
    {
        string mode = args[0];
        int seconds = int.Parse(args[1], System.Globalization.CultureInfo.InvariantCulture);
        if (seconds < 1 || seconds > 120 || mode is not ("runtime" or "tiny" or "unpadded" or "span" or "generated"))
        {
            throw new ArgumentException("Expected a supported mode and duration of 1..120 seconds.");
        }

        using Process process = Process.GetCurrentProcess();
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            process.ProcessorAffinity = (nint)(1L << 16);
        }

        int count = mode == "tiny" ? 1 : 256;
        byte[] expected = TextRecordBenchmarks.CreateInput(count);
        CStruct layout = FixtureCase.CompileLike(typeof(TextRecordLayout));
        StructValue value = layout.Parse(expected, "root", options: new ReadOptions { TrimFixedText = mode is "unpadded" or "span", });
        byte[] destination = new byte[expected.Length];
        TextRecordLayout.Root generated = TextRecordLayout.Parse(expected);

        // Keep complete serialization and output ownership inside every operation, just as in the existing benchmarks.
        Func<byte[]> operation = mode switch
        {
            "generated" => () => TextRecordLayout.Serialize(generated),
            "span" => () =>
            {
                layout.Serialize(destination.AsSpan(), "root", value);
                return destination;
            },
            _ => () => layout.Serialize("root", value),
        };
        byte[] first = operation();
        byte[] second = operation();
        if (!first.AsSpan().SequenceEqual(expected) || !second.AsSpan().SequenceEqual(expected) ||
            (mode != "span" && ReferenceEquals(first, second)))
        {
            throw new InvalidOperationException("Initial output or ownership differs.");
        }

        var samples = new List<object>();
        long origin = Stopwatch.GetTimestamp();
        for (int window = 0; window < seconds * 4; window++)
        {
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            TimeSpan jitBefore = JitInfo.GetCompilationTime();
            long ilBefore = JitInfo.GetCompiledILBytes();
            TimeSpan pauseBefore = GC.GetTotalPauseDuration();
            int gen0Before = GC.CollectionCount(0);
            int gen1Before = GC.CollectionCount(1);
            int gen2Before = GC.CollectionCount(2);
            process.Refresh();
            TimeSpan cpuBefore = process.TotalProcessorTime;
            long start = Stopwatch.GetTimestamp();
            int operations = 0;
            byte[] last;
            do
            {
                last = operation();
                for (int repeat = 1; repeat < 128; repeat++)
                {
                    last = operation();
                }

                operations += 128;
            }
            while (Stopwatch.GetElapsedTime(start).TotalMilliseconds < 250);

            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            double jitMs = (JitInfo.GetCompilationTime() - jitBefore).TotalMilliseconds;
            long ilBytes = JitInfo.GetCompiledILBytes() - ilBefore;
            double gcPauseMs = (GC.GetTotalPauseDuration() - pauseBefore).TotalMilliseconds;
            int gen0 = GC.CollectionCount(0) - gen0Before;
            int gen1 = GC.CollectionCount(1) - gen1Before;
            int gen2 = GC.CollectionCount(2) - gen2Before;
            process.Refresh();
            double cpuMs = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
            if (!last.AsSpan().SequenceEqual(expected))
            {
                throw new InvalidOperationException("Complete output differs after window " + window);
            }

            samples.Add(new
            {
                window, elapsedSeconds = Stopwatch.GetElapsedTime(origin).TotalSeconds, operations,
                nanoseconds = elapsedMs * 1_000_000 / operations, bytesPerOperation = (double)allocated / operations,
                jitMs, ilBytes, gcPauseMs, gen0, gen1, gen2, cpuMs, elapsedMs,
            });
        }

        Console.WriteLine(JsonSerializer.Serialize(new { mode, seconds, cpu = 16, samples }));
        return 0;
    }
}
