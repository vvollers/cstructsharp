namespace CStructSharp.Comparison.Model;

using System.Runtime.InteropServices;

/// <summary>
///     The comparison record as an unmanaged struct whose memory image is the 79-byte C layout: sequential members,
///     <c>Pack = 1</c> (no padding), and inline samples. <c>MemoryMarshal.Read</c>/<c>Write</c> copy it as raw
///     memory, which matches the layout only on a little-endian machine.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BlittableReading
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

    /// <summary>Gets or sets the eight raw samples.</summary>
    public SampleBuffer Samples { get; set; }
}
