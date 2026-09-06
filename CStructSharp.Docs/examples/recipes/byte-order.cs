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
        ByteOrder();
        Console.WriteLine("PASS byte-order");
    }

    private static void ByteOrder()
    {
        const string definition = "struct header { uint16 kind; uint32 length; };";
        byte[] bytes = [2, 0, 6, 0, 0, 0];
        var little = new CStruct(definition, isLittleEndian: true);
        var big = new CStruct(definition, isLittleEndian: false);
        Equal((ushort)2, (ushort)little.ReadValue(bytes.AsSpan(), "header.kind")!);
        Equal((ushort)512, (ushort)big.ReadValue(bytes.AsSpan(), "header.kind")!);
        Equal(100663296U, (uint)big.ReadValue(bytes.AsSpan(), "header.length")!);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }
}
