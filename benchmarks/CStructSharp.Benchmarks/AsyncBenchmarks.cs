namespace CStructSharp.Benchmarks;

using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Benchmarks.Baseline0;
using CStructSharp.Values;

/// <summary>
///     The awaitable stream forms against the synchronous stream form: <c>ParseAsync</c> over a memory stream that
///     exposes its buffer (read in place, completes synchronously), one that hides it (a pooled copy), and a file
///     opened for asynchronous I/O, for the record and the nested fixture; <c>WriteAsync</c> and <c>UpdateAsync</c>
///     against their stream forms for the record.
/// </summary>
[BenchmarkCategory("Async")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class AsyncBenchmarks
{
    private FixtureCase primRecord = null!;
    private FixtureCase nested = null!;
    private MemoryStream primRecordExposed = null!;
    private MemoryStream primRecordHidden = null!;
    private MemoryStream nestedExposed = null!;
    private MemoryStream nestedHidden = null!;
    private FileStream primRecordFile = null!;
    private string filePath = null!;
    private StructValue primRecordValue = null!;
    private MemoryStream writeTarget = null!;
    private MemoryStream updateTarget = null!;

    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.Load("prim-le-record");
        this.nested = FixtureCase.Load("nested-x256");
        this.primRecordExposed = new MemoryStream(this.primRecord.Bytes, writable: false);
        this.primRecordHidden = new MemoryStream(this.primRecord.Bytes, 0, this.primRecord.Bytes.Length, writable: false, publiclyVisible: false);
        this.nestedExposed = new MemoryStream(this.nested.Bytes, writable: false);
        this.nestedHidden = new MemoryStream(this.nested.Bytes, 0, this.nested.Bytes.Length, writable: false, publiclyVisible: false);
        this.filePath = Path.Combine(Path.GetTempPath(), $"cstructsharp-bench-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(this.filePath, this.primRecord.Bytes);
        this.primRecordFile = new FileStream(this.filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        this.primRecordValue = this.primRecord.Layout.Parse(this.primRecord.Bytes.AsSpan(), "root");
        this.writeTarget = new MemoryStream(new byte[this.primRecord.Bytes.Length]);
        this.updateTarget = new MemoryStream((byte[])this.primRecord.Bytes.Clone());
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.primRecordFile.Dispose();
        File.Delete(this.filePath);
    }

    // ---- prim-le-record ---------------------------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PrimRecord")]
    public StructValue Runtime_PrimRecord_ParseStream()
    {
        this.primRecordExposed.Position = 0;
        return this.primRecord.Layout.Parse(this.primRecordExposed, "root");
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecord", "ReleaseGate")]
    public ValueTask<StructValue> Runtime_PrimRecord_ParseAsync_MemoryStream()
    {
        this.primRecordExposed.Position = 0;
        return this.primRecord.Layout.ParseAsync(this.primRecordExposed, "root");
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public ValueTask<StructValue> Runtime_PrimRecord_ParseAsync_HiddenBuffer()
    {
        this.primRecordHidden.Position = 0;
        return this.primRecord.Layout.ParseAsync(this.primRecordHidden, "root");
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public ValueTask<StructValue> Runtime_PrimRecord_ParseAsync_File()
    {
        this.primRecordFile.Position = 0;
        return this.primRecord.Layout.ParseAsync(this.primRecordFile, "root");
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecordWrite")]
    public void Runtime_PrimRecord_WriteStream()
    {
        this.writeTarget.Position = 0;
        this.primRecord.Layout.Write(this.writeTarget, "root", this.primRecordValue);
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecordWrite")]
    public ValueTask Runtime_PrimRecord_WriteAsync()
    {
        this.writeTarget.Position = 0;
        return this.primRecord.Layout.WriteAsync(this.writeTarget, "root", this.primRecordValue);
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecordUpdate")]
    public void Runtime_PrimRecord_UpdateStream()
    {
        this.updateTarget.Position = 0;
        this.primRecord.Layout.Update(this.updateTarget, "root.c", 7u);
    }

    [Benchmark]
    [BenchmarkCategory("PrimRecordUpdate")]
    public ValueTask Runtime_PrimRecord_UpdateAsync()
    {
        this.updateTarget.Position = 0;
        return this.primRecord.Layout.UpdateAsync(this.updateTarget, "root.c", 7u);
    }

    // ---- nested-x256: 6,400 bytes --------------------------------------------------------------------------------------
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Nested256")]
    public StructValue Runtime_Nested256_ParseStream()
    {
        this.nestedExposed.Position = 0;
        return this.nested.Layout.Parse(this.nestedExposed, "root");
    }

    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public ValueTask<StructValue> Runtime_Nested256_ParseAsync_MemoryStream()
    {
        this.nestedExposed.Position = 0;
        return this.nested.Layout.ParseAsync(this.nestedExposed, "root");
    }

    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public ValueTask<StructValue> Runtime_Nested256_ParseAsync_HiddenBuffer()
    {
        this.nestedHidden.Position = 0;
        return this.nested.Layout.ParseAsync(this.nestedHidden, "root");
    }
}
