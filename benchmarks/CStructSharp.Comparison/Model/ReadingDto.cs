namespace CStructSharp.Comparison.Model;

using MemoryPack;
using MessagePack;
using ProtoBuf;

/// <summary>
///     The comparison record as an ordinary C# class, shared by every serializer that maps a class: MemoryPack,
///     MessagePack, protobuf-net, System.Text.Json, the hand-written and <c>BinaryReader</c> readers, and
///     CStructSharp's runtime <c>ReadValue&lt;T&gt;</c>/<c>Serialize</c> through <see cref="CStructMappedAttribute" />.
///     Each attribute only tells its own library how to find the members; none of them changes the others' formats.
/// </summary>
[CStructMapped(Layout = "reading")]
[MemoryPackable]
[MessagePackObject]
[ProtoContract]
public sealed partial class ReadingDto
{
    /// <summary>Gets or sets the sensor id (<c>uint32 id</c>).</summary>
    [Key(0)]
    [ProtoMember(1)]
    public uint Id { get; set; }

    /// <summary>Gets or sets the capture time in milliseconds since the Unix epoch (<c>int64 timestamp</c>).</summary>
    [Key(1)]
    [ProtoMember(2)]
    public long Timestamp { get; set; }

    /// <summary>Gets or sets the sensor position (<c>vec3 position</c>).</summary>
    [Key(2)]
    [ProtoMember(3, IsRequired = true)]
    public Vec3Dto Position { get; set; } = new();

    /// <summary>Gets or sets the sensor velocity (<c>vec3 velocity</c>).</summary>
    [Key(3)]
    [ProtoMember(4, IsRequired = true)]
    public Vec3Dto Velocity { get; set; } = new();

    /// <summary>Gets or sets the status bits (<c>uint16 flags</c>).</summary>
    [Key(4)]
    [ProtoMember(5)]
    public ushort Flags { get; set; }

    /// <summary>Gets or sets the reading kind (<c>uint8 kind</c>).</summary>
    [Key(5)]
    [ProtoMember(6)]
    public byte Kind { get; set; }

    /// <summary>Gets or sets the measured value (<c>float64 value</c>).</summary>
    [Key(6)]
    [ProtoMember(7)]
    public double Value { get; set; }

    /// <summary>Gets or sets the eight raw samples (<c>int32 samples[8]</c>).</summary>
    [Key(7)]
    [ProtoMember(8, IsPacked = true)]
    public int[] Samples { get; set; } = [];
}
