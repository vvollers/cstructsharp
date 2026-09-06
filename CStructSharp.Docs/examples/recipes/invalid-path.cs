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
        InvalidPath();
        Console.WriteLine("PASS invalid-path");
    }

    private static void InvalidPath()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        byte[] bytes = [2, 0, 6, 0, 0, 0];
        Throws<CStructPathException>(() => layout.ReadValue(bytes.AsSpan(), "Header.kind"));
        Equal((ushort)2, (ushort)layout.ReadValue(bytes.AsSpan(), "header.kind")!);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
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
