namespace CStructSharp.Benchmarks.Baseline0;

using BenchmarkDotNet.Attributes;

/// <summary>S-STREAM: the same payload through memory, file, and buffered-file streams (all must be seekable).</summary>
[BenchmarkCategory("Baseline0", "Stream")]
public class StreamBenchmarks
{
    private FixtureCase fixture = null!;
    private MemoryStream memoryStream = null!;
    private FileStream fileStream = null!;
    private BufferedStream bufferedStream = null!;
    private string tempPath = null!;

    [Params("array-u8-65536", "array-u8-1048576", "prim-le-x1k")]
    public string Fixture { get; set; } = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.fixture = FixtureCase.Load(this.Fixture);
        this.memoryStream = new MemoryStream(this.fixture.Bytes, writable: false);
        this.tempPath = Path.Combine(Path.GetTempPath(), $"cstructsharp-bench-{this.Fixture}-{Environment.ProcessId}.bin");
        File.WriteAllBytes(this.tempPath, this.fixture.Bytes);
        this.fileStream = new FileStream(this.tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1);
        this.bufferedStream = new BufferedStream(new FileStream(this.tempPath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 1), 64 * 1024);
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.memoryStream.Dispose();
        this.fileStream.Dispose();
        this.bufferedStream.Dispose();
        File.Delete(this.tempPath);
    }

    [Benchmark(Baseline = true)]
    public object Parse_MemoryStream()
    {
        return this.fixture.Parse(this.memoryStream);
    }

    [Benchmark]
    public object Parse_FileStream_Unbuffered()
    {
        return this.fixture.Parse(this.fileStream);
    }

    [Benchmark]
    public object Parse_BufferedFileStream_64K()
    {
        return this.fixture.Parse(this.bufferedStream);
    }
}
