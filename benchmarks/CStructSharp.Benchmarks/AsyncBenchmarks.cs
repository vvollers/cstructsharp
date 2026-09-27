namespace CStructSharp.Benchmarks;

using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;
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
    private PrimRecordLayout.Root generatedRecord = null!;

    /// <summary>
    ///     Loads the record and nested fixtures, opens the exposed, hidden-buffer and file streams, and prepares the values
    ///     and targets the write and update cases use.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.primRecord = FixtureCase.LoadMatching("prim-le-record", typeof(PrimRecordLayout));
        this.nested = FixtureCase.LoadMatching("nested-x256", typeof(NestedLayout));
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
        this.generatedRecord = PrimRecordLayout.Parse(this.primRecord.Bytes);
    }

    /// <summary>Closes the file stream and deletes its temporary file.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.primRecordFile.Dispose();
        File.Delete(this.filePath);
    }

    // ---- prim-le-record ---------------------------------------------------------------------------------------------

    /// <summary>Runtime <c>Parse(Stream)</c> of the 28-byte record from a memory stream: the synchronous reference.</summary>
    /// <returns>The parsed record.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("PrimRecord")]
    public StructValue Runtime_PrimRecord_ParseStream()
    {
        this.primRecordExposed.Position = 0;
        return this.primRecord.Layout.Parse(this.primRecordExposed, "root");
    }

    /// <summary>Runtime <c>ParseAsync</c> of a memory stream that exposes its buffer, so the bytes are read in place.</summary>
    /// <returns>The parse, already complete.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "PrimRecord", "Gate")]
    public ValueTask<StructValue> Runtime_PrimRecord_ParseAsync_MemoryStream()
    {
        this.primRecordExposed.Position = 0;
        return this.primRecord.Layout.ParseAsync(this.primRecordExposed, "root");
    }

    /// <summary>Runtime <c>ParseAsync</c> of a memory stream that hides its buffer, so the bytes are copied into a pooled buffer.</summary>
    /// <returns>The parse.</returns>
    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public ValueTask<StructValue> Runtime_PrimRecord_ParseAsync_HiddenBuffer()
    {
        this.primRecordHidden.Position = 0;
        return this.primRecord.Layout.ParseAsync(this.primRecordHidden, "root");
    }

    /// <summary>Runtime <c>ParseAsync</c> of a file opened for asynchronous I/O.</summary>
    /// <returns>The parse.</returns>
    [Benchmark]
    [BenchmarkCategory("PrimRecord")]
    public ValueTask<StructValue> Runtime_PrimRecord_ParseAsync_File()
    {
        this.primRecordFile.Position = 0;
        return this.primRecord.Layout.ParseAsync(this.primRecordFile, "root");
    }

    /// <summary>Runtime <c>Write</c> of the record to a memory stream.</summary>
    [Benchmark]
    [BenchmarkCategory("PrimRecordWrite")]
    public void Runtime_PrimRecord_WriteStream()
    {
        this.writeTarget.Position = 0;
        this.primRecord.Layout.Write(this.writeTarget, "root", this.primRecordValue);
    }

    /// <summary>Runtime <c>WriteAsync</c> of the record to the same memory stream.</summary>
    /// <returns>The write.</returns>
    [Benchmark]
    [BenchmarkCategory("PrimRecordWrite")]
    public ValueTask Runtime_PrimRecord_WriteAsync()
    {
        this.writeTarget.Position = 0;
        return this.primRecord.Layout.WriteAsync(this.writeTarget, "root", this.primRecordValue);
    }

    /// <summary>Runtime <c>Update</c> of <c>root.c</c> in a memory stream.</summary>
    [Benchmark]
    [BenchmarkCategory("PrimRecordUpdate")]
    public void Runtime_PrimRecord_UpdateStream()
    {
        this.updateTarget.Position = 0;
        this.primRecord.Layout.Update(this.updateTarget, "root.c", 7u);
    }

    /// <summary>Runtime <c>UpdateAsync</c> of <c>root.c</c> in the same memory stream.</summary>
    /// <returns>The update.</returns>
    [Benchmark]
    [BenchmarkCategory("PrimRecordUpdate")]
    public ValueTask Runtime_PrimRecord_UpdateAsync()
    {
        this.updateTarget.Position = 0;
        return this.primRecord.Layout.UpdateAsync(this.updateTarget, "root.c", 7u);
    }

    /// <summary>Generated <c>Parse(Stream)</c> of the record from the hidden-buffer stream.</summary>
    /// <returns>The generated record.</returns>
    [Benchmark]
    [BenchmarkCategory("GeneratedPrimRecord")]
    public PrimRecordLayout.Root Generated_PrimRecord_ParseStream()
    {
        this.primRecordHidden.Position = 0;
        return PrimRecordLayout.Parse(this.primRecordHidden);
    }

    /// <summary>Generated <c>ParseAsync</c> of the record from the hidden-buffer stream.</summary>
    /// <returns>The parse.</returns>
    [Benchmark]
    [BenchmarkCategory("GeneratedPrimRecord")]
    public ValueTask<PrimRecordLayout.Root> Generated_PrimRecord_ParseAsync()
    {
        this.primRecordHidden.Position = 0;
        return PrimRecordLayout.ParseAsync(this.primRecordHidden);
    }

    /// <summary>Generated <c>ParseAsync</c> of the record from the asynchronous file.</summary>
    /// <returns>The parse.</returns>
    [Benchmark]
    [BenchmarkCategory("GeneratedPrimRecord")]
    public ValueTask<PrimRecordLayout.Root> Generated_PrimRecord_ParseAsync_File()
    {
        this.primRecordFile.Position = 0;
        return PrimRecordLayout.ParseAsync(this.primRecordFile);
    }

    /// <summary>Generated <c>Write</c> of the record to a memory stream.</summary>
    [Benchmark]
    [BenchmarkCategory("GeneratedPrimRecordWrite")]
    public void Generated_PrimRecord_WriteStream()
    {
        this.writeTarget.Position = 0;
        PrimRecordLayout.Write(this.writeTarget, this.generatedRecord);
    }

    /// <summary>Generated <c>WriteAsync</c> of the record to the same memory stream.</summary>
    /// <returns>The write.</returns>
    [Benchmark]
    [BenchmarkCategory("GeneratedPrimRecordWrite")]
    public ValueTask Generated_PrimRecord_WriteAsync()
    {
        this.writeTarget.Position = 0;
        return PrimRecordLayout.WriteAsync(this.writeTarget, this.generatedRecord);
    }

    // ---- nested-x256: 6,400 bytes --------------------------------------------------------------------------------------

    /// <summary>Runtime <c>Parse(Stream)</c> of the 6,400-byte nested fixture: the synchronous reference.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark(Baseline = true)]
    [BenchmarkCategory("Nested256")]
    public StructValue Runtime_Nested256_ParseStream()
    {
        this.nestedExposed.Position = 0;
        return this.nested.Layout.Parse(this.nestedExposed, "root");
    }

    /// <summary>Runtime <c>ParseAsync</c> of the nested fixture from a stream that exposes its buffer.</summary>
    /// <returns>The parse, already complete.</returns>
    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public ValueTask<StructValue> Runtime_Nested256_ParseAsync_MemoryStream()
    {
        this.nestedExposed.Position = 0;
        return this.nested.Layout.ParseAsync(this.nestedExposed, "root");
    }

    /// <summary>Runtime <c>ParseAsync</c> of the nested fixture from a stream that hides its buffer.</summary>
    /// <returns>The parse.</returns>
    [Benchmark]
    [BenchmarkCategory("Nested256")]
    public ValueTask<StructValue> Runtime_Nested256_ParseAsync_HiddenBuffer()
    {
        this.nestedHidden.Position = 0;
        return this.nested.Layout.ParseAsync(this.nestedHidden, "root");
    }
}
