namespace CStructSharp.Benchmarks.Baseline0;

using BenchmarkDotNet.Attributes;

/// <summary>The 16 MiB stream case runs once per iteration because a single parse takes hundreds of milliseconds.</summary>
[BenchmarkCategory("Baseline0", "Stream", "Large")]
public class LargeStreamBenchmarks
{
    private FixtureCase fixture = null!;
    private MemoryStream memoryStream = null!;
    private BufferedStream bufferedStream = null!;
    private string tempPath = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.fixture = FixtureCase.Load("array-u8-16m-stream");
        this.memoryStream = new MemoryStream(this.fixture.Bytes, writable: false);
        this.tempPath = Path.Combine(Path.GetTempPath(), $"cstructsharp-bench-16m-{Environment.ProcessId}.bin");
        File.WriteAllBytes(this.tempPath, this.fixture.Bytes);
        this.bufferedStream = new BufferedStream(new FileStream(this.tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1), 1024 * 1024);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.memoryStream.Dispose();
        this.bufferedStream.Dispose();
        File.Delete(this.tempPath);
    }

    [Benchmark(Baseline = true)]
    [InvocationCount(1)]
    public object Parse16M_MemoryStream()
    {
        return this.fixture.Parse(this.memoryStream);
    }

    [Benchmark]
    [InvocationCount(1)]
    public object Parse16M_BufferedFileStream_1M()
    {
        return this.fixture.Parse(this.bufferedStream);
    }
}
