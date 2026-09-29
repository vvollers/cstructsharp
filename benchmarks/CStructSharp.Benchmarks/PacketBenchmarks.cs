namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Attributes;
using CStructSharp.Comparison.Variable;
using CStructSharp.Values;

/// <summary>
///     The runtime's general reader and writer on the data-dependent <c>packet</c> record of the serializer comparison
///     (<see cref="PacketLayout"/>): a count-sized <c>int32</c> array, a length-prefixed name, a member chosen by
///     <c>kind</c>, and a NUL-terminated note. No member after <c>samples</c> has a fixed offset, so no static plan or
///     direct reader applies and every case runs the general path from input to result.
/// </summary>
/// <remarks>
///     The layout, the mapped class and the 62-byte sample come from <c>benchmarks/CStructSharp.Comparison</c> (the
///     project links those source files), so these cases and the README's data-dependent table measure the same
///     record. Unlike the comparison, the read cases return the parsed value instead of a fingerprint of its members:
///     they measure the parse alone.
/// </remarks>
[BenchmarkCategory("Impact", "Packet")]
public class PacketBenchmarks
{
    private CStruct layout = null!;
    private byte[] bytes = [];
    private MemoryStream memoryStream = null!;
    private string filePath = string.Empty;
    private FileStream fileStream = null!;
    private StructValue structValue = null!;
    private PacketDto dto = null!;

    /// <summary>
    ///     Encodes the sample packet, opens a <see cref="MemoryStream"/> over it, writes it to a temporary file that
    ///     stays open for reading, and parses it once for the serialize cases.
    /// </summary>
    [GlobalSetup]
    public void Setup()
    {
        this.dto = PacketSample.CreateDto();
        byte[] encoded = new byte[256];
        this.bytes = encoded[..PacketSample.Write(this.dto, encoded)];
        this.layout = PacketLayout.Layout;
        this.memoryStream = new MemoryStream(this.bytes, writable: false);

        this.filePath = Path.Combine(Path.GetTempPath(), $"cstructsharp-packet-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(this.filePath, this.bytes);
        this.fileStream = new FileStream(this.filePath, FileMode.Open, FileAccess.Read, FileShare.Read);

        this.structValue = this.layout.Parse(this.bytes, "packet");
    }

    /// <summary>Closes the file stream and deletes the temporary file.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.fileStream.Dispose();
        this.memoryStream.Dispose();
        File.Delete(this.filePath);
    }

    /// <summary>Parses the packet from its in-memory bytes.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    public StructValue ParseSpan() => this.layout.Parse(this.bytes, "packet");

    /// <summary>Parses the packet from a <see cref="MemoryStream"/> rewound before each call.</summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    public StructValue ParseMemoryStream()
    {
        this.memoryStream.Position = 0;
        return this.layout.Parse(this.memoryStream, "packet");
    }

    /// <summary>
    ///     Parses the packet from a <see cref="FileStream"/> rewound before each call. The file is 62 bytes, so after
    ///     the first call the stream's buffer and the operating system's cache hold it: the case measures the reader
    ///     over a buffered file stream, not disk access.
    /// </summary>
    /// <returns>The parsed root.</returns>
    [Benchmark]
    public StructValue ParseFileStream()
    {
        this.fileStream.Position = 0;
        return this.layout.Parse(this.fileStream, "packet");
    }

    /// <summary>Reads the packet into the <c>[CStructMapped]</c> class, which is filled by name after a general read.</summary>
    /// <returns>The mapped instance.</returns>
    [Benchmark]
    public PacketDto ReadValueMapped() => this.layout.ReadValue<PacketDto>(this.bytes, "packet");

    /// <summary>Serializes the parsed <see cref="StructValue"/> into a new, exactly sized byte array.</summary>
    /// <returns>The encoded packet.</returns>
    [Benchmark]
    public byte[] SerializeStructValue() => this.layout.Serialize("packet", this.structValue);

    /// <summary>Serializes the <c>[CStructMapped]</c> instance into a new, exactly sized byte array.</summary>
    /// <returns>The encoded packet.</returns>
    [Benchmark]
    public byte[] SerializeMapped() => this.layout.Serialize("packet", this.dto);
}
