namespace CStructSharp.PackageConsumer;

/// <summary>A mapper the packaged generator writes: the members match the layout by name.</summary>
[CStructMapped]
public sealed partial class GeneratedRoot
{
    /// <summary>Gets or sets the marker.</summary>
    public byte Marker { get; set; }

    /// <summary>Gets or sets the value.</summary>
    public ushort Value { get; set; }

    /// <summary>Gets or sets the pointer.</summary>
    public CStructSharp.Values.Pointer Target { get; set; } = null!;
}
