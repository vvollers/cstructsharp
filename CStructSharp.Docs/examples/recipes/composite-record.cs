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
        CompositeRecord();
        Console.WriteLine("PASS composite-record");
    }

    private static void CompositeRecord()
    {
        const string definition = """
            enum kind : uint8 { Text = 1, Numbers = 2 };
            union payload_word { uint8 small; uint16 large; };
            struct record {
                kind type;
                char label[3];
                payload_word payload;
            };
            """;
        var layout = new CStruct(definition);
        byte[] bytes = [0x01, 0x41, 0x42, 0x00, 0x34, 0x12];
        dynamic record = layout.Parse(bytes, "record");

        var type = (EnumValueResult)record.type;
        Equal("Text", type.Name);
        Equal("AB\0", (string)record.label);

        var payload = (UnionValue)record.payload;
        Equal((byte)0x34, (byte)payload.Members["small"]!);
        Equal((ushort)0x1234, (ushort)payload.Members["large"]!);
        SequenceEqual(bytes, layout.Serialize("record", record));
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
