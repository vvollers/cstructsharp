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
        DecodeHeader();
        Console.WriteLine("PASS decode-header");
    }

    private static void DecodeHeader()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        ReadOnlySpan<byte> bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00];
        dynamic header = layout.Parse(bytes, "header");
        Equal((ushort)2, (ushort)header.kind);
        Equal(6U, (uint)header.length);

        bool read = layout.TryReadValue<Header>(bytes, out Header? typed, "header");
        True(read && typed is { Kind: 2, Length: 6 }, "Typed header result differed.");
        True(!layout.TryReadValue<Header>(bytes[..1], out _, "header"), "Truncated TryReadValue should fail.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public sealed class Header
    {
        public ushort Kind { get; set; }

        public uint Length { get; set; }
    }
}
