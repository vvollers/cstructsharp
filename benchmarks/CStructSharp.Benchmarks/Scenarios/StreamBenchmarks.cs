namespace CStructSharp.Benchmarks.Scenarios;

using BenchmarkDotNet.Attributes;

/// <summary>S-STREAM: the same payload through memory, file, and buffered-file streams (all must be seekable).</summary>
[BenchmarkCategory("Scenario", "Stream")]
public class StreamBenchmarks
{
    private FixtureCase fixture = null!;
    private MemoryStream memoryStream = null!;
    private FileStream fileStream = null!;
    private BufferedStream bufferedStream = null!;
    private string tempPath = null!;

    /// <summary>Gets or sets the fixture id.</summary>
    [Params("array-u8-65536", "array-u8-1048576", "prim-le-x1k")]
    public string Fixture { get; set; } = null!;

    /// <summary>Loads the fixture, writes it to a temporary file, and opens the memory, unbuffered-file and buffered-file streams.</summary>
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

    /// <summary>Disposes the streams and deletes the temporary file.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.memoryStream.Dispose();
        this.fileStream.Dispose();
        this.bufferedStream.Dispose();
        File.Delete(this.tempPath);
    }

    /// <summary>Parses the fixture from a memory stream: the reference.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    public object Parse_MemoryStream()
    {
        return this.fixture.Parse(this.memoryStream);
    }

    /// <summary>Parses the fixture from a file stream with a one-byte buffer, so every read reaches the operating system.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    public object Parse_FileStream_Unbuffered()
    {
        return this.fixture.Parse(this.fileStream);
    }

    /// <summary>Parses the fixture from the same file behind a 64 KiB <see cref="BufferedStream"/>.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    public object Parse_BufferedFileStream_64K()
    {
        return this.fixture.Parse(this.bufferedStream);
    }
}
