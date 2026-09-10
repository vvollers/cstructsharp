namespace CStructSharp.PackageConsumer;

/// <summary>Provides a typed value model for package-consumer round-trip checks.</summary>
public sealed class PackageRoot
{
    /// <summary>Gets or sets the Marker field used by the consumer fixture.</summary>
    public byte Marker { get; set; }

    /// <summary>Gets or sets the Value field used by the consumer fixture.</summary>
    public ushort Value { get; set; }

    /// <summary>Gets or sets the Target field used by the consumer fixture.</summary>
    public CStructSharp.Pointer Target { get; set; } = null!;
}
