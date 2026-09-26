namespace CStructSharp.Comparison;

using CStructSharp.Comparison.FlatBuffers;
using CStructSharp.Comparison.Model;

/// <summary>
///     The one record every benchmark serializes and deserializes, and its copies in each library's in-memory type.
///     The values are fixed so that runs are repeatable, and the floating-point values are exactly representable so
///     that text formats (JSON) round-trip them without rounding.
/// </summary>
public static class SampleRecord
{
    /// <summary>The size of the record in the C layout: 4 + 8 + 12 + 12 + 2 + 1 + 8 + 32 bytes, with no padding.</summary>
    public const int LayoutSize = 79;

    /// <summary>Creates the record as the shared C# class.</summary>
    /// <returns>A new instance; callers may keep or modify it.</returns>
    public static ReadingDto CreateDto() => new()
    {
        Id = 4_021_337,
        Timestamp = 1_758_844_800_123,
        Position = new Vec3Dto { X = 12.5f, Y = -3.25f, Z = 100.125f },
        Velocity = new Vec3Dto { X = 0.5f, Y = 0.75f, Z = -9.8125f },
        Flags = 0x0A05,
        Kind = 3,
        Value = 21.734375,
        Samples = [-120, 4, 88, 1024, -7, 65_000, 12, 0],
    };

    /// <summary>Copies the record into the class that the CStructSharp source generator wrote.</summary>
    /// <param name="source">The record.</param>
    /// <returns>A new generated instance.</returns>
    public static ReadingLayout.Reading ToGenerated(ReadingDto source) => new()
    {
        Id = source.Id,
        Timestamp = source.Timestamp,
        Position = new ReadingLayout.Vec3 { X = source.Position.X, Y = source.Position.Y, Z = source.Position.Z },
        Velocity = new ReadingLayout.Vec3 { X = source.Velocity.X, Y = source.Velocity.Y, Z = source.Velocity.Z },
        Flags = source.Flags,
        Kind = source.Kind,
        Value = source.Value,
        Samples = [.. source.Samples],
    };

    /// <summary>Copies the record into the unmanaged struct used with <c>MemoryMarshal</c>.</summary>
    /// <param name="source">The record.</param>
    /// <returns>The struct.</returns>
    public static BlittableReading ToBlittable(ReadingDto source)
    {
        var samples = default(SampleBuffer);
        source.Samples.CopyTo(samples);
        return new BlittableReading
        {
            Id = source.Id,
            Timestamp = source.Timestamp,
            Position = new BlittableVec3 { X = source.Position.X, Y = source.Position.Y, Z = source.Position.Z },
            Velocity = new BlittableVec3 { X = source.Velocity.X, Y = source.Velocity.Y, Z = source.Velocity.Z },
            Flags = source.Flags,
            Kind = source.Kind,
            Value = source.Value,
            Samples = samples,
        };
    }

    /// <summary>Copies the record into the struct used with the interop marshaller.</summary>
    /// <param name="source">The record.</param>
    /// <returns>The struct, with its own samples array.</returns>
    public static InteropReading ToInterop(ReadingDto source) => new()
    {
        Id = source.Id,
        Timestamp = source.Timestamp,
        Position = new BlittableVec3 { X = source.Position.X, Y = source.Position.Y, Z = source.Position.Z },
        Velocity = new BlittableVec3 { X = source.Velocity.X, Y = source.Velocity.Y, Z = source.Velocity.Z },
        Flags = source.Flags,
        Kind = source.Kind,
        Value = source.Value,
        Samples = [.. source.Samples],
    };

    /// <summary>Copies the record into the table class that FlatSharp generated from <c>reading.fbs</c>.</summary>
    /// <param name="source">The record.</param>
    /// <returns>A new table instance.</returns>
    public static FlatReading ToFlat(ReadingDto source) => new()
    {
        Id = source.Id,
        Timestamp = source.Timestamp,
        Position = new FlatVec3 { X = source.Position.X, Y = source.Position.Y, Z = source.Position.Z },
        Velocity = new FlatVec3 { X = source.Velocity.X, Y = source.Velocity.Y, Z = source.Velocity.Z },
        Flags = source.Flags,
        Kind = source.Kind,
        Value = source.Value,
        Samples = [.. source.Samples],
    };
}
