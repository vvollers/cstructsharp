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
        PatchField();
        Console.WriteLine("PASS patch-field");
    }

    private static void PatchField()
    {
        var layout = new CStruct("struct item { uint16 id; uint8 flags; }; struct root { item value; };");
        using var stream = new MemoryStream([0xEE, 0xEE, 0x34, 0x12, 0x01]);
        stream.Position = 2;
        layout.UpdateStream(stream, "root.value.flags", (byte)0xA5);
        SequenceEqual([0xEE, 0xEE, 0x34, 0x12, 0xA5], stream.ToArray());
        Equal(2L, stream.Position);

        byte[] before = stream.ToArray();
        Throws<CStructWriteException>(() => layout.UpdateStream(stream, "root.value.flags", 999));
        SequenceEqual(before, stream.ToArray());
        Equal(2L, stream.Position);
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

    private static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
