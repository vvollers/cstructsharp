namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.IO;
using CStructSharp.Structure;
using Pidgin;

/// <summary>Exercises layout-calculation and resource-limit cases that are easy to miss in ordinary format fixtures.</summary>
[TestClass]
public class LayoutSafetyTests
{
    /// <summary>Rejects a misspelled field type immediately instead of retrying alignment resolution forever.</summary>
    [TestMethod]
    public void Constructor_RejectsUnknownFieldType()
    {
        // The constructor compiles all declarations, so a typo must be visible before a stream is touched.
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { missing value; };"));
    }

    /// <summary>
    ///     Gives every invalid declaration a single public exception type, whether the problem is parser syntax or a
    ///     conflicting top-level name. This lets callers reject untrusted layout text without knowing parser internals.
    /// </summary>
    [TestMethod]
    public void Constructor_UsesLayoutExceptionForSyntaxAndDuplicateTopLevelNames()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { byte value; "));
        Assert.Throws<CStructLayoutException>(
                                              () => new CStruct(
                                                  "struct root { byte first; }; struct root { byte second; };"));
    }

    /// <summary>Rejects an impossible by-value recursive layout while preserving the legal self-pointer form.</summary>
    [TestMethod]
    public void Constructor_DistinguishesByValueRecursionFromSelfPointer()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct node { node next; };"));

        var pointerLayout = new CStruct("struct node { node *next; };");
        Assert.AreEqual(8, pointerLayout.GetStructSizeInBytes("node"));
    }

    /// <summary>Applies the caller-selected definition length and nesting limits before invoking the layout parser.</summary>
    [TestMethod]
    public void Constructor_EnforcesCompilationInputLimits()
    {
        Assert.Throws<CStructLayoutException>(
                                              () => new CStruct(
                                                  "struct root { byte value; };",
                                                  compilationOptions: new CStructCompilationOptions
                                                  {
                                                      MaxDefinitionLength = 8,
                                                  }));
        Assert.Throws<CStructLayoutException>(
                                              () => new CStruct(
                                                  "{{",
                                                  compilationOptions: new CStructCompilationOptions
                                                  {
                                                      MaxLayoutNestingDepth = 1,
                                                  }));
    }

    /// <summary>Uses the nested declaration's complete footprint rather than only its alignment in packed and aligned layouts.</summary>
    [TestMethod]
    public void GetStructSizeInBytes_UsesNestedStructStorageSize()
    {
        const string packed = "struct inner { byte a; byte b; }; struct outer { inner item; byte tail; };";
        const string aligned = "struct inner { byte a; uint16 b; }; struct outer { byte prefix; inner item; byte tail; };";

        Assert.AreEqual(3, new CStruct(packed).GetStructSizeInBytes("outer"));
        Assert.AreEqual(8, new CStruct(aligned, aligned: true).GetStructSizeInBytes("outer"));
    }

    /// <summary>Keeps tail padding in the stride between aligned nested-struct array elements during parsing.</summary>
    [TestMethod]
    public void ParseStream_UsesCompleteNestedStructArrayStride()
    {
        const string layout = "struct inner { uint64 a; uint32 b; }; struct root { inner items[2]; byte tail; };";
        byte[] bytes =
        [
            1, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0,
            3, 0, 0, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, 0, 0,
            0xA5,
        ];
        using var stream = new MemoryStream(bytes);
        var cstruct = new CStruct(layout, aligned: true);

        dynamic result = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(3UL, (ulong)result.items[1].a);
        Assert.AreEqual(4U, (uint)result.items[1].b);
        Assert.AreEqual((byte)0xA5, (byte)result.tail);
    }

    /// <summary>Does not add a full extra alignment unit when a union's largest array member is already aligned.</summary>
    [TestMethod]
    public void UnionArraySizeAndFollowingFieldOffset_AreExact()
    {
        const string layout = "union choice { uint32 words[2]; byte flag; }; struct root { choice value; byte tail; };";
        using var stream = new MemoryStream([1, 0, 0, 0, 2, 0, 0, 0, 0xA5,]);
        var cstruct = new CStruct(layout);

        dynamic result = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(8, cstruct.GetStructSizeInBytes("choice"));
        Assert.AreEqual(9, cstruct.GetStructSizeInBytes("root"));
        Assert.AreEqual((byte)0xA5, (byte)result.tail);
    }

    /// <summary>
    ///     Materializes the unused portion of a selected union member during serialization, so serializing a union by
    ///     itself produces the full declared storage rather than only the selected member's bytes.
    /// </summary>
    [TestMethod]
    public void Serialize_ReservesCompleteUnionStorageForShortSelectedMember()
    {
        const string layout = "union choice { uint32 wide; byte small; };";
        UnionValue value = UnionValue.FromMember("choice", "small", (byte)0xA5);
        var cstruct = new CStruct(layout);

        byte[] bytes = cstruct.Serialize("choice", value);

        CollectionAssert.AreEqual(new byte[] { 0xA5, 0, 0, 0, }, bytes);
        Assert.AreEqual(cstruct.GetStructSizeInBytes("choice"), bytes.Length);
    }

    /// <summary>Enforces the caller's limit before a data-controlled array can allocate or loop excessively.</summary>
    [TestMethod]
    public void ParseStream_EnforcesArrayElementLimit()
    {
        const string layout = "struct root { byte count; byte values[count]; };";
        using var stream = new MemoryStream([3, 1, 2, 3,]);
        var cstruct = new CStruct(layout);

        Assert.Throws<CStructReadException>(
                                             () => cstruct.ParseStream(
                                                 stream,
                                                 "root",
                                                 new Dictionary<string, Expr>(),
                                                 new ReadOptions { MaxArrayElements = 2, }));
    }

    /// <summary>Rejects a negative data-controlled array length before it can be used as a loop or allocation bound.</summary>
    [TestMethod]
    public void ParseStream_RejectsNegativeArrayElementCount()
    {
        const string layout = "struct root { int8 count; byte values[count]; };";
        using var stream = new MemoryStream([0xFF,]);
        var cstruct = new CStruct(layout);

        Assert.Throws<CStructReadException>(() => cstruct.ParseStream(stream, "root"));
    }

    /// <summary>
    ///     Treats a struct with a data-controlled array as a variable-size pointer target when a fixed target-size
    ///     policy is enabled, instead of leaking an internal layout-calculation exception.
    /// </summary>
    [TestMethod]
    public void ParseStream_RejectsVariableSizePointerTargetsWhenFixedLimitIsEnabled()
    {
        const string layout = "struct target { byte count; byte values[count]; }; struct root { target *item; };";
        using var stream = new MemoryStream([0x02, 0x00, 0x01, 0xA5,]);
        var cstruct = new CStruct(layout, 2);

        Assert.Throws<CStructReadException>(
                                             () => cstruct.ParseStream(
                                                 stream,
                                                 "root",
                                                 new Dictionary<string, Expr>(),
                                                 new ReadOptions { MaxPointerTargetBytes = 64, }));
    }

    /// <summary>Stops an unterminated or oversized C string at the configured per-field encoded-byte budget.</summary>
    [TestMethod]
    public void ParseStream_EnforcesStringByteLimit()
    {
        const string layout = "struct root { char name[]; };";
        using var stream = new MemoryStream([(byte)'a', (byte)'b', (byte)'c', 0,]);
        var cstruct = new CStruct(layout);

        Assert.Throws<CStructReadException>(
                                             () => cstruct.ParseStream(
                                                 stream,
                                                 "root",
                                                 new Dictionary<string, Expr>(),
                                                 new ReadOptions { MaxStringBytes = 3, }));
    }

    /// <summary>Counts every physical read, including primitive reads, against the operation-wide byte budget.</summary>
    [TestMethod]
    public void ParseStream_EnforcesTotalReadByteLimit()
    {
        const string layout = "struct root { uint32 value; };";
        using var stream = new MemoryStream([1, 2, 3, 4,]);
        var cstruct = new CStruct(layout);

        Assert.Throws<CStructReadException>(
                                             () => cstruct.ParseStream(
                                                 stream,
                                                 "root",
                                                 new Dictionary<string, Expr>(),
                                                 new ReadOptions { MaxTotalBytesRead = 3, }));
    }

    /// <summary>Applies nesting limits to nested declared structs, not only to the root declaration.</summary>
    [TestMethod]
    public void ParseStream_EnforcesNestedStructLimit()
    {
        const string layout = "struct c { byte value; }; struct b { c child; }; struct a { b child; };";
        using var stream = new MemoryStream([0x2A,]);
        var cstruct = new CStruct(layout);

        Assert.Throws<CStructReadException>(
                                             () => cstruct.ParseStream(
                                                 stream,
                                                 "a",
                                                 new Dictionary<string, Expr>(),
                                                 new ReadOptions { MaxNestingDepth = 2, }));
    }

    /// <summary>Uses documented C-like grouping for mixed arithmetic and bitwise layout expressions.</summary>
    [TestMethod]
    public void Expressions_UseSharedPrecedenceRows()
    {
        Assert.AreEqual(4, CStructDefinitionParser.Expr.ParseOrThrow("6 * 2 / 3").Calc());
        Assert.AreEqual(7, CStructDefinitionParser.Expr.ParseOrThrow("1 + 2 * 3").Calc());
        Assert.AreEqual(4, CStructDefinitionParser.Expr.ParseOrThrow("1 << 1 + 1").Calc());
        Assert.AreEqual(4, CStructDefinitionParser.Expr.ParseOrThrow("8 >> 1 & 7").Calc());
    }

    /// <summary>Makes the hex parser reject punctuation and incomplete bytes instead of silently discarding input.</summary>
    [TestMethod]
    public void ParseHexDataContent_IsStrictByDefault()
    {
        CollectionAssert.AreEqual(new byte[] { 0x0A, 0xFF, }, "0A FF".ParseHexDataContent());
        Assert.Throws<FormatException>(() => "0AF".ParseHexDataContent());
        Assert.Throws<FormatException>(() => "0A,FF".ParseHexDataContent());
    }

    /// <summary>Checks a representative fixed-size layout over many values rather than relying on one hand-picked fixture.</summary>
    [TestMethod]
    public void SerializeThenParse_RoundTripsRepresentativeFixedValues()
    {
        const string layout = "struct root { byte a; uint16 b; uint32 c; };";
        var random = new System.Random(20260724);
        var cstruct = new CStruct(layout);

        for (int i = 0; i < 128; i++)
        {
            byte a = (byte)random.Next(byte.MaxValue + 1);
            ushort b = (ushort)random.Next(ushort.MaxValue + 1);
            uint c = ((uint)random.Next() << 1) | (uint)random.Next(2);
            dynamic data = new System.Dynamic.ExpandoObject();
            data.a = a;
            data.b = b;
            data.c = c;

            byte[] bytes = cstruct.Serialize("root", data);
            using var stream = new MemoryStream(bytes);
            dynamic parsed = cstruct.ParseStream(stream, "root");

            Assert.AreEqual(a, (byte)parsed.a);
            Assert.AreEqual(b, (ushort)parsed.b);
            Assert.AreEqual(c, (uint)parsed.c);
        }
    }

    /// <summary>
    ///     Generates many small, fixed-size declarations and values to prove that serialization, parsing, alignment,
    ///     and public size reporting agree across combinations rather than only across hand-written examples.
    /// </summary>
    [TestMethod]
    public void SerializeThenParse_RoundTripsGeneratedFixedLayouts()
    {
        string[] typeNames = ["byte", "uint16", "uint32", "uint64",];
        var random = new System.Random(20260725);

        for (int layoutNumber = 0; layoutNumber < 96; layoutNumber++)
        {
            int fieldCount = random.Next(1, 7);
            var declarations = new List<string>(fieldCount);
            var fieldTypes = new Dictionary<string, string>(StringComparer.Ordinal);
            IDictionary<string, object?> values = new System.Dynamic.ExpandoObject();

            for (int fieldNumber = 0; fieldNumber < fieldCount; fieldNumber++)
            {
                string typeName = typeNames[random.Next(typeNames.Length)];
                string fieldName = "field" + fieldNumber;
                declarations.Add(typeName + " " + fieldName + ";");
                values.Add(fieldName, CreateRandomPrimitiveValue(typeName, random));
                fieldTypes.Add(fieldName, typeName);
            }

            var cstruct = new CStruct("struct root { " + string.Join(' ', declarations) + " };", aligned: layoutNumber % 2 == 0);
            byte[] bytes = cstruct.Serialize("root", values);
            using var stream = new MemoryStream(bytes);
            IDictionary<string, object?> parsed = cstruct.ParseStream(stream, "root");

            Assert.AreEqual(cstruct.GetStructSizeInBytes("root"), bytes.Length, "Layout " + layoutNumber);
            foreach (KeyValuePair<string, object?> expected in values)
            {
                Assert.AreEqual(
                                expected.Value,
                                parsed[expected.Key],
                                "Layout " + layoutNumber + ", field " + expected.Key + " (" + fieldTypes[expected.Key] + ")");
            }
        }
    }

    /// <summary>
    ///     Exercises a bounded variable-length layout with deterministic random input. Each trial must either produce
    ///     a regular object or stop through the documented read exception; hostile bytes must not escape as unrelated
    ///     runtime failures or grow work beyond the configured limits.
    /// </summary>
    [TestMethod]
    public void ParseStream_BoundedBinaryFuzz_UsesOnlyDocumentedReadFailures()
    {
        const string layout = "struct root { byte count; byte values[count]; char name[]; };";
        var cstruct = new CStruct(layout);
        var random = new System.Random(20260726);
        var options = new ReadOptions
        {
            MaxArrayElements = 8,
            MaxStringBytes = 8,
            MaxTotalBytesRead = 32,
            MaxNestingDepth = 8,
        };
        int successfulParses = 0;
        int documentedFailures = 0;

        for (int trial = 0; trial < 256; trial++)
        {
            byte[] bytes = new byte[random.Next(0, 33)];
            random.NextBytes(bytes);
            if (bytes.Length > 0 && trial % 3 == 0)
            {
                // Seed terminators into part of the corpus so successful variable-length parses are exercised too.
                bytes[^1] = 0;
            }

            using var stream = new MemoryStream(bytes);
            try
            {
                Assert.IsNotNull(cstruct.ParseStream(stream, "root", new Dictionary<string, Expr>(), options));
                successfulParses++;
            }
            catch (CStructReadException)
            {
                // Expected for truncated data or a count/string that crosses one of the explicit budgets.
                documentedFailures++;
            }
        }

        Assert.IsGreaterThan(0, successfulParses, "The corpus must continue exercising the successful parse branch.");
        Assert.IsGreaterThan(0, documentedFailures, "The corpus must continue exercising bounded failure handling.");
    }

    /// <summary>Rejects streams that would otherwise fail later when writer alignment queries their unavailable position.</summary>
    [TestMethod]
    public void WriteStream_RequiresSeekableStream()
    {
        const string layout = "struct root { byte value; };";
        var cstruct = new CStruct(layout);
        using var stream = new WriteOnlyNonSeekableStream();

        Assert.Throws<ArgumentException>(() => cstruct.WriteStream(stream, "root", new { value = (byte)0xA5, }));
    }

    /// <summary>Retries a short bitfield-storage read so an update preserves bits outside the selected field.</summary>
    /// <param name="path">The first or later bitfield selected for update.</param>
    /// <param name="value">The replacement value for the selected slice.</param>
    /// <param name="expectedFirst">The expected first storage byte after the update.</param>
    /// <param name="expectedSecond">The expected second storage byte after the update.</param>
    [TestMethod]
    [DataRow("root.low", 0x3, (byte)0xA3, (byte)0xBC)]
    [DataRow("root.high", 0x123, (byte)0x35, (byte)0x12)]
    public void UpdateStream_RetriesShortBitfieldStorageReads(
        string path,
        int value,
        byte expectedFirst,
        byte expectedSecond)
    {
        const string layout = "struct root { uint16 low:4; uint16 high:12; };";
        using ChunkedMemoryStream stream = RegressionTestSupport.CreateChunkedStream(
            [0xA5, 0xBC,],
            1,
            writable: true);
        var cstruct = new CStruct(layout);

        cstruct.UpdateStream(stream, path, value);

        CollectionAssert.AreEqual(new byte[] { expectedFirst, expectedSecond, }, stream.ToArray());
    }

    /// <summary>
    ///     Refuses to extend a truncated bitfield unit during an in-place update, because the missing byte could contain
    ///     neighbouring bits that an update operation is required to preserve.
    /// </summary>
    [TestMethod]
    public void UpdateStream_RejectsTruncatedExistingBitfieldStorage()
    {
        const string layout = "struct root { uint16 low:4; uint16 high:12; };";
        using var stream = new MemoryStream([0xA5,]);
        var cstruct = new CStruct(layout);

        Assert.Throws<CStructReadException>(() => cstruct.UpdateStream(stream, "root.low", 0x3));
        CollectionAssert.AreEqual(new byte[] { 0xA5, }, stream.ToArray());
    }

    /// <summary>Produces a value that is exactly representable by the generated primitive declaration.</summary>
    private static object CreateRandomPrimitiveValue(string typeName, System.Random random)
    {
        return typeName switch
        {
            "byte" => (object)(byte)random.Next(byte.MaxValue + 1),
            "uint16" => (object)(ushort)random.Next(ushort.MaxValue + 1),
            "uint32" => (object)(((uint)random.Next() << 1) | (uint)random.Next(2)),
            "uint64" => (object)(((ulong)(uint)random.Next() << 33) |
                                    ((ulong)(uint)random.Next() << 2) |
                                    (uint)random.Next(4)),
            _ => throw new ArgumentOutOfRangeException(nameof(typeName), typeName, "Unsupported generated primitive type."),
        };
    }

    /// <summary>Models a valid write-only transport whose lack of positioning makes layout-aware writing unsupported.</summary>
    private sealed class WriteOnlyNonSeekableStream : Stream
    {
        public override bool CanRead => false;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }
}
