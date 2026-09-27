namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Attributes;

/// <summary>
///     <c>ResolveAddress</c> of <c>root.groups[Index].value</c> in a 256-element array of two-byte structs, with a
///     fixed element count and with a count supplied as a layout variable.
/// </summary>
[BenchmarkCategory("Address")]
public class AddressBenchmarks
{
    private CStruct fixedLayout = null!;
    private CStruct runtimeLayout = null!;
    private MemoryStream stream = null!;
    private IReadOnlyDictionary<string, int> runtimeVariables = null!;
    private string path = null!;

    /// <summary>Gets or sets the element index: the first, a middle and the last element.</summary>
    [Params(0, 127, 255)]
    public int Index { get; set; }

    /// <summary>Compiles both layouts, opens a 512-byte stream, and builds the path for <see cref="Index"/>.</summary>
    [GlobalSetup]
    public void Setup()
    {
        const string item = "struct item { uint16 value; }; ";
        this.fixedLayout = new CStruct(item + "struct root { item groups[256]; };");
        this.runtimeLayout = new CStruct(item + "struct root { item groups[COUNT]; };");
        this.stream = new MemoryStream(new byte[512], writable: false);
        this.runtimeVariables = new Dictionary<string, int> { ["COUNT"] = 256, };
        this.path = $"root.groups[{this.Index}].value";
    }

    /// <summary>Disposes the stream.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.stream.Dispose();
    }

    /// <summary>Resolves the element's byte offset in the fixed-count array.</summary>
    /// <returns>The byte offset from the start of the stream.</returns>
    [Benchmark]
    [BenchmarkCategory("Gate")]
    public long ResolveFixedNestedArray()
    {
        return this.fixedLayout.ResolveAddress(this.stream, this.path);
    }

    /// <summary>Resolves the same offset when the array count comes from the <c>COUNT</c> variable.</summary>
    /// <returns>The byte offset from the start of the stream.</returns>
    [Benchmark]
    public long ResolveRuntimeNestedArray()
    {
        return this.runtimeLayout.ResolveAddress(this.stream, this.path, this.runtimeVariables);
    }
}
