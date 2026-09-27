namespace CStructSharp.Benchmarks.Scenarios;

using BenchmarkDotNet.Attributes;

/// <summary>The 16 MiB stream case runs once per iteration because a single parse takes hundreds of milliseconds.</summary>
[BenchmarkCategory("Scenario", "Stream", "Large")]
public class LargeStreamBenchmarks
{
    private FixtureCase fixture = null!;
    private MemoryStream memoryStream = null!;
    private BufferedStream bufferedStream = null!;
    private string tempPath = null!;

    /// <summary>Loads the 16 MiB fixture and opens it as a memory stream and as a file behind a 1 MiB buffer.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.fixture = FixtureCase.Load("array-u8-16m-stream");
        this.memoryStream = new MemoryStream(this.fixture.Bytes, writable: false);
        this.tempPath = Path.Combine(Path.GetTempPath(), $"cstructsharp-bench-16m-{Environment.ProcessId}.bin");
        File.WriteAllBytes(this.tempPath, this.fixture.Bytes);
        this.bufferedStream = new BufferedStream(new FileStream(this.tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1), 1024 * 1024);
    }

    /// <summary>Disposes the streams and deletes the temporary file.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.memoryStream.Dispose();
        this.bufferedStream.Dispose();
        File.Delete(this.tempPath);
    }

    /// <summary>Parses the 16 MiB record from a memory stream.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    [InvocationCount(1)]
    public object Parse16M_MemoryStream()
    {
        return this.fixture.Parse(this.memoryStream);
    }

    /// <summary>Parses the same record from an unbuffered file stream wrapped in a 1 MiB <see cref="BufferedStream"/>.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    [InvocationCount(1)]
    public object Parse16M_BufferedFileStream_1M()
    {
        return this.fixture.Parse(this.bufferedStream);
    }
}
