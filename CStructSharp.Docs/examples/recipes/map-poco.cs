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
        MapPoco();
        Console.WriteLine("PASS map-poco");
    }

    private static void MapPoco()
    {
        var layout = new CStruct("struct point { int16 x; int16 y; };");
        Point point = layout.ReadValue<Point>(new byte[] { 0xFE, 0xFF, 0x05, 0x00 }, "point");
        Equal((short)-2, point.X);
        Equal((short)5, point.Y);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }

    public sealed class Point
    {
        public short X { get; set; }

        public short Y { get; set; }
    }
}
