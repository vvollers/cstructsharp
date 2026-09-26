namespace CStructSharp.Comparison.Model;

using MemoryPack;
using MessagePack;
using ProtoBuf;

/// <summary>A three-component vector of the comparison record as an ordinary C# class (see <see cref="ReadingDto" />).</summary>
[CStructMapped(Layout = "vec3")]
[MemoryPackable]
[MessagePackObject]
[ProtoContract]
public sealed partial class Vec3Dto
{
    /// <summary>Gets or sets the x component.</summary>
    [Key(0)]
    [ProtoMember(1)]
    public float X { get; set; }

    /// <summary>Gets or sets the y component.</summary>
    [Key(1)]
    [ProtoMember(2)]
    public float Y { get; set; }

    /// <summary>Gets or sets the z component.</summary>
    [Key(2)]
    [ProtoMember(3)]
    public float Z { get; set; }
}
