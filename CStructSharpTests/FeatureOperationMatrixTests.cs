namespace CStructSharp.Tests;

using System.Buffers;
using System.Dynamic;
using System.Text.Json;
using CStructSharp.Structure;

/// <summary>Executes the supported feature rows recorded in the repository's cross-operation matrix.</summary>
[TestClass]
public class FeatureOperationMatrixTests
{
    /// <summary>
    ///     Prevents a primitive spelling from being added to the public codec dictionaries without being classified in
    ///     the canonical feature matrix.
    /// </summary>
    [TestMethod]
    public void Catalog_CoversEveryRegisteredPrimitiveSpelling()
    {
        using JsonDocument document = LoadCatalog();
        JsonElement spellings = document.RootElement.GetProperty("primitiveSpellings");
        string[] catalog =
        [
            .. spellings.GetProperty("fixed").EnumerateArray().Select(item => item.GetString()!),
            .. spellings.GetProperty("terminated").EnumerateArray().Select(item => item.GetString()!),
        ];

        var cstruct = new CStruct("struct root { byte value; };");
        CollectionAssert.AreEquivalent(
            cstruct.FieldHandlers.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            catalog.OrderBy(item => item, StringComparer.Ordinal).ToArray());
        CollectionAssert.AreEquivalent(
            cstruct.WriteHandlers.Keys.OrderBy(item => item, StringComparer.Ordinal).ToArray(),
            catalog.OrderBy(item => item, StringComparer.Ordinal).ToArray());
    }

    /// <summary>
    ///     Applies the length query to every named terminated handler, proves nonzero-position restoration, and keeps
    ///     ordinary scalar fields outside the array/string contract.
    /// </summary>
    [TestMethod]
    public void NamedStringLength_CoversEveryHandlerAndRejectsScalars()
    {
        using JsonDocument document = LoadCatalog();
        string[] terminatedTypes = document.RootElement.
            GetProperty("primitiveSpellings").
            GetProperty("terminated").
            EnumerateArray().
            Select(item => item.GetString()!).
            ToArray();

        foreach (string typeName in terminatedTypes)
        {
            bool isWide = typeName.StartsWith("unicode_", StringComparison.Ordinal) ||
                          typeName.StartsWith("string", StringComparison.Ordinal);
            bool isNewline = typeName.Contains("_newline", StringComparison.Ordinal);
            byte terminator = isNewline ? (byte)'\n' : (byte)0;
            bool storedLittleEndian = typeName.EndsWith('<') || !typeName.EndsWith('>');
            byte[] encodedTerminator = isWide
                                           ? storedLittleEndian
                                               ? [terminator, 0,]
                                               : [0, terminator,]
                                           : [terminator,];
            byte[] bytes = [0xA5, .. encodedTerminator,];
            var cstruct = new CStruct(
                $"struct root {{ {typeName} value; }};",
                pointerSize: 1,
                isLittleEndian: true);
            using var stream = new MemoryStream(bytes) { Position = 1, };

            Assert.AreEqual(0, cstruct.GetDynamicArrayLength(stream, "root.value"), typeName);
            Assert.AreEqual(1L, stream.Position, typeName + "/position");
        }

        var scalar = new CStruct("struct root { byte value; };", pointerSize: 1);
        using var scalarStream = new MemoryStream([0x2A,]);
        Assert.Throws<CStructPathException>(
            () => scalar.GetDynamicArrayLength(scalarStream, "root.value"));
        Assert.AreEqual(0L, scalarStream.Position);
    }

    /// <summary>
    ///     Exercises every fixed-width primitive spelling in both layout byte orders across parse, debug, address,
    ///     serialize, direct write, and update.
    /// </summary>
    [TestMethod]
    public void PrimitiveScalarCases_AgreeAcrossCoreOperations()
    {
        foreach ((string typeName, int width) in FixedPrimitiveCases())
        {
            foreach (bool layoutLittleEndian in RegressionTestSupport.Endianness)
            {
                string caseName = typeName + "/" + (layoutLittleEndian ? "little" : "big");
                bool storedLittleEndian = typeName.EndsWith('<') ||
                                          (!typeName.EndsWith('>') && layoutLittleEndian);
                byte[] originalValue = EncodeUnsigned(0x12, width, storedLittleEndian);
                byte[] replacementValue = EncodeUnsigned(0x34, width, storedLittleEndian);
                byte[] original = [.. originalValue, 0x7E,];
                byte[] replacement = [.. replacementValue, 0x7E,];
                var cstruct = new CStruct(
                    $"struct root {{ {typeName} value; byte tail; }};",
                    pointerSize: 1,
                    isLittleEndian: layoutLittleEndian);

                using var parseStream = new MemoryStream(original);
                ExpandoObject parsed = cstruct.ParseStream(parseStream, "root");
                Assert.AreEqual(0x12UL, Convert.ToUInt64(((dynamic)parsed).value), caseName + "/parse");
                Assert.AreEqual((byte)0x7E, (byte)((dynamic)parsed).tail, caseName + "/tail");

                ExpandoObject memoryParsed = cstruct.Parse(original.AsSpan(), "root");
                Assert.AreEqual(
                    JsonSerializer.Serialize(parsed),
                    JsonSerializer.Serialize(memoryParsed),
                    caseName + "/memory-parse");

                using var valueStream = new MemoryStream(original);
                Assert.AreEqual(
                    0x12UL,
                    Convert.ToUInt64(cstruct.ReadValue(valueStream, "root.value")),
                    caseName + "/read-value");
                Assert.AreEqual(
                    0x12UL,
                    Convert.ToUInt64(
                        cstruct.ReadValue((ReadOnlyMemory<byte>)original, "root.value")),
                    caseName + "/memory-read-value");

                using var debugStream = new MemoryStream(original);
                (List<DebugData> debug, dynamic debugWrapper) =
                    cstruct.ParseStreamWithDebug(debugStream, "root");
                ExpandoObject debugRoot = GetDebugRoot(debugWrapper);
                Assert.AreEqual(
                    JsonSerializer.Serialize(parsed),
                    JsonSerializer.Serialize(debugRoot),
                    caseName + "/debug-value");
                Assert.IsTrue(
                    debug.Any(item => item.CurPos == 0 && item.EndPos == width),
                    caseName + "/debug-range");

                using var addressStream = new MemoryStream(original);
                Assert.AreEqual(0L, cstruct.ResolveAddress(addressStream, "root.value"), caseName + "/address");

                CollectionAssert.AreEqual(original, cstruct.Serialize("root", parsed), caseName + "/serialize");
                byte[] spanOutput = new byte[original.Length];
                Assert.AreEqual(
                    original.Length,
                    cstruct.Serialize(spanOutput.AsSpan(), "root", parsed),
                    caseName + "/span-length");
                CollectionAssert.AreEqual(original, spanOutput, caseName + "/span-serialize");

                using var writeStream = new MemoryStream();
                cstruct.WriteStream(writeStream, "root", parsed);
                CollectionAssert.AreEqual(original, writeStream.ToArray(), caseName + "/write");

                using var updateStream = new MemoryStream((byte[])original.Clone());
                cstruct.UpdateStream(updateStream, "root.value", (byte)0x34);
                CollectionAssert.AreEqual(replacement, updateStream.ToArray(), caseName + "/update");
                Assert.AreEqual(0L, updateStream.Position, caseName + "/update-position");
            }
        }
    }

    /// <summary>
    ///     Runs representative arrays, composites, strings, aliases, pointers, bitfields, and placement rules through
    ///     every operation that applies to the selected case.
    /// </summary>
    [TestMethod]
    public void RepresentativeFeatureCases_AgreeAcrossCoreOperations()
    {
        foreach (MatrixCase item in RepresentativeCases())
        {
            IReadOnlyDictionary<string, int> variables =
                item.Variables ?? new Dictionary<string, int>();
            var cstruct = new CStruct(
                item.Layout,
                pointerSize: item.PointerSize,
                aligned: item.Aligned,
                isLittleEndian: item.LittleEndian);

            using var parseStream = new MemoryStream((byte[])item.Input.Clone());
            ExpandoObject parsed = cstruct.ParseStream(
                parseStream,
                "root",
                variables,
                new ReadOptions());
            ExpandoObject memoryParsed = cstruct.Parse(
                item.Input.AsSpan(),
                "root",
                variables,
                new ReadOptions());
            Assert.AreEqual(
                JsonSerializer.Serialize(parsed),
                JsonSerializer.Serialize(memoryParsed),
                item.Id + "/memory-parse");

            using var valueStream = new MemoryStream((byte[])item.Input.Clone());
            Assert.IsNotNull(
                cstruct.ReadValue(valueStream, item.UpdatePath, variables),
                item.Id + "/read-value");
            Assert.IsNotNull(
                cstruct.ReadValue(
                    (ReadOnlyMemory<byte>)item.Input,
                    item.UpdatePath,
                    variables),
                item.Id + "/memory-read-value");

            using var debugStream = new MemoryStream((byte[])item.Input.Clone());
            (List<DebugData> debug, dynamic debugWrapper) =
                cstruct.ParseStreamWithDebug(
                    debugStream,
                    "root",
                    variables,
                    new ReadOptions());
            ExpandoObject debugRoot = GetDebugRoot(debugWrapper);
            Assert.AreEqual(
                JsonSerializer.Serialize(parsed),
                JsonSerializer.Serialize(debugRoot),
                item.Id + "/debug-value");
            if (item.RequireDebugRange)
            {
                Assert.IsTrue(
                    debug.Any(entry => entry.CurPos <= item.UpdateAddress && entry.EndPos > item.UpdateAddress),
                    item.Id + "/debug-range");
            }

            using var addressStream = new MemoryStream((byte[])item.Input.Clone());
            Assert.AreEqual(
                item.UpdateAddress,
                cstruct.ResolveAddress(addressStream, item.UpdatePath, variables),
                item.Id + "/address");

            if (item.LengthPath is not null)
            {
                Assert.AreEqual(
                    item.ExpectedLength,
                    cstruct.GetDynamicArrayLength(addressStream, item.LengthPath, variables),
                    item.Id + "/length");
            }

            CollectionAssert.AreEqual(
                item.Serialized,
                cstruct.Serialize("root", parsed, variables),
                item.Id + "/serialize");
            byte[] spanOutput = new byte[item.Serialized.Length];
            Assert.AreEqual(
                item.Serialized.Length,
                cstruct.Serialize(spanOutput.AsSpan(), "root", parsed, variables),
                item.Id + "/span-length");
            CollectionAssert.AreEqual(item.Serialized, spanOutput, item.Id + "/span-serialize");
            var bufferOutput = new ArrayBufferWriter<byte>();
            Assert.AreEqual(
                (long)item.Serialized.Length,
                cstruct.Serialize(bufferOutput, "root", parsed, variables),
                item.Id + "/buffer-length");
            CollectionAssert.AreEqual(
                item.Serialized,
                bufferOutput.WrittenSpan.ToArray(),
                item.Id + "/buffer-serialize");

            using var writeStream = new MemoryStream();
            cstruct.WriteStream(writeStream, "root", parsed, variables);
            CollectionAssert.AreEqual(item.Serialized, writeStream.ToArray(), item.Id + "/write");

            using var updateStream = new MemoryStream((byte[])item.Input.Clone());
            cstruct.UpdateStream(updateStream, item.UpdatePath, item.Replacement, variables);
            CollectionAssert.AreEqual(item.Updated, updateStream.ToArray(), item.Id + "/update");
            Assert.AreEqual(0L, updateStream.Position, item.Id + "/update-position");
        }
    }

    /// <summary>
    ///     Verifies lossless raw union round-trip and explicit selected-member writes across all core operations.
    /// </summary>
    [TestMethod]
    public void ExplicitUnionCase_PreservesRawStorageAndWriteSelection()
    {
        const string layout = """
                              union choice { uint8 narrow; uint16 wide; };
                              struct root { byte head; choice value; byte tail; };
                              """;
        byte[] original = [0x7E, 0x34, 0x12, 0x6A,];
        var cstruct = new CStruct(layout, pointerSize: 1);

        using var parseStream = new MemoryStream(original);
        dynamic parsed = cstruct.ParseStream(parseStream, "root");
        Assert.IsInstanceOfType<UnionValue>(parsed.value);
        Assert.IsFalse(((UnionValue)parsed.value).HasSelection);
        Assert.AreEqual((ushort)0x1234, (ushort)parsed.value.wide);
        Assert.AreEqual((byte)0x34, (byte)parsed.value.narrow);

        using var valueStream = new MemoryStream(original);
        UnionValue selected = cstruct.ReadValue<UnionValue>(valueStream, "root.value");
        Assert.AreEqual((ushort)0x1234, (ushort)selected["wide"]!);
        UnionValue memorySelected =
            cstruct.ReadValue<UnionValue>((ReadOnlyMemory<byte>)original, "root.value");
        Assert.AreEqual((ushort)0x1234, (ushort)memorySelected["wide"]!);

        using var debugStream = new MemoryStream(original);
        (List<DebugData> debug, dynamic debugWrapper) =
            cstruct.ParseStreamWithDebug(debugStream, "root");
        dynamic debugRoot = GetDebugRoot(debugWrapper);
        Assert.AreEqual((ushort)0x1234, (ushort)debugRoot.value.wide);
        Assert.IsTrue(debug.Any(item => item.CurPos == 1 && item.EndPos == 3));

        using var addressStream = new MemoryStream(original);
        Assert.AreEqual(1L, cstruct.ResolveAddress(addressStream, "root.value.wide"));
        Assert.AreEqual(1L, cstruct.ResolveAddress(addressStream, "root.value.narrow"));

        UnionValue selectedUnion = UnionValue.FromMember("choice", "narrow", (byte)0xA5);
        dynamic writableRoot = new ExpandoObject();
        writableRoot.head = (byte)0x7E;
        writableRoot.value = selectedUnion;
        writableRoot.tail = (byte)0x6A;
        byte[] expected = [0x7E, 0xA5, 0x00, 0x6A,];

        CollectionAssert.AreEqual(expected, cstruct.Serialize("root", writableRoot));
        byte[] spanOutput = new byte[expected.Length];
        Assert.AreEqual(
            expected.Length,
            cstruct.Serialize(spanOutput.AsSpan(), "root", (object)writableRoot));
        CollectionAssert.AreEqual(expected, spanOutput);
        var bufferOutput = new ArrayBufferWriter<byte>();
        Assert.AreEqual(
            (long)expected.Length,
            cstruct.Serialize(bufferOutput, "root", (object)writableRoot));
        CollectionAssert.AreEqual(expected, bufferOutput.WrittenSpan.ToArray());
        using var writeStream = new MemoryStream();
        cstruct.WriteStream(writeStream, "root", writableRoot);
        CollectionAssert.AreEqual(expected, writeStream.ToArray());

        using var updateStream = new MemoryStream((byte[])original.Clone());
        cstruct.UpdateStream(updateStream, "root.value", selectedUnion);
        CollectionAssert.AreEqual(expected, updateStream.ToArray());
        Assert.AreEqual(0L, updateStream.Position);

        CollectionAssert.AreEqual(
            original,
            cstruct.Serialize("root", parsed),
            "An unchanged parsed union must emit its complete raw storage.");
        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "choice",
                new Dictionary<string, object?> { ["narrow"] = (byte)0xA5, }));
    }

    private static IEnumerable<(string TypeName, int Width)> FixedPrimitiveCases()
    {
        yield return ("byte", 1);
        yield return ("int8", 1);
        yield return ("uint8", 1);
        yield return ("char", 1);
        yield return ("wchar", 2);
        yield return ("wchar>", 2);
        yield return ("wchar<", 2);

        foreach (string prefix in new[] { "int16", "uint16", })
        {
            yield return (prefix, 2);
            yield return (prefix + ">", 2);
            yield return (prefix + "<", 2);
        }

        foreach (string prefix in new[] { "int32", "uint32", })
        {
            yield return (prefix, 4);
            yield return (prefix + ">", 4);
            yield return (prefix + "<", 4);
        }

        foreach (string prefix in new[] { "int64", "uint64", })
        {
            yield return (prefix, 8);
            yield return (prefix + ">", 8);
            yield return (prefix + "<", 8);
        }

        yield return ("short", 2);
        yield return ("ushort", 2);
        yield return ("int", 4);
        yield return ("uint", 4);
        yield return ("long", 8);
        yield return ("ulong", 8);
    }

    private static IEnumerable<MatrixCase> RepresentativeCases()
    {
        yield return new MatrixCase(
            "fixed-character-buffer",
            "struct root { char value[2]; byte tail; };",
            [0x41, 0x42, 0x7E,],
            [0x41, 0x42, 0x7E,],
            "root.value[1]",
            'Z',
            1,
            [0x41, 0x5A, 0x7E,],
            "root.value",
            2);
        yield return new MatrixCase(
            "terminated-string",
            "struct root { utf8_string_zero value; byte tail; };",
            [0x41, 0x00, 0x7E,],
            [0x41, 0x00, 0x7E,],
            "root.value",
            "B",
            0,
            [0x42, 0x00, 0x7E,],
            "root.value",
            1);
        yield return new MatrixCase(
            "enum",
            "enum mode : uint8 { One=1, Two=2 }; struct root { mode value; byte tail; };",
            [0x01, 0x7E,],
            [0x01, 0x7E,],
            "root.value",
            "Two",
            0,
            [0x02, 0x7E,]);
        yield return new MatrixCase(
            "bitfield",
            "struct root { uint8 low:4; uint8 high:4; byte tail; };",
            [0xA5, 0x7E,],
            [0xA5, 0x7E,],
            "root.high",
            (byte)3,
            0,
            [0x35, 0x7E,]);
        yield return new MatrixCase(
            "fixed-array",
            "struct root { uint8 values[2]; byte tail; };",
            [0x11, 0x22, 0x7E,],
            [0x11, 0x22, 0x7E,],
            "root.values[1]",
            (byte)0x33,
            1,
            [0x11, 0x33, 0x7E,],
            "root.values",
            2);
        yield return new MatrixCase(
            "runtime-array",
            "struct root { uint8 values[N]; byte tail; };",
            [0x11, 0x22, 0x7E,],
            [0x11, 0x22, 0x7E,],
            "root.values[1]",
            (byte)0x33,
            1,
            [0x11, 0x33, 0x7E,],
            "root.values",
            2,
            Variables: new Dictionary<string, int> { ["N"] = 2, });
        yield return new MatrixCase(
            "nested-struct",
            "struct child { uint8 value; }; struct root { child item; byte tail; };",
            [0x11, 0x7E,],
            [0x11, 0x7E,],
            "root.item.value",
            (byte)0x33,
            0,
            [0x33, 0x7E,]);
        yield return new MatrixCase(
            "inline-struct",
            "struct root { struct { uint8 value; } item; byte tail; };",
            [0x11, 0x7E,],
            [0x11, 0x7E,],
            "root.item.value",
            (byte)0x33,
            0,
            [0x33, 0x7E,]);
        yield return new MatrixCase(
            "typedef",
            "typedef uint16 word; struct root { word value; byte tail; };",
            [0x34, 0x12, 0x7E,],
            [0x34, 0x12, 0x7E,],
            "root.value",
            (ushort)0x5678,
            0,
            [0x78, 0x56, 0x7E,]);
        yield return new MatrixCase(
            "pointer",
            "struct child { uint8 value; }; struct root { child *ptr; byte tail; };",
            [0x03, 0x7E, 0x00, 0x11,],
            [0x03, 0x7E,],
            "root.ptr.value.value",
            (byte)0x33,
            3,
            [0x03, 0x7E, 0x00, 0x33,]);
        yield return new MatrixCase(
            "multi-pointer",
            "struct root { uint8 **ptr; byte tail; };",
            [0x02, 0x7E, 0x04, 0x00, 0x11,],
            [0x02, 0x7E,],
            "root.ptr.value.value",
            (byte)0x33,
            4,
            [0x02, 0x7E, 0x04, 0x00, 0x33,],
            RequireDebugRange: false);
        yield return new MatrixCase(
            "aligned-composite",
            "struct root { uint8 head; uint16 value; };",
            [0x11, 0x00, 0x34, 0x12,],
            [0x11, 0x00, 0x34, 0x12,],
            "root.value",
            (ushort)0x5678,
            2,
            [0x11, 0x00, 0x78, 0x56,],
            Aligned: true);
        yield return new MatrixCase(
            "explicit-endian",
            "struct root { uint16> big; uint16< little; };",
            [0x12, 0x34, 0x78, 0x56,],
            [0x12, 0x34, 0x78, 0x56,],
            "root.little",
            (ushort)0x9ABC,
            2,
            [0x12, 0x34, 0xBC, 0x9A,]);
    }

    private static byte[] EncodeUnsigned(ulong value, int width, bool littleEndian)
    {
        byte[] result = new byte[width];
        for (int index = 0; index < width; index++)
        {
            int destination = littleEndian ? index : width - index - 1;
            result[destination] = (byte)(value >> (index * 8));
        }

        return result;
    }

    private static ExpandoObject GetDebugRoot(ExpandoObject wrapper)
    {
        var values = (IDictionary<string, object?>)wrapper;
        Assert.IsTrue(values.TryGetValue("root", out object? root));
        Assert.IsInstanceOfType<ExpandoObject>(root);
        return (ExpandoObject)root;
    }

    private static JsonDocument LoadCatalog()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "feature-operation-matrix.json");
        Assert.IsTrue(File.Exists(path), "The feature-operation matrix was not copied to the test output.");
        return JsonDocument.Parse(File.ReadAllText(path));
    }

    private sealed record MatrixCase(
        string Id,
        string Layout,
        byte[] Input,
        byte[] Serialized,
        string UpdatePath,
        object Replacement,
        long UpdateAddress,
        byte[] Updated,
        string? LengthPath = null,
        int? ExpectedLength = null,
        IReadOnlyDictionary<string, int>? Variables = null,
        byte PointerSize = 1,
        bool Aligned = false,
        bool LittleEndian = true,
        bool RequireDebugRange = true);
}
