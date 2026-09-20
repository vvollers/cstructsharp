namespace CStructSharp.PackageConsumer;

using System.Runtime.CompilerServices;
using CStructSharp.Values;

/// <summary>Provides a typed value model for package-consumer round-trip checks, mapped by its own members.</summary>
public sealed class PackageRoot : ICStructMapped<PackageRoot>
{
    /// <summary>Gets or sets the Marker field used by the consumer fixture.</summary>
    public byte Marker { get; set; }

    /// <summary>Gets or sets the Value field used by the consumer fixture.</summary>
    public ushort Value { get; set; }

    /// <summary>Gets or sets the Target field used by the consumer fixture.</summary>
    public CStructSharp.Values.Pointer Target { get; set; } = null!;

    /// <inheritdoc/>
    public static PackageRoot ReadFrom(StructValue source)
    {
        return new PackageRoot
        {
            Marker = source.Get<byte>("marker"),
            Value = source.Get<ushort>("value"),
            Target = source.Get<CStructSharp.Values.Pointer>("target"),
        };
    }

    /// <inheritdoc/>
    public static void WriteTo(PackageRoot value, StructValue target)
    {
        target["marker"] = value.Marker;
        target["value"] = value.Value;
        target["target"] = value.Target;
    }

    /// <summary>Registers the type before any consumer code runs.</summary>
    [ModuleInitializer]
    internal static void Register()
    {
        MappedTypes.Register<PackageRoot>();
    }
}
