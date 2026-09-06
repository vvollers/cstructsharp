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
        AlignedHeader();
        Console.WriteLine("PASS aligned-header");
    }

    private static void AlignedHeader()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };", aligned: true);
        byte[] bytes = [2, 0, 0, 0, 6, 0, 0, 0];
        Equal(6U, (uint)layout.ReadValue(bytes.AsSpan(), "header.length")!);
        using var stream = new MemoryStream(bytes);
        Equal(4L, layout.ResolveAddress(stream, "header.length"));
        SequenceEqual(bytes, layout.Serialize("header", layout.Parse(bytes.AsSpan(), "header")));
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
