namespace CStructSharp.Docs.Examples;

using System.Buffers;
using System.Collections.Generic;
using System.Dynamic;
using System.Numerics;
using System.Runtime.CompilerServices;
using global::CStructSharp;
using global::CStructSharp.Diagnostics;
using global::CStructSharp.Values;

/// <summary>Runs the named executable documentation scenarios and reports failures to the documentation gate.</summary>
internal static partial class Program
{
    private static readonly (string Name, Action Run)[] Scenarios =
    [
        ("decode-header", DecodeHeader),
        ("composite-record", CompositeRecord),
        ("runtime-payload", RuntimePayload),
        ("map-poco", MapPoco),
        ("map-mapped", MapMapped),
        ("options-with", OptionsWith),
        ("cancellation", Cancellation),
        ("parse-async", () => ParseAsyncExample().GetAwaiter().GetResult()),
        ("parse-many", () => ParseManyExample().GetAwaiter().GetResult()),
        ("write-async", () => WriteAsyncExample().GetAwaiter().GetResult()),
        ("update-async", () => UpdateAsyncExample().GetAwaiter().GetResult()),
        ("try-get", TryGetAndGetOrDefault),
        ("async-stream", () => AsyncStream().GetAwaiter().GetResult()),
        ("pipe-reader", () => PipeReaderFraming().GetAwaiter().GetResult()),

        // Exercise the two-record guide independently; existing recipe exports remain self-contained.
        ("forward-only-records", () => ForwardOnlyRecords().GetAwaiter().GetResult()),

        // Verify that a partially received record survives the next pipe read.
        ("retained-record", () => RetainedRecord().GetAwaiter().GetResult()),

        ("record-sequence", () => RecordSequence().GetAwaiter().GetResult()),
        ("try-parse", TryParseForms),
        ("generated-async", () => GeneratedAsync().GetAwaiter().GetResult()),
        ("inspect-ranges", InspectRanges),
        ("follow-pointer", FollowPointer),
        ("preserve-union", PreserveUnion),
        ("preserve-enum", PreserveEnum),
        ("fixed-text", FixedText),
        ("round-trip", RoundTrip),
        ("patch-field", PatchField),
        ("header-round-trip", HeaderRoundTrip),
        ("byte-order", ByteOrder),
        ("nested-array", NestedArray),
        ("aligned-header", AlignedHeader),
        ("bit-flags", BitFlags),
        ("terminated-text", TerminatedText),
        ("invalid-path", InvalidPath),
        ("bounded-read", BoundedRead),
        ("relative-pointer", RelativePointer),
        ("positioned-stream", PositionedStream),
        ("edit-file", EditFile),
        ("integers-24", Integers24),
        ("bounded-encodings", BoundedEncodings),
        ("variable-integers", VariableIntegers),
        ("fixed-point", FixedPoint),
        ("identifier-order", IdentifierOrder),
        ("conditional-records", ConditionalRecords),
        ("windows-header", WindowsHeader),
        ("flags-and-data-sized-arrays", FlagsAndDataSizedArrays),
        ("header-preprocessor", HeaderPreprocessor),
        ("layout-introspection", LayoutIntrospection),
        ("custom-codec", CustomCodec),
        ("generated-first-layout", GeneratedFirstLayout),
        ("generated-views", GeneratedViews),
        ("generated-arrays-strings-enums", GeneratedArraysStringsEnums),
        ("generated-unions-bitfields-nested", GeneratedUnionsBitfieldsNested),
        ("generated-pointers", GeneratedPointers),
        ("generated-conditionals", GeneratedConditionals),
        ("generated-writing-updating", GeneratedWritingUpdating),
        ("generated-mapped-classes", GeneratedMappedClasses),
    ];

    public static int Main(string[] args)
    {
        if (args is ["--list"])
        {
            foreach (var scenario in Scenarios)
            {
                Console.WriteLine(scenario.Name);
            }

            return 0;
        }

        var selected = args.Length == 0 ? Scenarios : Scenarios.Where(scenario => args.Contains(scenario.Name)).ToArray();
        if (args.Any(arg => !Scenarios.Any(scenario => scenario.Name == arg)))
        {
            Console.Error.WriteLine("Unknown scenario. Use --list to see names, or omit arguments to run all.");
            return 2;
        }

        foreach ((string name, Action run) in selected)
        {
            run();
            Console.WriteLine($"PASS {name}");
        }

        Console.WriteLine($"PASS {(args.Length == 0 ? "all" : "selected")} {selected.Length} scenarios");
        return 0;
    }

    #region api-reference-cstruct
    private static void DecodeHeader()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        ReadOnlySpan<byte> bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00];
        StructValue header = layout.Parse(bytes, "header");
        Equal((ushort)2, header.Get<ushort>("kind"));
        Equal(6U, header.Get<uint>("length"));

        bool read = layout.TryReadValue<Header>(bytes, "header", out Header? typed);
        True(read && typed is { Kind: 2, Length: 6 }, "Typed header result differed.");
        True(!layout.TryReadValue<Header>(bytes[..1], "header", out _), "Truncated TryReadValue should fail.");
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
        StructValue record = layout.Parse(bytes, "record");

        EnumValueResult type = record.Get<EnumValueResult>("type");
        Equal("Text", type.Name);
        Equal("AB\0", record.Get<string>("label"));

        UnionValue payload = record.Get<UnionValue>("payload");
        Equal((byte)0x34, payload.Get<byte>("small"));
        Equal((ushort)0x1234, payload.Get<ushort>("large"));
        SequenceEqual(bytes, layout.Serialize("record", record));
    }
    #endregion

    #region language-tutorial-runtime-payload
    private static void RuntimePayload()
    {
        var layout = new CStruct("struct packet { uint8 kind; uint8 payload[COUNT]; };");
        var variables = new Dictionary<string, int> { ["COUNT"] = 3 };
        byte[] bytes = [0x7F, 0x10, 0x20, 0x30];
        StructValue packet = layout.Parse(bytes, "packet", variables);
        Equal((byte)0x7F, packet.Get<byte>("kind"));
        Equal(3, packet.Get<byte[]>("payload").Length);
        object? secondPayload = layout.ReadValue(bytes, "packet.payload[1]", variables);
        Equal((byte)0x20, (byte)secondPayload!);

        using var stream = new MemoryStream(bytes);
        stream.Position = 1;
        Equal(3, layout.GetArrayLength(stream, "packet.payload", variables));
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

    #region api-guide-options-with
    private static void OptionsWith()
    {
        var layout = new CStruct("struct sample { uint8 count; uint16 values[count]; char name[4]; };");
        byte[] bytes = [1, 0x34, 0x12, (byte)'a', (byte)'b', 0, 0];

        // One shared policy, and a variation that changes a single member.
        var strict = new ReadOptions { MaxArrayElements = 8, MaxStringBytes = 64, };
        ReadOptions trimmed = strict with { TrimFixedText = true, };
        Equal(8, trimmed.MaxArrayElements);
        Equal("ab\0\0", layout.Parse(bytes, "sample", options: strict).Get<string>("name"));
        Equal("ab", layout.Parse(bytes, "sample", options: trimmed).Get<string>("name"));

        // Records compare by their members, so an equal policy is the same policy.
        True(strict == new ReadOptions { MaxArrayElements = 8, MaxStringBytes = 64, }, "equal members, equal options");
        True(strict != trimmed, "one member differs");
    }
    #endregion

    #region api-guide-cancellation
    private static void Cancellation()
    {
        var layout = new CStruct("struct point { uint16 x; uint16 y; }; struct path { uint8 count; point points[count]; };");
        byte[] bytes = [2, 1, 0, 2, 0, 3, 0, 4, 0];

        // A token that is already cancelled ends the read at its first boundary, before any byte is decoded.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        var options = new ReadOptions { CancellationToken = cancelled.Token, };
        Throws<OperationCanceledException>(() => layout.Parse(bytes, "path", options: options));

        // The same options with a live token read normally; a timeout is the usual source of one.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        StructValue path = layout.Parse(bytes, "path", options: options with { CancellationToken = timeout.Token, });
        Equal(2, path.Get<StructValue[]>("points").Length);
    }
    #endregion

    #region api-guide-parse-async
    private static async Task ParseAsyncExample()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        byte[] bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00, 0xFF];
        string path = Path.Combine(Path.GetTempPath(), $"cstructsharp-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, bytes);
        try
        {
            // The bytes are read with ReadAsync while the thread is free; the decode itself is the ordinary reader.
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            StructValue header = await layout.ParseAsync(file, "header", cancellationToken: timeout.Token);
            Equal(6u, header.Get<uint>("length"));
            Equal(6L, file.Position);

            // The non-throwing form reports a failure instead of throwing it; the stream is back at its origin.
            file.Position = 3;
            ReadAttempt<StructValue> attempt = await layout.TryReadValueAsync<StructValue>(file, "header");
            True(!attempt.Succeeded, "four bytes are not a header");
            True(attempt.Failure is CStructReadException, "the failure is the read exception the throwing form raises");
            Equal(3L, file.Position);
        }
        finally
        {
            File.Delete(path);
        }
    }
    #endregion

    #region api-guide-parse-many
    private static async Task ParseManyExample()
    {
        // Three fixed-size entries, then two count-prefixed frames: each record is parsed when the loop reaches it.
        var log = new CStruct("struct entry { uint16 id; uint8 level; };");
        byte[] entries = [1, 0, 3, 2, 0, 1, 3, 0, 2];
        var levels = new List<byte>();
        foreach (StructValue entry in log.ParseMany(entries, "entry"))
        {
            levels.Add(entry.Get<byte>("level"));
        }

        Equal("3,1,2", string.Join(",", levels));

        var frames = new CStruct("struct frame { uint8 count; uint8 payload[count]; };");
        byte[] framed = [2, 0xAA, 0xBB, 1, 0xCC];
        using var stream = new MemoryStream(framed);
        var sizes = new List<int>();
        await foreach (StructValue frame in frames.ParseManyAsync(stream, "frame"))
        {
            sizes.Add(frame.Get<byte[]>("payload").Length);
        }

        Equal("2,1", string.Join(",", sizes));

        // A trailing byte that is not a whole entry fails on the step that reaches it, naming the record.
        byte[] trailing = [1, 0, 3, 9];
        using IEnumerator<StructValue> records = log.ParseMany(trailing, "entry").GetEnumerator();
        True(records.MoveNext(), "the first entry is whole");
        try
        {
            records.MoveNext();
            True(false, "unreachable");
        }
        catch (CStructReadException failure)
        {
            True(failure.Message.Contains("remaining 1 bytes are not a whole number of 3-byte elements", StringComparison.Ordinal), failure.Message);
            Equal("[1].entry", failure.Path);
        }
    }
    #endregion

    #region api-guide-write-async
    private static async Task WriteAsyncExample()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        var value = new Dictionary<string, object?> { ["kind"] = (ushort)2, ["length"] = 6u, };
        using var stream = new MemoryStream();

        // Validation happens before the write: a rejected value leaves the stream empty.
        await layout.WriteAsync(stream, "header", value);
        SequenceEqual([0x02, 0x00, 0x06, 0x00, 0x00, 0x00], stream.ToArray());
        try
        {
            await layout.WriteAsync(stream, "header", new Dictionary<string, object?> { ["kind"] = 70000, ["length"] = 6u, });
            True(false, "70000 does not fit uint16");
        }
        catch (CStructWriteException)
        {
            Equal(6L, stream.Length);
        }
    }
    #endregion

    #region api-guide-update-async
    private static async Task UpdateAsyncExample()
    {
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        using var stream = new MemoryStream([0x02, 0x00, 0x06, 0x00, 0x00, 0x00]);

        // Only the two bytes of 'kind' are written back; the position returns to the origin.
        await layout.UpdateAsync(stream, "header.kind", (ushort)3);
        SequenceEqual([0x03, 0x00, 0x06, 0x00, 0x00, 0x00], stream.ToArray());
        Equal(0L, stream.Position);
    }
    #endregion

    #region api-guide-try-get
    private static void TryGetAndGetOrDefault()
    {
        var layout = new CStruct("struct message { uint8 kind; if (kind == 1) { uint32 code; } uint8 tail; };");
        StructValue plain = layout.Parse([2, 9], "message");

        // 'code' belongs to an arm that was not selected: absent, a path failure.
        True(!plain.TryGet("code", out uint _, out CStructException? absent), "code was not read");
        True(absent is CStructPathException, "absent members are path failures");
        Equal(0u, plain.GetOrDefault("code", 0u));

        // 'kind' is there but 200 does not fit a signed byte: unconvertible, a read failure.
        StructValue wide = layout.Parse([200, 9], "message");
        True(!wide.TryGet("kind", out sbyte _, out CStructException? unconvertible), "200 is not an sbyte");
        True(unconvertible is CStructReadException, "conversions that lose data are read failures");
        Equal((byte)200, wide.GetOrDefault("kind", (byte)0));
    }
    #endregion

    #region api-guide-map-mapped
    private static void MapMapped()
    {
        var layout = new CStruct("struct point { int16 x; int16 y; };");
        byte[] bytes = [0xFE, 0xFF, 0x05, 0x00];

        // The generated mapper matches X to x by name (case-insensitively) and converts with Get<short>'s checks.
        MappedPoint point = layout.ReadValue<MappedPoint>(bytes, "point");
        Equal((short)-2, point.X);
        Equal((short)5, point.Y);

        // The same class writes back through the generated WriteTo.
        SequenceEqual(bytes, layout.Serialize("point", point));
    }
    #endregion

    #region api-reference-debug-data
    private static void InspectRanges()
    {
        var layout = new CStruct("struct sample { uint8 tag; uint16 value; };");
        using var stream = new MemoryStream([0xA1, 0x34, 0x12]);
        (StructValue result, IReadOnlyList<DebugData> ranges) = layout.ParseWithDebug(stream, "sample");
        Equal((byte)0xA1, result.Get<byte>("tag"));
        True(ranges.Any(item => item.Start == 1 && item.End == 3), "Value range was not reported.");

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
        StructValue root = layout.Parse(stream, "root");
        Pointer pointer = root.Get<Pointer>("target");
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
        StructValue value = layout.Parse(new byte[] { 0x41, 0x42, 0x43, 0x00 }, "label");
        Equal("ABC\0", value.Get<string>("text"));
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
        layout.Update(stream, "root.value.flags", (byte)0xA5);
        SequenceEqual([0xEE, 0xEE, 0x34, 0x12, 0xA5], stream.ToArray());
        Equal(2L, stream.Position);

        byte[] before = stream.ToArray();
        Throws<CStructWriteException>(() => layout.Update(stream, "root.value.flags", 999));
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

    public sealed class Header : ICStructMapped<Header>
    {
        public ushort Kind { get; set; }

        public uint Length { get; set; }

        public static Header ReadFrom(StructValue source)
        {
            return new Header { Kind = source.Get<ushort>("kind"), Length = source.Get<uint>("length") };
        }

        public static void WriteTo(Header value, StructValue target)
        {
            target["kind"] = value.Kind;
            target["length"] = value.Length;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Header>();
        }
    }

    #region api-guide-map-mapped-type
    // The generator writes ReadFrom, WriteTo, and the registration for this partial class.
    [CStructMapped]
    public sealed partial class MappedPoint
    {
        public short X { get; set; }

        public short Y { get; set; }
    }
    #endregion

    #region api-guide-map-poco-type
    public sealed class Point : ICStructMapped<Point>
    {
        public short X { get; set; }

        public short Y { get; set; }

        // The mapper names the layout members it reads; Get<T> converts each one with range checks.
        public static Point ReadFrom(StructValue source)
        {
            return new Point { X = source.Get<short>("x"), Y = source.Get<short>("y") };
        }

        public static void WriteTo(Point value, StructValue target)
        {
            target["x"] = value.X;
            target["y"] = value.Y;
        }

        // Runs before any other code in the assembly; the [CStructMapped] generator emits the same registration.
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Point>();
        }
    }
    #endregion
}
