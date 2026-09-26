namespace CStructSharp.Comparison;

using System.Text.Json;
using CStructSharp.Comparison.FlatBuffers;
using CStructSharp.Comparison.Model;
using FlatSharp;
using MemoryPack;
using MessagePack;

/// <summary>
///     The sample record encoded once in every format, created before measuring so that each deserialize
///     benchmark reads bytes its own library wrote. <see cref="Layout" /> is the 79-byte C layout, written by
///     <see cref="HandWrittenCodec" />; the others are each library's own format and have different sizes.
/// </summary>
public sealed class Payloads
{
    /// <summary>Initializes a new instance of the <see cref="Payloads" /> class by encoding the sample record.</summary>
    public Payloads()
    {
        ReadingDto dto = SampleRecord.CreateDto();
        this.Layout = new byte[SampleRecord.LayoutSize];
        HandWrittenCodec.Write(dto, this.Layout);
        this.MemoryPack = MemoryPackSerializer.Serialize(dto);
        this.MessagePack = MessagePackSerializer.Serialize(dto);
        using (var protobuf = new MemoryStream())
        {
            ProtoBuf.Serializer.Serialize(protobuf, dto);
            this.Protobuf = protobuf.ToArray();
        }

        FlatReading flat = SampleRecord.ToFlat(dto);
        var flatBuffer = new byte[FlatReading.Serializer.GetMaxSize(flat)];
        int flatLength = FlatReading.Serializer.Write(flatBuffer, flat);
        this.FlatBuffers = flatBuffer[..flatLength];
        this.Json = JsonSerializer.SerializeToUtf8Bytes(dto, ReadingJsonContext.Default.ReadingDto);
    }

    /// <summary>Gets the record in the C layout (79 bytes, little-endian, no padding).</summary>
    public byte[] Layout { get; }

    /// <summary>Gets the record in MemoryPack's format.</summary>
    public byte[] MemoryPack { get; }

    /// <summary>Gets the record in MessagePack's format (an array of eight values, as the <c>Key</c> attributes ask).</summary>
    public byte[] MessagePack { get; }

    /// <summary>Gets the record as a Protocol Buffers message written by protobuf-net.</summary>
    public byte[] Protobuf { get; }

    /// <summary>Gets the record as a FlatBuffers table written by FlatSharp.</summary>
    public byte[] FlatBuffers { get; }

    /// <summary>Gets the record as UTF-8 JSON written by System.Text.Json.</summary>
    public byte[] Json { get; }
}
