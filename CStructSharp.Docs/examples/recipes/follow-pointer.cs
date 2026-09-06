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
        FollowPointer();
        Console.WriteLine("PASS follow-pointer");
    }

    private static void FollowPointer()
    {
        var layout = new CStruct("struct root { uint8 *target; };", pointerSize: 1);
        using var stream = new MemoryStream([0x01, 0x2A]);
        dynamic root = layout.ParseStream(stream, "root");
        var pointer = (Pointer)root.target;
        Equal(1L, pointer.Address);
        True(pointer.IsDereferenced, "Pointer should be followed by default.");
        Equal((byte)0x2A, (byte)pointer.Value!);
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
