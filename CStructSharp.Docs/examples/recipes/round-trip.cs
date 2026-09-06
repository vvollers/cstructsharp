// Generated from executable documentation examples. Edit the source region, then regenerate.
using System;
using System.IO;
using System.Linq;
using System.Buffers;
using System.Collections.Generic;
using System.Dynamic;
using System.Numerics;
using CStructSharp;

internal static class Program
{
    public static void Main()
    {
        RoundTrip();
        Console.WriteLine("PASS round-trip");
    }

    private static void RoundTrip()
    {
        var layout = new CStruct("struct sample { uint16 id; uint8 flags; };");
        byte[] input = [0x34, 0x12, 0xA5];
        object parsed = layout.Parse(input, "sample");
        SequenceEqual(input, layout.Serialize("sample", parsed));

        Span<byte> destination = stackalloc byte[8];
        destination.Fill(0xCC);
        int written = layout.Serialize(destination, "sample", parsed);
        Equal(3, written);
        SequenceEqual(input, destination[..written].ToArray());
        Equal((byte)0xCC, destination[written]);

        var writer = new ArrayBufferWriter<byte>();
        Equal(3L, layout.Serialize(writer, "sample", parsed));
        SequenceEqual(input, writer.WrittenSpan.ToArray());
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }

    private static void SequenceEqual(byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new InvalidOperationException(
                $"Expected {Convert.ToHexString(expected)}, received {Convert.ToHexString(actual)}.");
        }
    }
}
