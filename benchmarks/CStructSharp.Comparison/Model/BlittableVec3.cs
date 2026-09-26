namespace CStructSharp.Comparison.Model;

using System.Runtime.InteropServices;

/// <summary>A three-component vector with the C layout's 12 bytes, for <see cref="BlittableReading" />.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BlittableVec3
{
    /// <summary>Gets or sets the x component.</summary>
    public float X { get; set; }

    /// <summary>Gets or sets the y component.</summary>
    public float Y { get; set; }

    /// <summary>Gets or sets the z component.</summary>
    public float Z { get; set; }
}
