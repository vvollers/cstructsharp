namespace CStructSharp.Benchmarks;

using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using BenchmarkDotNet.Jobs;

/// <summary>Applies and records process-local controls for development measurements without changing machine settings.</summary>
internal static class DevelopmentEnvironment
{
    /// <summary>Pins this host when requested and records the actual runtime and affinity used for the measurement.</summary>
    /// <param name="job">The in-process Screen or Confirm job.</param>
    /// <param name="artifactsPath">The new run directory receiving its environment record.</param>
    /// <returns>The job with the same affinity as the host.</returns>
    /// <remarks>
    /// Auto selects the middle allowed logical CPU, without topology or load assumptions. Affinity limits this
    /// process's scheduling; it does not reserve the core or exclude work on its simultaneous-multithreading sibling.
    /// </remarks>
    /// <exception cref="ArgumentException">The CPU is invalid or outside the process's allowed mask.</exception>
    /// <exception cref="PlatformNotSupportedException">Explicit affinity is unavailable on this platform.</exception>
    /// <exception cref="InvalidOperationException">The operating system did not retain the requested affinity.</exception>
    public static Job Apply(Job job, string artifactsPath)
    {
        string requested = Environment.GetEnvironmentVariable("CSTRUCTSHARP_BENCHMARK_CPU") ?? "none";
        string? affinity = null;
        int? selectedCpu = null;
        if (OperatingSystem.IsWindows() || OperatingSystem.IsLinux())
        {
            using Process process = Process.GetCurrentProcess();
            ulong allowed = unchecked((ulong)process.ProcessorAffinity.ToInt64());
            if (requested != "none")
            {
                if (Environment.ProcessorCount > 64)
                {
                    throw new PlatformNotSupportedException("Development pinning supports one processor group; use --cpu none on machines with more than 64 logical CPUs.");
                }

                if (requested == "auto")
                {
                    var cpus = new List<int>();
                    for (int cpu = 0; cpu < (IntPtr.Size * 8) - 1; cpu++)
                    {
                        if ((allowed & (1UL << cpu)) != 0)
                        {
                            cpus.Add(cpu);
                        }
                    }

                    if (cpus.Count == 0)
                    {
                        throw new ArgumentException("No CPU representable by the supported affinity mask; use --cpu none.");
                    }

                    selectedCpu = cpus[cpus.Count / 2];
                }
                else if (int.TryParse(requested, NumberStyles.None, CultureInfo.InvariantCulture, out int cpu) &&
                         cpu >= 0 && cpu < (IntPtr.Size * 8) - 1 && (allowed & (1UL << cpu)) != 0)
                {
                    selectedCpu = cpu;
                }
                else
                {
                    throw new ArgumentException("CPU must be none, auto, or an allowed logical CPU index in the current processor group.");
                }

                nint mask = (nint)(1L << selectedCpu.Value);
                process.ProcessorAffinity = mask;
                if (process.ProcessorAffinity != mask)
                {
                    throw new InvalidOperationException("The requested CPU affinity was not applied.");
                }

                job = job.WithAffinity(mask);
            }

            affinity = unchecked((ulong)process.ProcessorAffinity.ToInt64()).ToString("X", CultureInfo.InvariantCulture);
        }
        else if (requested != "none" && requested != "auto")
        {
            throw new PlatformNotSupportedException("Explicit CPU affinity requires Windows or Linux; use --cpu none.");
        }

        Directory.CreateDirectory(artifactsPath);
        File.WriteAllText(Path.Combine(artifactsPath, "development-environment.json"), JsonSerializer.Serialize(new
        {
            protocol = 1,
            requestedCpu = requested,
            selectedCpu,
            affinity,
            runtime = RuntimeInformation.FrameworkDescription,
            architecture = RuntimeInformation.ProcessArchitecture.ToString(),
            os = RuntimeInformation.OSDescription,
            serverGc = GCSettings.IsServerGC,
            tiering = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
            timerHz = Stopwatch.Frequency,
            powerPlan = "unchanged",
        }));
        return job;
    }
}
