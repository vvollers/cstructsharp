namespace CStructSharp.Comparison.Variable;

/// <summary>
///     The variable-length packet as an ordinary class, mapped to <see cref="PacketLayout"/>. Its layout is not fixed,
///     so the class has no direct members: it reads and writes through <c>ReadFrom</c>/<c>WriteTo</c>.
/// </summary>
[CStructMapped(Layout = "packet")]
public sealed partial class PacketDto
{
    /// <summary>Gets or sets the packet id (<c>uint32 id</c>).</summary>
    public uint Id { get; set; }

    /// <summary>Gets or sets the number of samples (<c>uint16 count</c>).</summary>
    public ushort Count { get; set; }

    /// <summary>Gets or sets the samples (<c>int32 samples[count]</c>).</summary>
    public int[] Samples { get; set; } = [];

    /// <summary>Gets or sets the name's length in bytes (<c>uint8 name_length</c>).</summary>
    public byte NameLength { get; set; }

    /// <summary>Gets or sets the sensor name (<c>char name[name_length]</c>).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Gets or sets the kind, which selects <see cref="Value"/> (1) or <see cref="Code"/> (anything else).</summary>
    public byte Kind { get; set; }

    /// <summary>Gets or sets the measured value, present when <see cref="Kind"/> is 1.</summary>
    public double? Value { get; set; }

    /// <summary>Gets or sets the status code, present when <see cref="Kind"/> is not 1.</summary>
    public uint? Code { get; set; }

    /// <summary>Gets or sets the NUL-terminated note (<c>cstring note</c>).</summary>
    public string Note { get; set; } = string.Empty;
}
