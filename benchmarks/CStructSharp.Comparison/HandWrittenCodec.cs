namespace CStructSharp.Comparison;

using System.Buffers.Binary;
using CStructSharp.Comparison.Model;

/// <summary>
///     The 79-byte C layout read and written by hand with <see cref="BinaryPrimitives" />: the code a programmer would
///     write without a library, with every offset typed in. It is the speed ceiling for the "same bytes" table and
///     the reference output that the other writers must reproduce byte for byte.
/// </summary>
public static class HandWrittenCodec
{
    /// <summary>Decodes a record from the start of <paramref name="source" />.</summary>
    /// <param name="source">At least <see cref="SampleRecord.LayoutSize" /> bytes in the C layout.</param>
    /// <returns>A new record.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="source" /> is shorter than the layout.</exception>
    public static ReadingDto Read(ReadOnlySpan<byte> source)
    {
        // One bounds check up front; the fixed offsets below then stay inside the record.
        source = source[..SampleRecord.LayoutSize];
        var samples = new int[8];
        for (int index = 0; index < samples.Length; index++)
        {
            samples[index] = BinaryPrimitives.ReadInt32LittleEndian(source[(47 + (index * 4))..]);
        }

        return new ReadingDto
        {
            Id = BinaryPrimitives.ReadUInt32LittleEndian(source),
            Timestamp = BinaryPrimitives.ReadInt64LittleEndian(source[4..]),
            Position = ReadVector(source[12..]),
            Velocity = ReadVector(source[24..]),
            Flags = BinaryPrimitives.ReadUInt16LittleEndian(source[36..]),
            Kind = source[38],
            Value = BinaryPrimitives.ReadDoubleLittleEndian(source[39..]),
            Samples = samples,
        };
    }

    /// <summary>Encodes <paramref name="value" /> at the start of <paramref name="destination" />.</summary>
    /// <param name="value">The record; it must hold exactly eight samples.</param>
    /// <param name="destination">At least <see cref="SampleRecord.LayoutSize" /> bytes; the rest is left unchanged.</param>
    /// <returns>The number of bytes written, always <see cref="SampleRecord.LayoutSize" />.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="destination" /> is shorter than the layout.</exception>
    public static int Write(ReadingDto value, Span<byte> destination)
    {
        destination = destination[..SampleRecord.LayoutSize];
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value.Id);
        BinaryPrimitives.WriteInt64LittleEndian(destination[4..], value.Timestamp);
        WriteVector(value.Position, destination[12..]);
        WriteVector(value.Velocity, destination[24..]);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[36..], value.Flags);
        destination[38] = value.Kind;
        BinaryPrimitives.WriteDoubleLittleEndian(destination[39..], value.Value);
        for (int index = 0; index < 8; index++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination[(47 + (index * 4))..], value.Samples[index]);
        }

        return SampleRecord.LayoutSize;
    }

    /// <summary>Decodes a 12-byte vector.</summary>
    /// <param name="source">The vector's bytes and anything after them.</param>
    /// <returns>A new vector.</returns>
    private static Vec3Dto ReadVector(ReadOnlySpan<byte> source) => new()
    {
        X = BinaryPrimitives.ReadSingleLittleEndian(source),
        Y = BinaryPrimitives.ReadSingleLittleEndian(source[4..]),
        Z = BinaryPrimitives.ReadSingleLittleEndian(source[8..]),
    };

    /// <summary>Encodes a vector into its 12 bytes.</summary>
    /// <param name="value">The vector.</param>
    /// <param name="destination">The vector's bytes and anything after them.</param>
    private static void WriteVector(Vec3Dto value, Span<byte> destination)
    {
        BinaryPrimitives.WriteSingleLittleEndian(destination, value.X);
        BinaryPrimitives.WriteSingleLittleEndian(destination[4..], value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(destination[8..], value.Z);
    }
}
