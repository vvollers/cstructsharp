namespace CStructSharp.Comparison.Model;

using System.Runtime.InteropServices;

/// <summary>
///     The comparison record as a struct for the interop marshaller (<c>Marshal.PtrToStructure</c> and
///     <c>StructureToPtr</c>): the same 79-byte packed image as <see cref="BlittableReading" />, but with the samples
///     as a managed array that the marshaller copies element by element (<c>ByValArray</c>).
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InteropReading
{
    /// <summary>Gets or sets the sensor id.</summary>
    public uint Id { get; set; }

    /// <summary>Gets or sets the capture time in milliseconds since the Unix epoch.</summary>
    public long Timestamp { get; set; }

    /// <summary>Gets or sets the sensor position.</summary>
    public BlittableVec3 Position { get; set; }

    /// <summary>Gets or sets the sensor velocity.</summary>
    public BlittableVec3 Velocity { get; set; }

    /// <summary>Gets or sets the status bits.</summary>
    public ushort Flags { get; set; }

    /// <summary>Gets or sets the reading kind.</summary>
    public byte Kind { get; set; }

    /// <summary>Gets or sets the measured value.</summary>
    public double Value { get; set; }

    /// <summary>Gets or sets the eight raw samples; the marshaller requires exactly <c>SizeConst</c> elements.</summary>
    [field: MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
    public int[] Samples { get; set; }
}
