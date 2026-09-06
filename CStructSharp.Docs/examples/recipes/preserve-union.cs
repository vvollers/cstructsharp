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
        PreserveUnion();
        Console.WriteLine("PASS preserve-union");
    }

    private static void PreserveUnion()
    {
        var layout = new CStruct("union choice { uint8 small; uint16 large; };");
        UnionValue parsed = layout.ReadValue<UnionValue>(new byte[] { 0x34, 0x12 }, "choice");
        Equal("choice", parsed.UnionName);
        Equal((ushort)0x1234, (ushort)parsed.Members["large"]!);
        SequenceEqual([0x34, 0x12], layout.Serialize("choice", parsed));

        UnionValue selected = UnionValue.FromMember("choice", "small", (byte)0xA5);
        SequenceEqual([0xA5, 0x00], layout.Serialize("choice", selected));
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
