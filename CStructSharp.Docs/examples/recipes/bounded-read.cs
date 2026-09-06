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
        BoundedRead();
        Console.WriteLine("PASS bounded-read");
    }

    private static void BoundedRead()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        byte[] bytes = [2, 0, 6, 0, 0, 0];
        Throws<CStructReadLimitException>(() => layout.Parse(bytes.AsSpan(), "header", options: new ReadOptions { MaxTotalBytesRead = 3 }));
        dynamic header = layout.Parse(bytes.AsSpan(), "header", options: new ReadOptions { MaxTotalBytesRead = 6 });
        Equal(6U, (uint)header.length);
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
