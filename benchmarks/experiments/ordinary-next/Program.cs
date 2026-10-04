namespace OrdinaryNext;

using System.Diagnostics;
using System.Runtime;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Benchmarks;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;
using CStructSharp.Values;

/// <summary>Records first operations and warmed allocation without moving schema preparation into the parse measurements.</summary>
internal static class Program
{
    /// <summary>Executes exactly one cold operation or a warmed allocation batch and verifies complete results outside timing.</summary>
    /// <param name="args">The operation: read-1, read-256, write-256 or compile; prefix with allocations for a warmed batch.</param>
    /// <returns>Zero after emitting one JSON observation.</returns>
    /// <exception cref="ArgumentException">The requested mode is unknown.</exception>
    public static int Main(string[] args)
    {
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            Process.GetCurrentProcess().ProcessorAffinity = (nint)(1L << 16);
        }

        bool allocations = args[0] == "allocations";
        string mode = allocations ? args[1] : args[0];
        int count = mode == "read-1" ? 1 : 256;
        byte[] input = TextRecordBenchmarks.CreateInput(count);
        Func<object> operation;
        if (mode == "compile")
        {
            // First schema preparation includes all lazily initialized compilation helpers.
            operation = () => FixtureCase.CompileLike(typeof(TextRecordLayout));
        }
        else
        {
            CStruct layout = FixtureCase.CompileLike(typeof(TextRecordLayout));
            if (mode is "read-1" or "read-256")
            {
                // Schema construction is outside the boundary; lazy read-program preparation stays inside it.
                operation = () => layout.Parse(input, "root");
            }
            else if (mode == "write-256")
            {
                StructValue value = layout.Parse(input, "root");

                // Preparing the complete write value does not run a writer.
                operation = () => layout.Serialize("root", value);
            }
            else
            {
                throw new ArgumentException("Unknown mode: " + mode);
            }
        }

        if (allocations)
        {
            for (int repeat = 0; repeat < 32; repeat++)
            {
                GC.KeepAlive(operation());
            }

            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            for (int repeat = 0; repeat < 128; repeat++)
            {
                GC.KeepAlive(operation());
            }

            long total = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            Console.WriteLine(JsonSerializer.Serialize(new { mode, bytesPerOperation = total / 128.0 }));
            return 0;
        }

        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        TimeSpan jitBefore = JitInfo.GetCompilationTime();
        long ilBefore = JitInfo.GetCompiledILBytes();
        long start = Stopwatch.GetTimestamp();
        object result = operation();
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        double jitMilliseconds = (JitInfo.GetCompilationTime() - jitBefore).TotalMilliseconds;
        long ilBytes = JitInfo.GetCompiledILBytes() - ilBefore;
        if (result is StructValue parsed)
        {
            TextRecordBenchmarks.Verify(parsed, count);
        }
        else if (result is byte[] bytes && !bytes.AsSpan().SequenceEqual(input))
        {
            throw new InvalidOperationException("Cold write did not preserve every input byte.");
        }

        Console.WriteLine(JsonSerializer.Serialize(new { mode, milliseconds, allocated, jitMilliseconds, ilBytes }));
        return 0;
    }
}
