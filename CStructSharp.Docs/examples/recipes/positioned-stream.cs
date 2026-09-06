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
        PositionedStream();
        Console.WriteLine("PASS positioned-stream");
    }

    private static void PositionedStream()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        using var stream = new MemoryStream([0xEE, 0xEE, 2, 0, 6, 0, 0, 0]);
        stream.Position = 2;
        Equal(4L, layout.ResolveAddress(stream, "header.length"));
        Equal(2L, stream.Position);
        dynamic header = layout.ParseStream(stream, "header");
        Equal(6U, (uint)header.length);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }
}
