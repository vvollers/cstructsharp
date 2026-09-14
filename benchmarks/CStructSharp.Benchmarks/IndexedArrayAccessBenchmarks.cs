namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Attributes;

/// <summary>
///     Indexed address resolution into a 10,000-element array of a fixed-size struct. Lives in its own class so its
///     <see cref="FixedSizeStructArrayIndex"/> parameter does not attach to the <see cref="ReadBenchmarks"/>
///     release-gate cases, whose contract keys (contracts/performance/non-web-rc1.json) have no parameters.
/// </summary>
[BenchmarkCategory("IndexedArrayAccess")]
public class IndexedArrayAccessBenchmarks
{
    private CStruct fixedSizeStructArrayLayout = null!;
    private MemoryStream fixedSizeStructArrayStream = null!;
    private string fixedSizeStructArrayElementPath = null!;

    /// <summary>
    ///     The selected index into a 10,000-element array of a fixed-size two-field struct, used by
    ///     <see cref="ResolveIndexedFixedSizeStructArrayElement"/> to show that indexed address resolution costs the
    ///     same regardless of how far into the array the selected element is.
    /// </summary>
    [Params(0, 1, 10, 100, 1000, 9999)]
    public int FixedSizeStructArrayIndex { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        this.fixedSizeStructArrayLayout = new CStruct(
            "struct record { uint32 id; uint16 tag; }; struct root { record items[10000]; };");
        this.fixedSizeStructArrayStream = new MemoryStream(new byte[10000 * 6], writable: false);
        this.fixedSizeStructArrayElementPath = $"root.items[{this.FixedSizeStructArrayIndex}].id";
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        this.fixedSizeStructArrayStream.Dispose();
    }

    /// <summary>
    ///     Resolves the address of one field inside one selected element of a 10,000-element array of a fixed-size
    ///     struct. Address resolution for a statically fixed-size element is a direct multiplication rather than a
    ///     walk over every preceding element, so this benchmark's reported time should stay flat across
    ///     <see cref="FixedSizeStructArrayIndex"/> instead of growing with the selected index.
    /// </summary>
    [Benchmark]
    public long ResolveIndexedFixedSizeStructArrayElement()
    {
        this.fixedSizeStructArrayStream.Position = 0;
        return this.fixedSizeStructArrayLayout.ResolveAddress(
            this.fixedSizeStructArrayStream,
            this.fixedSizeStructArrayElementPath);
    }
}
