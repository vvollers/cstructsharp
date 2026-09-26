namespace CStructSharp.Comparison.Variable;

using System.Buffers.Binary;
using CStructSharp.Values;

/// <summary>
///     The variable-length packet the <see cref="VariableBenchmarks"/> read and write: the sample values, a hand-written
///     reader and writer (the speed ceiling and the reference bytes), and the fingerprints every reader's result must
///     agree on.
/// </summary>
public static class PacketSample
{
    /// <summary>Creates the sample packet: six samples, an eight-character name, kind 1 with a value, and a note.</summary>
    /// <returns>A new instance.</returns>
    public static PacketDto CreateDto() => new()
    {
        Id = 77,
        Count = 6,
        Samples = [-120, 4, 88, 1024, -7, 65_000],
        NameLength = 8,
        Name = "thermo-7",
        Kind = 1,
        Value = 21.5,
        Note = "calibrated ok",
    };

    /// <summary>Copies the sample into the class the source generator wrote for <see cref="PacketLayout"/>.</summary>
    /// <param name="source">The packet.</param>
    /// <returns>A new generated instance.</returns>
    public static PacketLayout.Packet ToGenerated(PacketDto source) => new()
    {
        Id = source.Id,
        Count = source.Count,
        Samples = [.. source.Samples],
        NameLength = source.NameLength,
        Name = source.Name,
        Kind = source.Kind,
        HasValue = source.Value.HasValue,
        Value = source.Value ?? 0,
        HasCode = source.Code.HasValue,
        Code = source.Code ?? 0,
        Note = source.Note,
    };

    /// <summary>Reads a packet by hand: the member offsets follow from the counts read before them.</summary>
    /// <param name="source">The packet's bytes.</param>
    /// <returns>A new packet.</returns>
    public static PacketDto Read(ReadOnlySpan<byte> source)
    {
        var packet = new PacketDto { Id = BinaryPrimitives.ReadUInt32LittleEndian(source), Count = BinaryPrimitives.ReadUInt16LittleEndian(source[4..]) };
        int position = 6;
        packet.Samples = new int[packet.Count];
        for (int index = 0; index < packet.Samples.Length; index++, position += 4)
        {
            packet.Samples[index] = BinaryPrimitives.ReadInt32LittleEndian(source[position..]);
        }

        packet.NameLength = source[position++];
        packet.Name = System.Text.Encoding.Latin1.GetString(source.Slice(position, packet.NameLength));
        position += packet.NameLength;
        packet.Kind = source[position++];
        if (packet.Kind == 1)
        {
            packet.Value = BinaryPrimitives.ReadDoubleLittleEndian(source[position..]);
            position += 8;
        }
        else
        {
            packet.Code = BinaryPrimitives.ReadUInt32LittleEndian(source[position..]);
            position += 4;
        }

        int end = source[position..].IndexOf((byte)0);
        packet.Note = System.Text.Encoding.ASCII.GetString(source.Slice(position, end));
        return packet;
    }

    /// <summary>Writes a packet by hand into <paramref name="destination"/>.</summary>
    /// <param name="value">The packet; its counts must match its arrays and text.</param>
    /// <param name="destination">Enough bytes for the packet.</param>
    /// <returns>The number of bytes written.</returns>
    public static int Write(PacketDto value, Span<byte> destination)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(destination, value.Id);
        BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], value.Count);
        int position = 6;
        foreach (int sample in value.Samples)
        {
            BinaryPrimitives.WriteInt32LittleEndian(destination[position..], sample);
            position += 4;
        }

        destination[position++] = value.NameLength;
        position += System.Text.Encoding.Latin1.GetBytes(value.Name, destination[position..]);
        destination[position++] = value.Kind;
        if (value.Kind == 1)
        {
            BinaryPrimitives.WriteDoubleLittleEndian(destination[position..], value.Value!.Value);
            position += 8;
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(destination[position..], value.Code!.Value);
            position += 4;
        }

        position += System.Text.Encoding.ASCII.GetBytes(value.Note, destination[position..]);
        destination[position++] = 0;
        return position;
    }

    /// <summary>Fingerprints the shared class.</summary>
    /// <param name="packet">The decoded packet.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(PacketDto packet)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(packet.Id);
        fingerprint.AddInteger(packet.Count);
        foreach (int sample in packet.Samples)
        {
            fingerprint.AddInteger(sample);
        }

        fingerprint.AddInteger(packet.NameLength);
        AddText(ref fingerprint, packet.Name);
        fingerprint.AddInteger(packet.Kind);
        fingerprint.AddDouble(packet.Value ?? double.NaN);
        fingerprint.AddInteger(packet.Code ?? uint.MaxValue);
        AddText(ref fingerprint, packet.Note);
        return fingerprint.Value;
    }

    /// <summary>Fingerprints the class the source generator wrote.</summary>
    /// <param name="packet">The decoded packet.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(PacketLayout.Packet packet)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(packet.Id);
        fingerprint.AddInteger(packet.Count);
        foreach (int sample in packet.Samples)
        {
            fingerprint.AddInteger(sample);
        }

        fingerprint.AddInteger(packet.NameLength);
        AddText(ref fingerprint, packet.Name);
        fingerprint.AddInteger(packet.Kind);
        fingerprint.AddDouble(packet.HasValue ? packet.Value : double.NaN);
        fingerprint.AddInteger(packet.HasCode ? packet.Code : uint.MaxValue);
        AddText(ref fingerprint, packet.Note);
        return fingerprint.Value;
    }

    /// <summary>Fingerprints the runtime's untyped result through prepared accessors.</summary>
    /// <param name="packet">The parsed packet.</param>
    /// <param name="accessors">The accessors resolved for the packet's layout.</param>
    /// <returns>The fingerprint value.</returns>
    public static ulong Of(StructValue packet, PacketAccessors accessors)
    {
        var fingerprint = default(Fingerprint);
        fingerprint.AddInteger(accessors.Id.Get(packet));
        fingerprint.AddInteger(accessors.Count.Get(packet));
        foreach (int sample in accessors.Samples.Get(packet))
        {
            fingerprint.AddInteger(sample);
        }

        fingerprint.AddInteger(accessors.NameLength.Get(packet));
        AddText(ref fingerprint, accessors.Name.Get(packet));
        fingerprint.AddInteger(accessors.Kind.Get(packet));
        fingerprint.AddDouble(packet.TryGet("value", out double value) ? value : double.NaN);
        fingerprint.AddInteger(packet.TryGet("code", out uint code) ? code : uint.MaxValue);
        AddText(ref fingerprint, accessors.Note.Get(packet));
        return fingerprint.Value;
    }

    /// <summary>Adds each character of a text member.</summary>
    private static void AddText(ref Fingerprint fingerprint, string text)
    {
        foreach (char character in text)
        {
            fingerprint.AddInteger(character);
        }
    }
}
