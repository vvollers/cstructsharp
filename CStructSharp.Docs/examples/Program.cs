namespace CStructSharp.Docs.Examples;

using System.Buffers;
using System.Collections.Generic;
using System.Dynamic;
using System.Numerics;
using global::CStructSharp;

internal static class Program
{
    private static readonly (string Name, Action Run)[] Scenarios =
    [
        ("decode-header", DecodeHeader),
        ("composite-record", CompositeRecord),
        ("runtime-payload", RuntimePayload),
        ("map-poco", MapPoco),
        ("inspect-ranges", InspectRanges),
        ("follow-pointer", FollowPointer),
        ("preserve-union", PreserveUnion),
        ("preserve-enum", PreserveEnum),
        ("fixed-text", FixedText),
        ("round-trip", RoundTrip),
        ("patch-field", PatchField),
    ];

    public static int Main()
    {
        foreach ((string name, Action run) in Scenarios)
        {
            run();
            Console.WriteLine($"PASS {name}");
        }

        Console.WriteLine($"PASS all {Scenarios.Length} scenarios");
        return 0;
    }

    #region api-reference-cstruct
    private static void DecodeHeader()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        ReadOnlySpan<byte> bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00];
        dynamic header = layout.Parse(bytes, "header");
        Equal((ushort)2, (ushort)header.kind);
        Equal(6U, (uint)header.length);

        bool read = layout.TryReadValue<Header>(bytes, out Header? typed, "header");
        True(read && typed is { Kind: 2, Length: 6 }, "Typed header result differed.");
        True(!layout.TryReadValue<Header>(bytes[..1], out _, "header"), "Truncated TryReadValue should fail.");
    }
    #endregion

    #region language-tutorial-composite-record
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
    #endregion

    #region language-tutorial-runtime-payload
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
    #endregion

    #region api-guide-map-poco
    private static void MapPoco()
    {
        var layout = new CStruct("struct point { int16 x; int16 y; };");
        Point point = layout.ReadValue<Point>(new byte[] { 0xFE, 0xFF, 0x05, 0x00 }, "point");
        Equal((short)-2, point.X);
        Equal((short)5, point.Y);
    }
    #endregion

    #region api-reference-debug-data
    private static void InspectRanges()
    {
        var layout = new CStruct("struct sample { uint8 tag; uint16 value; };");
        using var stream = new MemoryStream([0xA1, 0x34, 0x12]);
        (List<DebugData> ranges, dynamic result) = layout.ParseStreamWithDebug(stream, "sample");
        Equal((byte)0xA1, (byte)result.sample.tag);
        True(ranges.Any(item => item.CurPos == 1 && item.EndPos == 3), "Value range was not reported.");

        stream.Position = 0;
        Equal(1L, layout.ResolveAddress(stream, "sample.value"));
        Equal(0L, stream.Position);
    }
    #endregion

    #region api-reference-pointer-read-options
    private static void FollowPointer()
    {
        var layout = new CStruct("struct root { uint8 *target; };", pointerSize: 1);
        using var stream = new MemoryStream([0x01, 0x2A]);
        dynamic root = layout.ParseStream(stream, "root");
        var pointer = (Pointer)root.target;
        Equal(1L, pointer.Address);
        True(pointer.IsDereferenced, "Pointer should be followed by default.");
        Equal((byte)0x2A, (byte)pointer.Value!);
    }
    #endregion

    #region api-reference-union
    private static void PreserveUnion()
    {
        var layout = new CStruct("union choice { uint8 small; uint16 large; };");
        UnionValue parsed = layout.ReadValue<UnionValue>(new byte[] { 0x34, 0x12 }, "choice");
        Equal("choice", parsed.UnionName);
        Equal((ushort)0x1234, (ushort)parsed.Members["large"]!);
        SequenceEqual([0x34, 0x12], layout.Serialize("choice", parsed));

        UnionValue selected = UnionValue.FromMember("choice", "small", (byte)0xA5);
        SequenceEqual([0xA5, 0x00], layout.Serialize("choice", selected));
    }
    #endregion

    #region api-reference-enum
    private static void PreserveEnum()
    {
        var layout = new CStruct("enum state : uint32 { Known = 1 }; struct root { state value; };");
        var value = (EnumValueResult)layout.ReadValue(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF }, "root.value")!;
        Equal(new BigInteger(uint.MaxValue), value.Value);
        Equal(null, value.Name);
        Equal(32, value.BitWidth);
        True(!value.IsSigned, "uint32 enum should be unsigned.");
    }
    #endregion

    #region language-tutorial-fixed-text
    private static void FixedText()
    {
        var layout = new CStruct("struct label { char text[4]; };");
        dynamic value = layout.Parse(new byte[] { 0x41, 0x42, 0x43, 0x00 }, "label");
        Equal("ABC\0", (string)value.text);
        SequenceEqual(
            [0x58, 0x59, 0x00, 0x00],
            layout.Serialize("label", new Dictionary<string, object?> { ["text"] = "XY" }));
    }
    #endregion

    #region api-reference-write-options
    private static void RoundTrip()
    {
        var layout = new CStruct("struct sample { uint16 id; uint8 flags; };");
        byte[] input = [0x34, 0x12, 0xA5];
        object parsed = layout.Parse(input, "sample");
        SequenceEqual(input, layout.Serialize("sample", parsed));

        Span<byte> destination = stackalloc byte[8];
        destination.Fill(0xCC);
        int written = layout.Serialize(destination, "sample", parsed);
        Equal(3, written);
        SequenceEqual(input, destination[..written].ToArray());
        Equal((byte)0xCC, destination[written]);

        var writer = new ArrayBufferWriter<byte>();
        Equal(3L, layout.Serialize(writer, "sample", parsed));
        SequenceEqual(input, writer.WrittenSpan.ToArray());
    }
    #endregion

    #region api-reference-update-options
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
    #endregion

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

    public sealed class Header
    {
        public ushort Kind { get; set; }

        public uint Length { get; set; }
    }

    #region api-guide-map-poco-type
    public sealed class Point
    {
        public short X { get; set; }

        public short Y { get; set; }
    }
    #endregion
}
