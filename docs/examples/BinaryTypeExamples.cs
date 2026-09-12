namespace CStructSharp.Docs.Examples;

using global::CStructSharp;

internal static partial class Program
{
    #region recipe-integers-24
    private static void Integers24()
    {
        var layout = new CStruct("struct root { uint24< size; int24< delta; uint8 tail; };", aligned: true);
        byte[] bytes = layout.Serialize("root", new { size = 16777215U, delta = -2, tail = 99 });
        SequenceEqual([255, 255, 255, 254, 255, 255, 99], bytes);
        Equal(7, layout.GetStructSizeInBytes("root"));
        Equal(-2, layout.ReadValue<int>(bytes.AsSpan(), "root.delta"));
        using var stream = new MemoryStream(bytes);
        Equal(3L, layout.ResolveAddress(stream, "root.delta"));
        Throws<CStructWriteException>(() => layout.UpdateStream(stream, "root.size", 16777216U));
        SequenceEqual(bytes, stream.ToArray());
    }
    #endregion

    #region recipe-bounded-encodings
    private static void BoundedEncodings()
    {
        const string definition = "struct root { utf8 currency[3]; latin1 western[1]; cp437 dos[1]; utf16le little[4]; utf16be big[4]; uint8 tail; };";
        var layout = new CStruct(definition, aligned: false);
        var value = new { currency = "€", western = "é", dos = "é", little = "😀", big = "😀", tail = 99 };
        byte[] bytes = layout.Serialize("root", value);
        SequenceEqual(Convert.FromHexString("E282ACE9823DD800DED83DDE0063"), bytes);
        dynamic parsed = layout.Parse(bytes, "root");
        Equal("€", (string)parsed.currency);
        Equal("é", (string)parsed.dos);
        Equal("😀", (string)parsed.little);
        Equal("😀", (string)parsed.big);
        Equal((byte)99, (byte)parsed.tail);
        SequenceEqual(bytes, layout.Serialize("root", parsed));
        Throws<CStructWriteException>(() => layout.Serialize("root.currency", "€!"));
    }
    #endregion

    #region recipe-variable-integers
    private static void VariableIntegers()
    {
        var layout = new CStruct("struct root { uleb128_32 count; uleb128_64 values[count]; sleb128_32 delta; uint8 tail; };", aligned: false);
        byte[] bytes = layout.Serialize("root", new { count = 2, values = new ulong[] { 127, 128 }, delta = -65, tail = 99 });
        SequenceEqual([2, 127, 128, 1, 191, 127, 99], bytes);
        using var stream = new MemoryStream(bytes);
        Equal(2, layout.GetDynamicArrayLength(stream, "root.values"));
        Equal(4L, layout.ResolveAddress(stream, "root.delta"));
        Equal(-65, layout.ReadValue<int>(stream, "root.delta"));
        stream.Position = 0;
        layout.UpdateStream(stream, "root.values[1]", 129UL);
        SequenceEqual([2, 127, 129, 1, 191, 127, 99], stream.ToArray());
        Throws<CStructWriteException>(() => layout.UpdateStream(stream, "root.values[1]", 1UL));
        SequenceEqual([2, 127, 129, 1, 191, 127, 99], stream.ToArray());
    }
    #endregion

    #region recipe-fixed-point
    private static void FixedPoint()
    {
        var layout = new CStruct("struct root { fixed16_16> revision; ufixed8_8< volume; };", aligned: false);
        byte[] bytes = layout.Serialize("root", new { revision = -1.5, volume = 0.5 });
        SequenceEqual([255, 254, 128, 0, 128, 0], bytes);
        Equal(-1.5, layout.ReadValue<double>(bytes.AsSpan(), "root.revision"));
        Equal(0.5, layout.ReadValue<double>(bytes.AsSpan(), "root.volume"));
        using var stream = new MemoryStream(bytes);
        Throws<CStructWriteException>(() => layout.UpdateStream(stream, "root.volume", 0.1));
        SequenceEqual(bytes, stream.ToArray());
    }
    #endregion

    #region recipe-identifier-order
    private static void IdentifierOrder()
    {
        var layout = new CStruct("struct root { uuid network; guid windows; };", aligned: false);
        Guid id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        byte[] bytes = layout.Serialize("root", new { network = id, windows = id });
        SequenceEqual(Convert.FromHexString("00112233445566778899AABBCCDDEEFF33221100554477668899AABBCCDDEEFF"), bytes);
        Equal(id, layout.ReadValue<Guid>(bytes.AsSpan(), "root.network"));
        Equal(id, layout.ReadValue<Guid>(bytes.AsSpan(), "root.windows"));
        using var stream = new MemoryStream(bytes);
        Equal(16L, layout.ResolveAddress(stream, "root.windows"));
    }
    #endregion

    #region recipe-conditional-records
    private static void ConditionalRecords()
    {
        const string definition = """
            struct entry {
                uint8 kind;
                switch (kind) {
                    case 1: { utf8 label[3]; }
                    default: { uint24< number; }
                }
                if (kind == 1) { uint8 flags; }
            };
            struct root { uleb128_32 count; entry items[count]; };
            """;
        var layout = new CStruct(definition, aligned: false);
        byte[] bytes = [2, 1, 226, 130, 172, 7, 2, 42, 0, 0];
        dynamic parsed = layout.Parse(bytes, "root");
        Equal("€", (string)parsed.items[0].label);
        Equal(42U, (uint)parsed.items[1].number);
        SequenceEqual(bytes, layout.Serialize("root", parsed));
        using var stream = new MemoryStream(bytes);
        Equal(7L, layout.ResolveAddress(stream, "root.items[1].number"));
        Throws<CStructPathException>(() => layout.ResolveAddress(stream, "root.items[1].label"));
        Throws<CStructWriteException>(() => layout.UpdateStream(stream, "root.items[0].kind", 2));
        SequenceEqual(bytes, stream.ToArray());
        // Each item evaluates both groups with its own fields, including a calculation.
        const string decisions = "struct entry { uint8 tag; int8 some_parameter; if (some_parameter * 20 > 10) { uint8 high; } else { uint8 low; } switch (tag) { case 1: { uint8 first; } case 2: { uint8 second; } default: { uint8 other; } } }; struct root { entry items[3]; };";
        var decisionLayout = new CStruct(decisions, aligned: false);
        byte[] items = [1, 0, 10, 11, 2, 1, 20, 21, 3, 255, 30, 31];
        dynamic selected = decisionLayout.Parse(items, "root");
        Equal((byte)10, (byte)selected.items[0].low);
        Equal((byte)21, (byte)selected.items[1].second);
        Equal((byte)31, (byte)selected.items[2].other);
        SequenceEqual(items, decisionLayout.Serialize("root", selected));
        items[0] = 2;
        Equal((byte)11, (byte)decisionLayout.Parse(items, "root").items[0].second);
        items[0] = 1;
        items[1] = 1;
        Equal((byte)10, (byte)decisionLayout.Parse(items, "root").items[0].high);

        // A caller's count cannot replace an unread local; a short-circuit guard repairs the example.
        const string scope = "#define count 99\nstruct entry { uint8 tag; if (tag) { uint8 count; } if (count > 0) { uint8 payload[count]; } }; struct root { entry items[2]; };";
        byte[] scopedBytes = [1, 1, 42, 0];
        var variables = new Dictionary<string, int> { ["count"] = 99 };
        var scopedLayout = new CStruct(scope, aligned: false);
        Throws<CStructLayoutException>(() => scopedLayout.Parse(scopedBytes, "root", variables: variables));
        var guarded = new CStruct(scope.Replace("if (count > 0)", "if (tag != 0 && count > 0)"), aligned: false);
        dynamic scoped = guarded.Parse(scopedBytes, "root", variables: variables);
        Equal(1, ((IDictionary<string, object>)scoped.items[1]).Count);
        SequenceEqual(scopedBytes, guarded.Serialize("root", scoped, variables: variables));

        // Inactive nested expressions are skipped, but become errors when reached.
        var nested = new CStruct("struct root { uint8 tag; if (tag) { if (missing > 0) { uint8 value; } } uint8 tail; };", aligned: false);
        Equal((byte)9, (byte)nested.Parse(new byte[] { 0, 9 }, "root").tail);
        Throws<CStructLayoutException>(() => nested.Parse(new byte[] { 1, 9 }, "root"));
    }
    #endregion
}
