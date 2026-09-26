namespace CStructSharp.Comparison;

using CStructSharp.Comparison.FlatBuffers;
using CStructSharp.Comparison.Model;
using CStructSharp.Values;
using Kaitai;

/// <summary>
///     Reads every member of each library's decoded record, in declaration order, into a <see cref="Fingerprint" />.
///     One overload per result type; all of them must return the same value for the same record.
/// </summary>
public static class Fingerprints
{
    // Precomputed element paths, so the StructValue consumer does not format a string per sample.
    private static readonly string[] SamplePaths = [.. Enumerable.Range(0, 8).Select(index => $"samples[{index}]")];

    /// <summary>Fingerprints the shared C# class.</summary>
    /// <param name="reading">The decoded record.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(ReadingDto reading)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(reading.Id);
        fingerprint.AddInteger(reading.Timestamp);
        AddVector(ref fingerprint, reading.Position.X, reading.Position.Y, reading.Position.Z);
        AddVector(ref fingerprint, reading.Velocity.X, reading.Velocity.Y, reading.Velocity.Z);
        fingerprint.AddInteger(reading.Flags);
        fingerprint.AddInteger(reading.Kind);
        fingerprint.AddDouble(reading.Value);
        foreach (int sample in reading.Samples)
        {
            fingerprint.AddInteger(sample);
        }

        return fingerprint.Value;
    }

    /// <summary>Fingerprints the class the CStructSharp source generator wrote.</summary>
    /// <param name="reading">The decoded record.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(ReadingLayout.Reading reading)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(reading.Id);
        fingerprint.AddInteger(reading.Timestamp);
        AddVector(ref fingerprint, reading.Position.X, reading.Position.Y, reading.Position.Z);
        AddVector(ref fingerprint, reading.Velocity.X, reading.Velocity.Y, reading.Velocity.Z);
        fingerprint.AddInteger(reading.Flags);
        fingerprint.AddInteger(reading.Kind);
        fingerprint.AddDouble(reading.Value);
        foreach (int sample in reading.Samples)
        {
            fingerprint.AddInteger(sample);
        }

        return fingerprint.Value;
    }

    /// <summary>Fingerprints a generated view; each member is decoded from the bytes as it is read.</summary>
    /// <param name="reading">The view over the record's bytes.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(ReadingLayout.ReadingView reading)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(reading.Id);
        fingerprint.AddInteger(reading.Timestamp);
        ReadingLayout.Vec3View position = reading.Position;
        AddVector(ref fingerprint, position.X, position.Y, position.Z);
        ReadingLayout.Vec3View velocity = reading.Velocity;
        AddVector(ref fingerprint, velocity.X, velocity.Y, velocity.Z);
        fingerprint.AddInteger(reading.Flags);
        fingerprint.AddInteger(reading.Kind);
        fingerprint.AddDouble(reading.Value);
        for (int index = 0; index < 8; index++)
        {
            fingerprint.AddInteger(reading.Samples(index));
        }

        return fingerprint.Value;
    }

    /// <summary>Fingerprints the runtime's untyped result, reading each member by path.</summary>
    /// <param name="reading">The parsed record.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(StructValue reading)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(reading.Get<uint>("id"));
        fingerprint.AddInteger(reading.Get<long>("timestamp"));
        AddVector(ref fingerprint, reading.Get<float>("position.x"), reading.Get<float>("position.y"), reading.Get<float>("position.z"));
        AddVector(ref fingerprint, reading.Get<float>("velocity.x"), reading.Get<float>("velocity.y"), reading.Get<float>("velocity.z"));
        fingerprint.AddInteger(reading.Get<ushort>("flags"));
        fingerprint.AddInteger(reading.Get<byte>("kind"));
        fingerprint.AddDouble(reading.Get<double>("value"));
        foreach (string path in SamplePaths)
        {
            fingerprint.AddInteger(reading.Get<int>(path));
        }

        return fingerprint.Value;
    }

    /// <summary>Fingerprints the runtime's untyped result through prepared accessors instead of path strings.</summary>
    /// <param name="reading">The parsed record.</param>
    /// <param name="accessors">The accessors resolved for the record's layout.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(StructValue reading, ReadingAccessors accessors)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(accessors.Id.Get(reading));
        fingerprint.AddInteger(accessors.Timestamp.Get(reading));
        AddVector(ref fingerprint, accessors.Position[0].Get(reading), accessors.Position[1].Get(reading), accessors.Position[2].Get(reading));
        AddVector(ref fingerprint, accessors.Velocity[0].Get(reading), accessors.Velocity[1].Get(reading), accessors.Velocity[2].Get(reading));
        fingerprint.AddInteger(accessors.Flags.Get(reading));
        fingerprint.AddInteger(accessors.Kind.Get(reading));
        fingerprint.AddDouble(accessors.Value.Get(reading));
        foreach (FieldAccessor<int> sample in accessors.Samples)
        {
            fingerprint.AddInteger(sample.Get(reading));
        }

        return fingerprint.Value;
    }

    /// <summary>Fingerprints a runtime view: each member is decoded from the bytes as it is read.</summary>
    /// <param name="reading">The view over the record's bytes.</param>
    /// <param name="accessors">The accessors resolved for the view's layout.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(StructView reading, ReadingAccessors accessors)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(reading.Get(accessors.Id));
        fingerprint.AddInteger(reading.Get(accessors.Timestamp));
        AddVector(ref fingerprint, reading.Get(accessors.Position[0]), reading.Get(accessors.Position[1]), reading.Get(accessors.Position[2]));
        AddVector(ref fingerprint, reading.Get(accessors.Velocity[0]), reading.Get(accessors.Velocity[1]), reading.Get(accessors.Velocity[2]));
        fingerprint.AddInteger(reading.Get(accessors.Flags));
        fingerprint.AddInteger(reading.Get(accessors.Kind));
        fingerprint.AddDouble(reading.Get(accessors.Value));
        foreach (FieldAccessor<int> sample in accessors.Samples)
        {
            fingerprint.AddInteger(reading.Get(sample));
        }

        return fingerprint.Value;
    }

    /// <summary>Fingerprints the unmanaged struct that <c>MemoryMarshal</c> copies.</summary>
    /// <param name="reading">The copied record.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(BlittableReading reading)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(reading.Id);
        fingerprint.AddInteger(reading.Timestamp);
        AddVector(ref fingerprint, reading.Position.X, reading.Position.Y, reading.Position.Z);
        AddVector(ref fingerprint, reading.Velocity.X, reading.Velocity.Y, reading.Velocity.Z);
        fingerprint.AddInteger(reading.Flags);
        fingerprint.AddInteger(reading.Kind);
        fingerprint.AddDouble(reading.Value);
        SampleBuffer samples = reading.Samples;
        foreach (int sample in samples)
        {
            fingerprint.AddInteger(sample);
        }

        return fingerprint.Value;
    }

    /// <summary>Fingerprints the struct the interop marshaller fills.</summary>
    /// <param name="reading">The marshalled record.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(InteropReading reading)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(reading.Id);
        fingerprint.AddInteger(reading.Timestamp);
        AddVector(ref fingerprint, reading.Position.X, reading.Position.Y, reading.Position.Z);
        AddVector(ref fingerprint, reading.Velocity.X, reading.Velocity.Y, reading.Velocity.Z);
        fingerprint.AddInteger(reading.Flags);
        fingerprint.AddInteger(reading.Kind);
        fingerprint.AddDouble(reading.Value);
        foreach (int sample in reading.Samples)
        {
            fingerprint.AddInteger(sample);
        }

        return fingerprint.Value;
    }

    /// <summary>Fingerprints the object that the Kaitai Struct generated reader builds.</summary>
    /// <param name="reading">The decoded record.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(SensorReading reading)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(reading.Id);
        fingerprint.AddInteger(reading.Timestamp);
        AddVector(ref fingerprint, reading.Position.X, reading.Position.Y, reading.Position.Z);
        AddVector(ref fingerprint, reading.Velocity.X, reading.Velocity.Y, reading.Velocity.Z);
        fingerprint.AddInteger(reading.Flags);
        fingerprint.AddInteger(reading.Kind);
        fingerprint.AddDouble(reading.Value);
        foreach (int sample in reading.Samples)
        {
            fingerprint.AddInteger(sample);
        }

        return fingerprint.Value;
    }

    /// <summary>Fingerprints a FlatSharp table; in lazy mode each member is decoded from the buffer as it is read.</summary>
    /// <param name="reading">The table over the buffer.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(FlatReading reading)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(reading.Id);
        fingerprint.AddInteger(reading.Timestamp);
        FlatVec3 position = reading.Position!;
        AddVector(ref fingerprint, position.X, position.Y, position.Z);
        FlatVec3 velocity = reading.Velocity!;
        AddVector(ref fingerprint, velocity.X, velocity.Y, velocity.Z);
        fingerprint.AddInteger(reading.Flags);
        fingerprint.AddInteger(reading.Kind);
        fingerprint.AddDouble(reading.Value);
        IList<int> samples = reading.Samples!;
        for (int index = 0; index < samples.Count; index++)
        {
            fingerprint.AddInteger(samples[index]);
        }

        return fingerprint.Value;
    }

    /// <summary>Adds the three components of a vector in x, y, z order.</summary>
    /// <param name="fingerprint">The fingerprint being built.</param>
    /// <param name="x">The x component.</param>
    /// <param name="y">The y component.</param>
    /// <param name="z">The z component.</param>
    private static void AddVector(ref Fingerprint fingerprint, float x, float y, float z)
    {
        fingerprint.AddSingle(x);
        fingerprint.AddSingle(y);
        fingerprint.AddSingle(z);
    }
}
