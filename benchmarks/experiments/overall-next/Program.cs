namespace OverallNext;

using System.Diagnostics;
using System.Reflection;
using System.Runtime;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;

/// <summary>Records one first operation per process, preserving full owned outputs outside the timed region.</summary>
internal static class Program
{
    /// <summary>Measures a first nested runtime parse or generated matrix write, or reports generated writer IL size.</summary>
    /// <param name="args">The mode: nested, matrix, sizes, or allocations followed by nested or matrix.</param>
    /// <returns>Zero after writing one JSON observation.</returns>
    public static int Main(string[] args)
    {
        if (args[0] == "sizes")
        {
            foreach (Type type in new[] { typeof(MaterializedSmallMatrix), typeof(MaterializedLargeMatrix), typeof(MaterializedBigMatrix) })
            {
                int bytes = 0;
                foreach (MethodInfo method in type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (method.Name.StartsWith("Encode", StringComparison.Ordinal))
                    {
                        bytes += method.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0;
                    }
                }

                Console.WriteLine(JsonSerializer.Serialize(new { type = type.Name, writerIlBytes = bytes }));
            }

            return 0;
        }

        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            Process.GetCurrentProcess().ProcessorAffinity = (nint)(1L << 16);
        }

        bool allocationOnly = args[0] == "allocations";
        string mode = allocationOnly ? args[1] : args[0];
        Func<object> operation;
        if (mode == "nested")
        {
            // Fixture loading compiles the schema but does not parse; lazy plan preparation remains in the observation.
            FixtureCase fixture = FixtureCase.Load("nested-x256");
            operation = fixture.ParseSpan;
        }
        else
        {
            var rows = new ushort[256][];
            for (int row = 0; row < rows.Length; row++)
            {
                rows[row] = new ushort[256];
                for (int column = 0; column < rows[row].Length; column++)
                {
                    rows[row][column] = (ushort)((((row * 256) + column) * 29) + 17);
                }
            }

            var value = new MaterializedLargeMatrix.Root { Grid = rows };

            // Construct the same input as the benchmark without calling any writer before its first observation.
            operation = () => MaterializedLargeMatrix.Serialize(value);
        }

        if (allocationOnly)
        {
            for (int index = 0; index < 20; index++)
            {
                GC.KeepAlive(operation());
            }

            long allocationStart = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 128; index++)
            {
                GC.KeepAlive(operation());
            }

            long totalAllocated = GC.GetAllocatedBytesForCurrentThread() - allocationStart;
            Console.WriteLine(JsonSerializer.Serialize(new { name = mode, bytesPerOperation = totalAllocated / 128.0 }));
            return 0;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread();
        TimeSpan jit = JitInfo.GetCompilationTime();
        long il = JitInfo.GetCompiledILBytes();
        long start = Stopwatch.GetTimestamp();
        object result = operation();
        double milliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        long bytesAllocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        double jitMilliseconds = (JitInfo.GetCompilationTime() - jit).TotalMilliseconds;
        long ilBytes = JitInfo.GetCompiledILBytes() - il;
        GC.KeepAlive(result);
        Console.WriteLine(JsonSerializer.Serialize(new { name = args[0], milliseconds, allocated = bytesAllocated, jitMilliseconds, ilBytes }));
        return 0;
    }
}
