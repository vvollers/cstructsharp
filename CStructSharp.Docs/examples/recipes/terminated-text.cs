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
        TerminatedText();
        Console.WriteLine("PASS terminated-text");
    }

    private static void TerminatedText()
    {
        var layout = new CStruct("struct label { char text[]; };");
        byte[] bytes = [0x41, 0x42, 0];
        dynamic label = layout.Parse(bytes.AsSpan(), "label");
        Equal("AB", (string)label.text);
        SequenceEqual(bytes, layout.Serialize("label", label));
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
