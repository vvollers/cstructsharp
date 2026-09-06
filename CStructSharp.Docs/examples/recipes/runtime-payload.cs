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
        RuntimePayload();
        Console.WriteLine("PASS runtime-payload");
    }

    private static void RuntimePayload()
    {
        var layout = new CStruct("struct packet { uint8 kind; uint8 payload[COUNT]; };");
        var variables = new Dictionary<string, int> { ["COUNT"] = 3 };
        byte[] bytes = [0x7F, 0x10, 0x20, 0x30];
        dynamic packet = layout.Parse(bytes, "packet", variables);
        Equal((byte)0x7F, (byte)packet.kind);
        Equal(3, ((IList<object?>)packet.payload).Count);
        object? secondPayload = layout.ReadValue(bytes, "packet.payload[1]", variables);
        Equal((byte)0x20, (byte)secondPayload!);

        using var stream = new MemoryStream(bytes);
        stream.Position = 1;
        Equal(3, layout.GetDynamicArrayLength(stream, "packet.payload", variables));
        Equal(1L, stream.Position);
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', received '{actual}'.");
        }
    }
}
