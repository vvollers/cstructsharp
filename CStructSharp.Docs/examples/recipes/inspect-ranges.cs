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
        InspectRanges();
        Console.WriteLine("PASS inspect-ranges");
    }

    private static void InspectRanges()
    {
        var layout = new CStruct("struct sample { uint8 tag; uint16 value; };");
        using var stream = new MemoryStream([0xA1, 0x34, 0x12]);
        (List<DebugData> ranges, dynamic result) = layout.ParseStreamWithDebug(stream, "sample");
        Equal((byte)0xA1, (byte)result.sample.tag);
        True(ranges.Any(item => item.CurPos == 1 && item.EndPos == 3), "Value range was not reported.");

        stream.Position = 0;
        Equal(1L, layout.ResolveAddress(stream, "sample.value"));
        Equal(0L, stream.Position);
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
}
