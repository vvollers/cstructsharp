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
        FixedText();
        Console.WriteLine("PASS fixed-text");
    }

    private static void FixedText()
    {
        var layout = new CStruct("struct label { char text[4]; };");
        dynamic value = layout.Parse(new byte[] { 0x41, 0x42, 0x43, 0x00 }, "label");
        Equal("ABC\0", (string)value.text);
        SequenceEqual(
            [0x58, 0x59, 0x00, 0x00],
            layout.Serialize("label", new Dictionary<string, object?> { ["text"] = "XY" }));
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
