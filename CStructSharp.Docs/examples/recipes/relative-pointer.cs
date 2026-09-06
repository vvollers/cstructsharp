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
        RelativePointer();
        Console.WriteLine("PASS relative-pointer");
    }

    private static void RelativePointer()
    {
        var layout = new CStruct("struct root { uint8 *target; };", pointerSize: 1);
        byte[] bytes = [0xEE, 1, 42];
        using var stream = new MemoryStream(bytes);
        stream.Position = 1;
        dynamic root = layout.ParseStream(stream, "root", options: new ReadOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = 1,
            MaxPointerDepth = 1,
            MaxPointerTargetBytes = 1,
        });
        var pointer = (Pointer)root.target;
        Equal(1L, pointer.Address);
        Equal((byte)42, (byte)pointer.Value!);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }
}
