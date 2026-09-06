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
        BitFlags();
        Console.WriteLine("PASS bit-flags");
    }

    private static void BitFlags()
    {
        var layout = new CStruct("struct flags { uint8 enabled : 1; uint8 mode : 3; uint8 reserved : 4; };");
        byte[] bytes = [0x0B];
        dynamic flags = layout.Parse(bytes.AsSpan(), "flags");
        Equal(1, Convert.ToInt32(flags.enabled));
        Equal(5, Convert.ToInt32(flags.mode));
        SequenceEqual(bytes, layout.Serialize("flags", flags));
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
