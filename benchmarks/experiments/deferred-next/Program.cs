namespace DeferredNext;

using System.Buffers.Binary;
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
    /// <summary>Measures a first runtime matrix operation or generated bitfield parse, or reports generated reader IL size.</summary>
    /// <param name="args">The mode: matrix-read, matrix-write, bitfields, leaf, sizes, or allocations followed by an operation mode.</param>
    /// <returns>Zero after writing one JSON observation.</returns>
    /// <exception cref="ArgumentException">The requested operation mode is unknown.</exception>
    public static int Main(string[] args)
    {
        if (args[0] == "sizes")
        {
            foreach (Type type in new[] { typeof(BitfieldLayout) })
            {
                int bytes = 0;
                foreach (MethodInfo method in type.GetMethods(BindingFlags.NonPublic | BindingFlags.Static))
                {
                    if (method.Name.StartsWith("ReadRec", StringComparison.Ordinal))
                    {
                        bytes += method.GetMethodBody()?.GetILAsByteArray()?.Length ?? 0;
                    }
                }

                Console.WriteLine(JsonSerializer.Serialize(new { type = type.Name, readerIlBytes = bytes }));
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
        if (mode is "bitfields" or "leaf")
        {
            FixtureCase fixture = FixtureCase.LoadMatching("bitfield-x1k", typeof(BitfieldLayout));
            byte[] input = mode == "leaf" ? fixture.Bytes[..7] : fixture.Bytes;

            // The generated reader has not executed before the first observed call.
            operation = mode == "leaf" ? () => BitfieldLayout.ParseRec(input) : () => BitfieldLayout.Parse(input);
        }
        else
        {
            byte[] input = new byte[256 * 256 * 2];
            for (int index = 0; index < input.Length / 2; index++)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(index * 2), (ushort)((index * 29) + 17));
            }

            CStruct layout = FixtureCase.CompileLike(typeof(MaterializedLargeMatrix));
            if (mode == "matrix-read")
            {
                // Schema construction precedes the observation; lazy preparation remains part of the first parse.
                operation = () => layout.Parse(input, "root");
            }
            else if (mode == "matrix-write")
            {
                var value = layout.Parse(input, "root");

                // Prepare the same parsed value as the benchmark without calling a writer first.
                operation = () => layout.Serialize("root", value);
            }
            else
            {
                throw new ArgumentException("Unknown mode: " + mode);
            }
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
