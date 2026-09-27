namespace CStructSharp.Tests;

using System.Dynamic;
using System.Globalization;
using System.Text;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>Checks the documented semantic and canonical-byte round-trip contracts over reproducible generated cases.</summary>
[TestClass]
public class RoundTripPropertyTests
{
    private const ulong FixedSeed = 0x5141303246495845UL;
    private const ulong CompositeSeed = 0x51413032434F4D50UL;
    private const ulong EnumBitfieldSeed = 0x51413032454E554DUL;
    private const ulong StringSeed = 0x514130325354524EUL;
    private const ulong PointerSeed = 0x5141303250545253UL;

    private static readonly byte[] PointerSizes = [1, 2, 4,];

    private static readonly PrimitiveSpec[] PrimitiveSpecs =
    [
        new("byte", value => (byte)value),
        new("int8", value => unchecked((sbyte)value)),
        new("uint8", value => (byte)value),
        new("char", value => (char)(0x20 + (value % 0x5F))),
        new("wchar", value => (char)(value & 0xD7FF)),
        new("wchar>", value => (char)(value & 0xD7FF)),
        new("wchar<", value => (char)(value & 0xD7FF)),
        new("int16", value => unchecked((short)value)),
        new("int16>", value => unchecked((short)value)),
        new("int16<", value => unchecked((short)value)),
        new("uint16", value => (ushort)value),
        new("uint16>", value => (ushort)value),
        new("uint16<", value => (ushort)value),
        new("int32", value => unchecked((int)value)),
        new("int32>", value => unchecked((int)value)),
        new("int32<", value => unchecked((int)value)),
        new("uint32", value => (uint)value),
        new("uint32>", value => (uint)value),
        new("uint32<", value => (uint)value),
        new("int64", value => unchecked((long)value)),
        new("int64>", value => unchecked((long)value)),
        new("int64<", value => unchecked((long)value)),
        new("uint64", value => value),
        new("uint64>", value => value),
        new("uint64<", value => value),
        new("short", value => unchecked((short)value)),
        new("ushort", value => (ushort)value),
        new("int", value => unchecked((int)value)),
        new("uint", value => (uint)value),
        new("long", value => unchecked((long)value)),
        new("ulong", value => value),
    ];

    /// <summary>
    ///     A fixed seed generates 192 mixed-scalar layouts and values.
    /// </summary>
    /// <remarks>
    ///     Writing then reading must recover the values, and re-encoding must keep canonical bytes stable. The
    ///     generator also checks primitive coverage and can simplify a failing case, making broader combinations useful
    ///     rather than unreproducible.
    /// </remarks>
    [TestMethod]
    public void GeneratedFixedLayouts_ValueAndCanonicalBytesRoundTrip()
    {
        var generatedTypes = new HashSet<string>(StringComparer.Ordinal);
        PropertyTestSupport.Check(
            "generated fixed scalar layouts",
            FixedSeed,
            192,
            random =>
            {
                FixedCase item = GenerateFixedCase(random);
                foreach (PrimitiveField field in item.Fields)
                {
                    generatedTypes.Add(field.TypeName);
                }

                return item;
            },
            AssertFixedCase,
            ShrinkFixedCase,
            FormatFixedCase);
        CollectionAssert.AreEquivalent(
            PrimitiveSpecs.Select(item => item.TypeName).ToArray(),
            generatedTypes.ToArray(),
            "The retained corpus must exercise every fixed primitive spelling.");
    }

    /// <summary>
    ///     The generated cases combine nested and inline structs, aliases, fixed or runtime arrays, text buffers, and
    ///     padding.
    /// </summary>
    /// <remarks>
    ///     Equivalent normalized values must survive writing and reading, and canonical bytes must remain stable.
    ///     Normalization accounts for result representations such as dynamic lists without weakening the binary checks.
    /// </remarks>
    [TestMethod]
    public void GeneratedCompositeLayouts_NormalizedValuesAndCanonicalBytesRoundTrip()
    {
        PropertyTestSupport.Check(
            "generated composite layouts",
            CompositeSeed,
            128,
            GenerateCompositeCase,
            AssertCompositeCase,
            ShrinkCompositeCase,
            FormatCompositeCase);
    }

    /// <summary>
    ///     Generated records combine known or unnamed enum values with bitfields that fill their storage units.
    /// </summary>
    /// <remarks>
    ///     Values must remain exact through read/write cycles and produce stable bytes. Filling every storage bit
    ///     avoids confusing discarded unused bits with a genuine loss of field data.
    /// </remarks>
    [TestMethod]
    public void GeneratedEnumAndBitfieldValues_ValueAndCanonicalBytesRoundTrip()
    {
        PropertyTestSupport.Check(
            "generated enum and bitfield values",
            EnumBitfieldSeed,
            192,
            GenerateEnumBitfieldCase,
            AssertEnumBitfieldCase,
            ShrinkEnumBitfieldCase,
            FormatEnumBitfieldCase);
    }

    /// <summary>
    ///     Each registered terminated-text type receives generated text valid for its encoding.
    /// </summary>
    /// <remarks>
    ///     Writing and reading must recover the text and preserve complete terminator bytes on re-encoding. Cases
    ///     respect encoding restrictions so failures reveal conversion or termination defects rather than intentionally
    ///     invalid input.
    /// </remarks>
    [TestMethod]
    public void GeneratedTerminatedStrings_ValueAndCanonicalBytesRoundTrip()
    {
        string[] terminatedTypes = LoadCatalog().
            RootElement.
            GetProperty("primitiveSpellings").
            GetProperty("terminated").
            EnumerateArray().
            Select(item => item.GetString()!).
            ToArray();
        int generated = 0;

        PropertyTestSupport.Check(
            "generated terminated strings",
            StringSeed,
            terminatedTypes.Length * 12,
            random =>
            {
                string typeName = terminatedTypes[generated++ % terminatedTypes.Length];
                return GenerateStringCase(random, typeName);
            },
            AssertStringCase,
            ShrinkStringCase,
            FormatStringCase);
    }

    /// <summary>
    ///     Generated one- and two-level pointer records must preserve their stored addresses and root bytes after
    ///     parsing and serialization.
    /// </summary>
    /// <remarks>
    ///     Target graph bytes remain outside root serialization. Reading a target does not make the serializer
    ///     allocate, relocate, or copy that external target automatically.
    /// </remarks>
    [TestMethod]
    public void GeneratedPointerStorage_AddressAndCanonicalRootBytesRoundTrip()
    {
        PropertyTestSupport.Check(
            "generated pointer storage",
            PointerSeed,
            128,
            GeneratePointerCase,
            AssertPointerCase,
            ShrinkPointerCase,
            FormatPointerCase);
    }

    /// <summary>
    ///     Every catalog feature must describe both value round trips and byte round trips, including the conditions
    ///     under which each holds.
    /// </summary>
    /// <remarks>
    ///     The test checks that none are omitted. Equal decoded values do not always imply preservation of arbitrary
    ///     padding or unselected storage, so these promises are recorded separately.
    /// </remarks>
    [TestMethod]
    public void Catalog_ClassifiesRoundTripContractsForEveryFeature()
    {
        using JsonDocument document = LoadCatalog();
        string[] features = document.RootElement.
            GetProperty("features").
            EnumerateArray().
            Select(item => item.GetProperty("id").GetString()!).
            OrderBy(item => item, StringComparer.Ordinal).
            ToArray();
        JsonElement[] contracts = document.RootElement.
            GetProperty("roundTripContracts").
            EnumerateArray().
            ToArray();
        string[] classified = contracts.
            Select(item => item.GetProperty("featureId").GetString()!).
            OrderBy(item => item, StringComparer.Ordinal).
            ToArray();

        CollectionAssert.AreEqual(features, classified);
        foreach (JsonElement contract in contracts)
        {
            foreach (string property in new[] { "value", "bytes", })
            {
                JsonElement classification = contract.GetProperty(property);
                Assert.IsTrue(classification.TryGetProperty("status", out _));
                Assert.IsTrue(classification.TryGetProperty("conditions", out JsonElement conditions));
                Assert.IsGreaterThan(0, conditions.GetArrayLength());
            }
        }
    }

    /// <summary>
    ///     Generates a root layout of one to seven randomly chosen fixed primitive fields and their values.
    /// </summary>
    /// <param name="random">The reproducible source that selects byte order, alignment, types, and values.</param>
    /// <returns>The generated fixed-layout case.</returns>
    private static FixedCase GenerateFixedCase(PropertyTestSupport.StableRandom random)
    {
        int fieldCount = random.NextInt(7) + 1;
        var fields = new List<PrimitiveField>(fieldCount);
        for (int index = 0; index < fieldCount; index++)
        {
            PrimitiveSpec spec = PrimitiveSpecs[random.NextInt(PrimitiveSpecs.Length)];
            fields.Add(new PrimitiveField(spec.TypeName, spec.CreateValue(random.NextUInt64())));
        }

        return new FixedCase(random.NextBoolean(), random.NextBoolean(), fields);
    }

    /// <summary>
    ///     Serializes the case's values, parses the bytes back, and checks the size, every value, and that serializing
    ///     the parsed result reproduces the first bytes.
    /// </summary>
    /// <param name="item">The fixed-layout case to check.</param>
    private static void AssertFixedCase(FixedCase item)
    {
        string layout = "struct root { " +
                        string.Join(
                            ' ',
                            item.Fields.Select((field, index) => field.TypeName + " field" + index + ";")) +
                        " };";
        var cstruct = new CStruct(layout, aligned: item.Aligned, isLittleEndian: item.LittleEndian);
        IDictionary<string, object?> values = new ExpandoObject();
        for (int index = 0; index < item.Fields.Count; index++)
        {
            values.Add("field" + index, item.Fields[index].Value);
        }

        byte[] first = cstruct.Serialize("root", values);
        using var stream = new MemoryStream(first);
        IDictionary<string, object?> parsed = cstruct.Parse(stream, "root");

        Assert.AreEqual(cstruct.GetStructSizeInBytes("root"), first.Length);
        for (int index = 0; index < item.Fields.Count; index++)
        {
            Assert.AreEqual(item.Fields[index].Value, parsed["field" + index], "field" + index);
        }

        CollectionAssert.AreEqual(first, cstruct.Serialize("root", parsed));
    }

    /// <summary>
    ///     Yields simpler variants of a failing fixed-layout case: unaligned, little-endian, fewer fields, or one field
    ///     set to zero.
    /// </summary>
    /// <param name="item">The failing case to simplify.</param>
    /// <returns>Candidate cases, each one step simpler than <paramref name="item"/>.</returns>
    private static IEnumerable<FixedCase> ShrinkFixedCase(FixedCase item)
    {
        if (item.Aligned)
        {
            yield return item with { Aligned = false, };
        }

        if (!item.LittleEndian)
        {
            yield return item with { LittleEndian = true, };
        }

        if (item.Fields.Count > 1)
        {
            yield return item with { Fields = item.Fields.Take(item.Fields.Count / 2).ToArray(), };
            yield return item with { Fields = item.Fields.Take(1).ToArray(), };
        }

        for (int index = 0; index < item.Fields.Count; index++)
        {
            object zero = PrimitiveSpecs.Single(spec => spec.TypeName == item.Fields[index].TypeName).CreateValue(0);
            if (Equals(zero, item.Fields[index].Value))
            {
                continue;
            }

            PrimitiveField[] fields = item.Fields.ToArray();
            fields[index] = fields[index] with { Value = zero, };
            yield return item with { Fields = fields, };
        }
    }

    /// <summary>Describes a fixed-layout case for a property failure message.</summary>
    /// <param name="item">The case to describe.</param>
    /// <returns>One line listing the options and each field's type and value.</returns>
    private static string FormatFixedCase(FixedCase item)
    {
        return $"endian={(item.LittleEndian ? "little" : "big")}; aligned={item.Aligned}; " +
               string.Join(
                   ", ",
                   item.Fields.Select(
                       (field, index) => $"field{index}:{field.TypeName}={FormatValue(field.Value)}"));
    }

    /// <summary>
    ///     Generates a composite case with zero to four child records and runtime array elements and a label of up to
    ///     five letters.
    /// </summary>
    /// <param name="random">The reproducible source that selects options, counts, and values.</param>
    /// <returns>The generated composite case.</returns>
    private static CompositeCase GenerateCompositeCase(PropertyTestSupport.StableRandom random)
    {
        int count = random.NextInt(5);
        var children = new ChildValue[count];
        var values = new ushort[count];
        for (int index = 0; index < count; index++)
        {
            children[index] = new ChildValue((byte)random.NextInt(256), (ushort)random.NextUInt64());
            values[index] = (ushort)random.NextUInt64();
        }

        int labelLength = random.NextInt(6);
        var label = new StringBuilder(labelLength);
        for (int index = 0; index < labelLength; index++)
        {
            label.Append((char)('A' + random.NextInt(26)));
        }

        return new CompositeCase(
            random.NextBoolean(),
            random.NextBoolean(),
            (byte)random.NextInt(256),
            (uint)random.NextUInt64(),
            children,
            values,
            label.ToString());
    }

    /// <summary>
    ///     Builds a layout with an alias, a struct array, an inline struct, a runtime-length array, and a five-byte
    ///     text buffer; then checks that serialized values parse back and that re-serialization reproduces the bytes.
    /// </summary>
    /// <param name="item">The composite case to check.</param>
    /// <remarks>The label reads back padded with NUL characters to the full buffer length.</remarks>
    private static void AssertCompositeCase(CompositeCase item)
    {
        const string prefix = """
                              typedef uint16 word;
                              struct child { byte tag; word number; };
                              """;
        string layout = prefix +
                        $" struct root {{ byte head; child children[{item.Children.Count}]; " +
                        "struct { uint32 code; } inlineValue; word values[N]; char label[5]; };";
        var variables = new Dictionary<string, Expr> { ["N"] = new Literal(item.Values.Count), };
        var cstruct = new CStruct(layout, aligned: item.Aligned, isLittleEndian: item.LittleEndian);
        IDictionary<string, object?> data = new ExpandoObject();
        data.Add("head", item.Head);
        data.Add("children", item.Children.Select(CreateChildData).ToArray());
        dynamic inlineValue = new ExpandoObject();
        inlineValue.code = item.InlineCode;
        data.Add("inlineValue", inlineValue);
        data.Add("values", item.Values.Cast<object>().ToArray());
        data.Add("label", item.Label);

        byte[] first = cstruct.Serialize("root", data, variables);
        using var stream = new MemoryStream(first);
        dynamic parsed = cstruct.Parse(stream, "root", variables);

        Assert.AreEqual(item.Head, (byte)parsed.head);
        Assert.AreEqual(item.InlineCode, (uint)parsed.inlineValue.code);
        Assert.AreEqual(item.Label.PadRight(5, '\0'), (string)parsed.label);
        var parsedChildren = (IList<object>)parsed.children;
        Assert.AreEqual(item.Children.Count, parsedChildren.Count);
        for (int index = 0; index < item.Children.Count; index++)
        {
            Assert.AreEqual(item.Children[index].Tag, (byte)((dynamic)parsedChildren[index]).tag, "child tag " + index);
            Assert.AreEqual(
                item.Children[index].Number,
                (ushort)((dynamic)parsedChildren[index]).number,
                "child number " + index);
        }

        CollectionAssert.AreEqual(
            item.Values.Cast<object>().ToArray(),
            ((IList<object>)parsed.values).ToArray());
        CollectionAssert.AreEqual(first, cstruct.Serialize("root", (object)parsed, variables));
    }

    /// <summary>
    ///     Yields simpler variants of a failing composite case: unaligned, little-endian, fewer elements, a shorter
    ///     label, or zero scalar values.
    /// </summary>
    /// <param name="item">The failing case to simplify.</param>
    /// <returns>Candidate cases, each one step simpler than <paramref name="item"/>.</returns>
    private static IEnumerable<CompositeCase> ShrinkCompositeCase(CompositeCase item)
    {
        if (item.Aligned)
        {
            yield return item with { Aligned = false, };
        }

        if (!item.LittleEndian)
        {
            yield return item with { LittleEndian = true, };
        }

        if (item.Children.Count > 0)
        {
            int count = item.Children.Count / 2;
            yield return item with
            {
                Children = item.Children.Take(count).ToArray(),
                Values = item.Values.Take(count).ToArray(),
            };
            yield return item with { Children = [], Values = [], };
        }

        if (item.Label.Length > 0)
        {
            yield return item with { Label = item.Label[..(item.Label.Length / 2)], };
            yield return item with { Label = string.Empty, };
        }

        if (item.Head != 0 || item.InlineCode != 0)
        {
            yield return item with { Head = 0, InlineCode = 0, };
        }
    }

    /// <summary>Describes a composite case for a property failure message.</summary>
    /// <param name="item">The case to describe.</param>
    /// <returns>One line listing the options, scalar values, elements, and label.</returns>
    private static string FormatCompositeCase(CompositeCase item)
    {
        return $"endian={(item.LittleEndian ? "little" : "big")}; aligned={item.Aligned}; " +
               $"head={item.Head}; inline={item.InlineCode}; count={item.Children.Count}; " +
               $"children=[{string.Join(',', item.Children)}]; values=[{string.Join(',', item.Values)}]; " +
               $"label={JsonSerializer.Serialize(item.Label)}";
    }

    /// <summary>
    ///     Generates enum and bitfield values that fit their declared widths: any 16-bit enum value, 5 bits for
    ///     <c>low</c>, and 11 bits for <c>high</c>.
    /// </summary>
    /// <param name="random">The reproducible source that selects options and values.</param>
    /// <returns>The generated enum and bitfield case.</returns>
    private static EnumBitfieldCase GenerateEnumBitfieldCase(PropertyTestSupport.StableRandom random)
    {
        return new EnumBitfieldCase(
            random.NextBoolean(),
            random.NextBoolean(),
            (byte)random.NextInt(256),
            (ushort)random.NextUInt64(),
            (byte)random.NextInt(32),
            (ushort)random.NextInt(2048),
            (byte)random.NextInt(256));
    }

    /// <summary>
    ///     Serializes an enum field and two bitfields that fill one 16-bit storage unit, then checks the parsed values
    ///     and that re-serialization reproduces the bytes.
    /// </summary>
    /// <param name="item">The enum and bitfield case to check.</param>
    private static void AssertEnumBitfieldCase(EnumBitfieldCase item)
    {
        const string layout = """
                              enum mode : uint16 { Zero=0, One=1, Maximum=65535 };
                              struct root {
                                  byte prefix;
                                  mode state;
                                  uint16 low:5;
                                  uint16 high:11;
                                  byte tail;
                              };
                              """;
        var cstruct = new CStruct(layout, aligned: item.Aligned, isLittleEndian: item.LittleEndian);
        dynamic data = new ExpandoObject();
        data.prefix = item.Prefix;
        data.state = item.EnumValue;
        data.low = item.Low;
        data.high = item.High;
        data.tail = item.Tail;

        byte[] first = cstruct.Serialize("root", data);
        using var stream = new MemoryStream(first);
        dynamic parsed = cstruct.Parse(stream, "root");

        Assert.AreEqual(item.Prefix, (byte)parsed.prefix);
        Assert.AreEqual(item.EnumValue, (ushort)((EnumValueResult)parsed.state).Value);
        Assert.AreEqual(item.Low, Convert.ToByte(parsed.low, CultureInfo.InvariantCulture));
        Assert.AreEqual(item.High, Convert.ToUInt16(parsed.high, CultureInfo.InvariantCulture));
        Assert.AreEqual(item.Tail, (byte)parsed.tail);
        CollectionAssert.AreEqual(first, cstruct.Serialize("root", parsed));
    }

    /// <summary>
    ///     Yields simpler variants of a failing enum and bitfield case: unaligned, little-endian, or all values zero.
    /// </summary>
    /// <param name="item">The failing case to simplify.</param>
    /// <returns>Candidate cases, each one step simpler than <paramref name="item"/>.</returns>
    private static IEnumerable<EnumBitfieldCase> ShrinkEnumBitfieldCase(EnumBitfieldCase item)
    {
        if (item.Aligned)
        {
            yield return item with { Aligned = false, };
        }

        if (!item.LittleEndian)
        {
            yield return item with { LittleEndian = true, };
        }

        if (item != item with { Prefix = 0, EnumValue = 0, Low = 0, High = 0, Tail = 0, })
        {
            yield return item with { Prefix = 0, EnumValue = 0, Low = 0, High = 0, Tail = 0, };
        }
    }

    /// <summary>Describes an enum and bitfield case for a property failure message.</summary>
    /// <param name="item">The case to describe.</param>
    /// <returns>One line listing the options and every field value.</returns>
    private static string FormatEnumBitfieldCase(EnumBitfieldCase item)
    {
        return $"endian={(item.LittleEndian ? "little" : "big")}; aligned={item.Aligned}; prefix={item.Prefix}; " +
               $"enum={item.EnumValue}; low={item.Low}; high={item.High}; tail={item.Tail}";
    }

    /// <summary>Generates printable ASCII text of up to 16 characters for one terminated-text type.</summary>
    /// <param name="random">The reproducible source that selects byte order, text, and tail byte.</param>
    /// <param name="typeName">The terminated-text type spelling that the case declares.</param>
    /// <returns>The generated text case.</returns>
    private static StringCase GenerateStringCase(PropertyTestSupport.StableRandom random, string typeName)
    {
        int length = random.NextInt(17);
        var value = new StringBuilder(length);
        for (int index = 0; index < length; index++)
        {
            value.Append((char)(' ' + random.NextInt(95)));
        }

        return new StringCase(random.NextBoolean(), typeName, value.ToString(), (byte)random.NextInt(256));
    }

    /// <summary>
    ///     Serializes a terminated-text field followed by a tail byte, then checks that both values parse back and that
    ///     re-serialization reproduces the bytes.
    /// </summary>
    /// <param name="item">The text case to check.</param>
    private static void AssertStringCase(StringCase item)
    {
        var cstruct = new CStruct(
            $"struct root {{ {item.TypeName} value; byte tail; }};",
            isLittleEndian: item.LittleEndian);
        dynamic data = new ExpandoObject();
        data.value = item.Value;
        data.tail = item.Tail;

        byte[] first = cstruct.Serialize("root", data);
        using var stream = new MemoryStream(first);
        dynamic parsed = cstruct.Parse(stream, "root");

        Assert.AreEqual(item.Value, (string)parsed.value);
        Assert.AreEqual(item.Tail, (byte)parsed.tail);
        CollectionAssert.AreEqual(first, cstruct.Serialize("root", parsed));
    }

    /// <summary>
    ///     Yields simpler variants of a failing text case: little-endian, shorter text, or a zero tail byte.
    /// </summary>
    /// <param name="item">The failing case to simplify.</param>
    /// <returns>Candidate cases, each one step simpler than <paramref name="item"/>.</returns>
    private static IEnumerable<StringCase> ShrinkStringCase(StringCase item)
    {
        if (!item.LittleEndian)
        {
            yield return item with { LittleEndian = true, };
        }

        if (item.Value.Length > 0)
        {
            yield return item with { Value = item.Value[..(item.Value.Length / 2)], };
            yield return item with { Value = string.Empty, };
        }

        if (item.Tail != 0)
        {
            yield return item with { Tail = 0, };
        }
    }

    /// <summary>Describes a text case for a property failure message.</summary>
    /// <param name="item">The case to describe.</param>
    /// <returns>One line listing the byte order, type, JSON-quoted text, and tail byte.</returns>
    private static string FormatStringCase(StringCase item)
    {
        return $"endian={(item.LittleEndian ? "little" : "big")}; type={item.TypeName}; " +
               $"value={JsonSerializer.Serialize(item.Value)}; tail={item.Tail}";
    }

    /// <summary>
    ///     Generates a pointer case with depth one or two, a pointer size of 1, 2, or 4 bytes, and random target and
    ///     tail values.
    /// </summary>
    /// <param name="random">The reproducible source that selects the options and values.</param>
    /// <returns>The generated pointer case.</returns>
    private static PointerCase GeneratePointerCase(PropertyTestSupport.StableRandom random)
    {
        return new PointerCase(
            random.NextBoolean() ? 1 : 2,
            PointerSizes[random.NextInt(PointerSizes.Length)],
            random.NextBoolean(),
            (ushort)random.NextUInt64(),
            (byte)random.NextInt(256));
    }

    /// <summary>
    ///     Builds a buffer whose root pointer targets byte offset 16, parses it with dereferencing, and checks the
    ///     addresses, depth, and target value.
    /// </summary>
    /// <param name="item">The pointer case to check.</param>
    /// <remarks>
    ///     At depth two, offset 16 holds a second pointer to offset 24. Serializing the parsed root must return only
    ///     the root bytes (the pointer and the tail byte), and reading those bytes without dereferencing keeps the
    ///     address.
    /// </remarks>
    private static void AssertPointerCase(PointerCase item)
    {
        const int firstTarget = 16;
        const int secondTarget = 24;
        string layout = item.Depth == 1
                            ? "struct child { uint16 value; }; struct root { child *ptr; byte tail; };"
                            : "struct root { uint16 **ptr; byte tail; };";
        var cstruct = new CStruct(
            layout,
            pointerSize: item.PointerSize,
            isLittleEndian: item.LittleEndian);
        byte[] bytes = new byte[secondTarget + sizeof(ushort)];
        WriteUnsigned(bytes, 0, firstTarget, item.PointerSize, item.LittleEndian);
        bytes[item.PointerSize] = item.Tail;
        if (item.Depth == 1)
        {
            WriteUnsigned(bytes, firstTarget, item.TargetValue, sizeof(ushort), item.LittleEndian);
        }
        else
        {
            WriteUnsigned(bytes, firstTarget, secondTarget, item.PointerSize, item.LittleEndian);
            WriteUnsigned(bytes, secondTarget, item.TargetValue, sizeof(ushort), item.LittleEndian);
        }

        using var stream = new MemoryStream(bytes);
        dynamic parsed = cstruct.Parse(stream, "root");
        var pointer = (Pointer)parsed.ptr;
        Assert.AreEqual(firstTarget, pointer.Address);
        Assert.AreEqual(item.Depth, pointer.Depth);
        Assert.AreEqual(item.Tail, (byte)parsed.tail);
        if (item.Depth == 1)
        {
            Assert.AreEqual(item.TargetValue, (ushort)((dynamic)pointer.Value!).value);
        }
        else
        {
            Assert.IsInstanceOfType<Pointer>(pointer.Value);
            var next = (Pointer)pointer.Value;
            Assert.AreEqual(secondTarget, next.Address);
            Assert.AreEqual(item.TargetValue, (ushort)next.Value!);
        }

        byte[] rootBytes = cstruct.Serialize("root", parsed);
        CollectionAssert.AreEqual(bytes.Take(item.PointerSize + 1).ToArray(), rootBytes);
        using var rootStream = new MemoryStream(rootBytes);
        dynamic reparsed = cstruct.Parse(
            rootStream,
            "root",
            new Dictionary<string, Expr>(),
            new ReadOptions { DereferencePointers = false, });
        Assert.AreEqual(firstTarget, ((Pointer)reparsed.ptr).Address);
        Assert.IsFalse(((Pointer)reparsed.ptr).IsDereferenced);
        Assert.AreEqual(item.Tail, (byte)reparsed.tail);
    }

    /// <summary>
    ///     Yields simpler variants of a failing pointer case: depth one, one-byte pointers, little-endian, or zero
    ///     values.
    /// </summary>
    /// <param name="item">The failing case to simplify.</param>
    /// <returns>Candidate cases, each one step simpler than <paramref name="item"/>.</returns>
    private static IEnumerable<PointerCase> ShrinkPointerCase(PointerCase item)
    {
        if (item.Depth > 1)
        {
            yield return item with { Depth = 1, };
        }

        if (item.PointerSize > 1)
        {
            yield return item with { PointerSize = 1, };
        }

        if (!item.LittleEndian)
        {
            yield return item with { LittleEndian = true, };
        }

        if (item.TargetValue != 0 || item.Tail != 0)
        {
            yield return item with { TargetValue = 0, Tail = 0, };
        }
    }

    /// <summary>Describes a pointer case for a property failure message.</summary>
    /// <param name="item">The case to describe.</param>
    /// <returns>One line listing the depth, pointer size, byte order, target value, and tail byte.</returns>
    private static string FormatPointerCase(PointerCase item)
    {
        return $"depth={item.Depth}; pointerSize={item.PointerSize}; " +
               $"endian={(item.LittleEndian ? "little" : "big")}; target={item.TargetValue}; tail={item.Tail}";
    }

    /// <summary>Converts a child record into the dynamic object shape that serialization accepts.</summary>
    /// <param name="item">The child values to convert.</param>
    /// <returns>An expando object with <c>tag</c> and <c>number</c> members.</returns>
    private static dynamic CreateChildData(ChildValue item)
    {
        dynamic value = new ExpandoObject();
        value.tag = item.Tag;
        value.number = item.Number;
        return value;
    }

    /// <summary>Writes the low bytes of an unsigned value into a buffer in the requested byte order.</summary>
    /// <param name="bytes">The buffer to modify.</param>
    /// <param name="offset">The byte offset of the first written byte.</param>
    /// <param name="value">The value to encode; higher bytes beyond the width are ignored.</param>
    /// <param name="width">The number of bytes to write.</param>
    /// <param name="littleEndian">Whether the least significant byte is written first.</param>
    private static void WriteUnsigned(
        byte[] bytes,
        int offset,
        long value,
        int width,
        bool littleEndian)
    {
        for (int index = 0; index < width; index++)
        {
            int destination = littleEndian ? offset + index : offset + width - index - 1;
            bytes[destination] = (byte)((ulong)value >> (index * 8));
        }
    }

    /// <summary>
    ///     Formats a generated value for a failure message; characters appear as <c>U+XXXX</c> code points.
    /// </summary>
    /// <param name="value">The value to format.</param>
    /// <returns>Invariant-culture text for the value.</returns>
    private static string FormatValue(object value)
    {
        return value switch
        {
            char character => "U+" + ((int)character).ToString("X4", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }

    /// <summary>
    ///     Loads the feature-operation matrix copied beside the test assembly; the test fails when it is missing.
    /// </summary>
    /// <returns>The parsed catalog, which the caller disposes.</returns>
    private static JsonDocument LoadCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "feature-operation-matrix.json");
        Assert.IsTrue(File.Exists(path), "The feature-operation matrix was not copied to the test output.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    /// <summary>Pairs a fixed primitive spelling with a conversion from random bits to one of its values.</summary>
    /// <param name="TypeName">The primitive spelling used in generated layouts.</param>
    /// <param name="CreateValue">
    ///     Converts 64 random bits to a representable value of the primitive's result type.
    /// </param>
    private sealed record PrimitiveSpec(string TypeName, Func<ulong, object> CreateValue);

    /// <summary>One generated field of a fixed-layout case.</summary>
    /// <param name="TypeName">The primitive spelling of the field.</param>
    /// <param name="Value">The value written to and expected from the field.</param>
    private sealed record PrimitiveField(string TypeName, object Value);

    /// <summary>A generated layout of fixed primitive fields and the options it is compiled with.</summary>
    /// <param name="LittleEndian">Whether the layout's default byte order is little-endian.</param>
    /// <param name="Aligned">Whether fields are placed at their natural alignment.</param>
    /// <param name="Fields">The fields in declaration order.</param>
    private sealed record FixedCase(bool LittleEndian, bool Aligned, IReadOnlyList<PrimitiveField> Fields);

    /// <summary>The values of one <c>child</c> record in a composite case.</summary>
    /// <param name="Tag">The <c>tag</c> byte.</param>
    /// <param name="Number">The <c>number</c> word.</param>
    private sealed record ChildValue(byte Tag, ushort Number);

    /// <summary>A generated composite layout case and the options it is compiled with.</summary>
    /// <param name="LittleEndian">Whether the layout's default byte order is little-endian.</param>
    /// <param name="Aligned">Whether fields are placed at their natural alignment.</param>
    /// <param name="Head">The <c>head</c> byte.</param>
    /// <param name="InlineCode">The <c>inlineValue.code</c> value.</param>
    /// <param name="Children">The <c>children</c> elements; the fixed array length equals their count.</param>
    /// <param name="Values">The runtime <c>values</c> elements; the variable <c>N</c> equals their count.</param>
    /// <param name="Label">The <c>label</c> text of at most five characters.</param>
    private sealed record CompositeCase(
        bool LittleEndian,
        bool Aligned,
        byte Head,
        uint InlineCode,
        IReadOnlyList<ChildValue> Children,
        IReadOnlyList<ushort> Values,
        string Label);

    /// <summary>A generated enum and bitfield case and the options it is compiled with.</summary>
    /// <param name="LittleEndian">Whether the layout's default byte order is little-endian.</param>
    /// <param name="Aligned">Whether fields are placed at their natural alignment.</param>
    /// <param name="Prefix">The <c>prefix</c> byte.</param>
    /// <param name="EnumValue">The raw <c>state</c> enum value, which may have no name.</param>
    /// <param name="Low">The 5-bit <c>low</c> bitfield value.</param>
    /// <param name="High">The 11-bit <c>high</c> bitfield value.</param>
    /// <param name="Tail">The <c>tail</c> byte.</param>
    private sealed record EnumBitfieldCase(
        bool LittleEndian,
        bool Aligned,
        byte Prefix,
        ushort EnumValue,
        byte Low,
        ushort High,
        byte Tail);

    /// <summary>A generated terminated-text case.</summary>
    /// <param name="LittleEndian">Whether the layout's default byte order is little-endian.</param>
    /// <param name="TypeName">The terminated-text type spelling.</param>
    /// <param name="Value">The text to write and expect back.</param>
    /// <param name="Tail">The byte stored after the text.</param>
    private sealed record StringCase(bool LittleEndian, string TypeName, string Value, byte Tail);

    /// <summary>A generated pointer storage case.</summary>
    /// <param name="Depth">The pointer level: 1 for a struct pointer, 2 for a pointer to a pointer.</param>
    /// <param name="PointerSize">The pointer width in bytes.</param>
    /// <param name="LittleEndian">Whether the layout's default byte order is little-endian.</param>
    /// <param name="TargetValue">The 16-bit value stored at the final target.</param>
    /// <param name="Tail">The byte stored after the root pointer.</param>
    private sealed record PointerCase(int Depth, byte PointerSize, bool LittleEndian, ushort TargetValue, byte Tail);
}
