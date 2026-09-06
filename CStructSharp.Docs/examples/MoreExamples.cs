namespace CStructSharp.Docs.Examples;

using System.Collections.Generic;
using global::CStructSharp;

internal static partial class Program
{
    #region recipe-header-round-trip
    private static void HeaderRoundTrip()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        byte[] bytes = layout.Serialize("header", new Dictionary<string, object?> { ["kind"] = 2, ["length"] = 6 });
        SequenceEqual([2, 0, 6, 0, 0, 0], bytes);
        using var stream = new MemoryStream(bytes);
        layout.UpdateStream(stream, "header.kind", 3);
        SequenceEqual([3, 0, 6, 0, 0, 0], stream.ToArray());
        Header header = layout.ReadValue<Header>(stream.ToArray().AsSpan(), "header");
        Equal((ushort)3, header.Kind);
        Equal(6U, header.Length);
        Console.WriteLine($"Updated kind = {header.Kind}; length = {header.Length}");
    }
    #endregion

    #region recipe-byte-order
    private static void ByteOrder()
    {
        const string definition = "struct header { uint16 kind; uint32 length; };";
        byte[] bytes = [2, 0, 6, 0, 0, 0];
        var little = new CStruct(definition, isLittleEndian: true);
        var big = new CStruct(definition, isLittleEndian: false);
        Equal((ushort)2, (ushort)little.ReadValue(bytes.AsSpan(), "header.kind")!);
        Equal((ushort)512, (ushort)big.ReadValue(bytes.AsSpan(), "header.kind")!);
        Equal(100663296U, (uint)big.ReadValue(bytes.AsSpan(), "header.length")!);
    }
    #endregion

    #region recipe-nested-array
    private static void NestedArray()
    {
        var layout = new CStruct("struct item { uint16 id; }; struct packet { item items[2]; };");
        byte[] bytes = [1, 0, 2, 0];
        Equal((ushort)2, (ushort)layout.ReadValue(bytes.AsSpan(), "packet.items[1].id")!);
        object packet = layout.Parse(bytes.AsSpan(), "packet");
        SequenceEqual(bytes, layout.Serialize("packet", packet));
    }
    #endregion

    #region recipe-aligned-header
    private static void AlignedHeader()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };", aligned: true);
        byte[] bytes = [2, 0, 0, 0, 6, 0, 0, 0];
        Equal(6U, (uint)layout.ReadValue(bytes.AsSpan(), "header.length")!);
        using var stream = new MemoryStream(bytes);
        Equal(4L, layout.ResolveAddress(stream, "header.length"));
        SequenceEqual(bytes, layout.Serialize("header", layout.Parse(bytes.AsSpan(), "header")));
    }
    #endregion

    #region recipe-bit-flags
    private static void BitFlags()
    {
        var layout = new CStruct("struct flags { uint8 enabled : 1; uint8 mode : 3; uint8 reserved : 4; };");
        byte[] bytes = [0x0B];
        dynamic flags = layout.Parse(bytes.AsSpan(), "flags");
        Equal(1, Convert.ToInt32(flags.enabled));
        Equal(5, Convert.ToInt32(flags.mode));
        SequenceEqual(bytes, layout.Serialize("flags", flags));
    }
    #endregion

    #region recipe-terminated-text
    private static void TerminatedText()
    {
        var layout = new CStruct("struct label { char text[]; };");
        byte[] bytes = [0x41, 0x42, 0];
        dynamic label = layout.Parse(bytes.AsSpan(), "label");
        Equal("AB", (string)label.text);
        SequenceEqual(bytes, layout.Serialize("label", label));
    }
    #endregion

    #region recipe-invalid-path
    private static void InvalidPath()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        byte[] bytes = [2, 0, 6, 0, 0, 0];
        Throws<CStructPathException>(() => layout.ReadValue(bytes.AsSpan(), "Header.kind"));
        Equal((ushort)2, (ushort)layout.ReadValue(bytes.AsSpan(), "header.kind")!);
    }
    #endregion

    #region recipe-bounded-read
    private static void BoundedRead()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        byte[] bytes = [2, 0, 6, 0, 0, 0];
        Throws<CStructReadLimitException>(() => layout.Parse(bytes.AsSpan(), "header", options: new ReadOptions { MaxTotalBytesRead = 3 }));
        dynamic header = layout.Parse(bytes.AsSpan(), "header", options: new ReadOptions { MaxTotalBytesRead = 6 });
        Equal(6U, (uint)header.length);
    }
    #endregion

    #region recipe-relative-pointer
    private static void RelativePointer()
    {
        var layout = new CStruct("struct root { uint8 *target; };", pointerSize: 1);
        byte[] bytes = [0xEE, 1, 42];
        using var stream = new MemoryStream(bytes);
        stream.Position = 1;
        dynamic root = layout.ParseStream(stream, "root", options: new ReadOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = 1,
            MaxPointerDepth = 1,
            MaxPointerTargetBytes = 1,
        });
        var pointer = (Pointer)root.target;
        Equal(1L, pointer.Address);
        Equal((byte)42, (byte)pointer.Value!);
    }
    #endregion

    #region recipe-positioned-stream
    private static void PositionedStream()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        using var stream = new MemoryStream([0xEE, 0xEE, 2, 0, 6, 0, 0, 0]);
        stream.Position = 2;
        Equal(4L, layout.ResolveAddress(stream, "header.length"));
        Equal(2L, stream.Position);
        dynamic header = layout.ParseStream(stream, "header");
        Equal(6U, (uint)header.length);
    }
    #endregion

    #region recipe-edit-file
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
    #endregion
}

