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
        PreserveEnum();
        Console.WriteLine("PASS preserve-enum");
    }

    private static void PreserveEnum()
    {
        var layout = new CStruct("enum state : uint32 { Known = 1 }; struct root { state value; };");
        var value = (EnumValueResult)layout.ReadValue(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, "root.value")!;
        Equal(new BigInteger(uint.MaxValue), value.Value);
        Equal(null, value.Name);
        Equal(32, value.BitWidth);
        True(!value.IsSigned, "uint32 enum should be unsigned.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}
