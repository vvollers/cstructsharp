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
        EditFile();
        Console.WriteLine("PASS edit-file");
    }

    private static void EditFile()
    {
        // Teaching format: signature C S, version 1, count, then count records (uint16 id, uint8 flags).
        byte[] fixture = [0x43, 0x53, 1, 2, 1, 0, 0x10, 2, 0, 0x20];
        string file = Path.Combine(Path.GetTempPath(), "cstructsharp-example-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            File.WriteAllBytes(file, fixture);
            byte[] input = File.ReadAllBytes(file);
            var headerLayout = new CStruct("struct header { uint8 signature[2]; uint8 version; uint8 count; };");
            var recordsLayout = new CStruct("struct record { uint16 id; uint8 flags; }; struct data { record records[COUNT]; };");

            byte[] Patch(byte[] data)
            {
                if (data.Length < 4 || data[0] != 0x43 || data[1] != 0x53 || data[2] != 1)
                {
                    throw new InvalidDataException("Expected a CS file, version 1, with a complete header.");
                }

                int count = (byte)headerLayout.ReadValue(data.AsSpan(), "header.count")!;
                if (count is < 2 or > 32 || data.Length != 4 + count * 3)
                {
                    throw new InvalidDataException("Expected 2 through 32 complete records and no trailing data.");
                }

                var variables = new Dictionary<string, int> { ["COUNT"] = count };
                using var stream = new MemoryStream((byte[])data.Clone());
                stream.Position = 4;
                Equal((ushort)2, (ushort)recordsLayout.ReadValue(stream, "data.records[1].id", variables)!);
                stream.Position = 4;
                recordsLayout.UpdateStream(stream, "data.records[1].flags", 0xA5, variables);
                return stream.ToArray();
            }

            byte[] output = Patch(input);
            SequenceEqual([0x43, 0x53, 1, 2, 1, 0, 0x10, 2, 0, 0xA5], output);
            SequenceEqual(input[..9], output[..9]);
            Throws<InvalidDataException>(() => Patch(input[..^1]));
            byte[] excessive = (byte[])input.Clone();
            excessive[3] = 255;
            Throws<InvalidDataException>(() => Patch(excessive));
            Console.WriteLine(Convert.ToHexString(output));
        }
        finally
        {
            File.Delete(file);
        }
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
