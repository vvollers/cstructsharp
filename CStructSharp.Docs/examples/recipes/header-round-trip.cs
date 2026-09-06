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
        HeaderRoundTrip();
        Console.WriteLine("PASS header-round-trip");
    }

    private static void HeaderRoundTrip()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        byte[] bytes = layout.Serialize("header", new Dictionary<string, object?> { ["kind"] = 2, ["length"] = 6 });
        SequenceEqual([2, 0, 6, 0, 0, 0], bytes);
        using var stream = new MemoryStream(bytes);
        layout.UpdateStream(stream, "header.kind", 3);
        SequenceEqual([3, 0, 6, 0, 0, 0], stream.ToArray());
        Header header = layout.ReadValue<Header>(stream.ToArray().AsSpan(), "header");
        Equal((ushort)3, header.Kind);
        Equal(6U, header.Length);
        Console.WriteLine($"Updated kind = {header.Kind}; length = {header.Length}");
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

    public sealed class Header
    {
        public ushort Kind { get; set; }

        public uint Length { get; set; }
    }
}
