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
        NestedArray();
        Console.WriteLine("PASS nested-array");
    }

    private static void NestedArray()
    {
        var layout = new CStruct("struct item { uint16 id; }; struct packet { item items[2]; };");
        byte[] bytes = [1, 0, 2, 0];
        Equal((ushort)2, (ushort)layout.ReadValue(bytes.AsSpan(), "packet.items[1].id")!);
        object packet = layout.Parse(bytes.AsSpan(), "packet");
        SequenceEqual(bytes, layout.Serialize("packet", packet));
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
