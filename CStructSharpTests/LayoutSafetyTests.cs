namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.IO;
using CStructSharp.Structure;
using Pidgin;

/// <summary>Exercises layout-calculation and resource-limit cases that are easy to miss in ordinary format fixtures.</summary>
[TestClass]
public class LayoutSafetyTests
{
    /// <summary>
    ///     missing is used as a field type without being declared.
    /// </summary>
    /// <remarks>
    ///     Construction must immediately raise a layout error. This catches a typo before reading data and avoids an
    ///     endless attempt to calculate the alignment of a type that does not exist.
    /// </remarks>
    [TestMethod]
    public void Constructor_RejectsUnknownFieldType()
    {
        // The constructor compiles all declarations, so a typo must be visible before a stream is touched.
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { missing value; };"));
    }

    /// <summary>
    ///     One input omits closing syntax and another declares root twice.
    /// </summary>
    /// <remarks>
    ///     Both must raise CStructLayoutException even though the causes differ. A caller validating layout text can
    ///     therefore handle malformed grammar and conflicting names through the same public error category.
    /// </remarks>
    [TestMethod]
    public void Constructor_UsesLayoutExceptionForSyntaxAndDuplicateTopLevelNames()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { byte value; "));
        Assert.Throws<CStructLayoutException>(
                                              () => new CStruct(
                                                  "struct root { byte first; }; struct root { byte second; };"));
    }

    /// <summary>
    ///     node containing another complete node would require infinite inline storage and must be rejected. node
    ///     containing node* is valid because only the pointer address is stored inline.
    /// </summary>
    /// <remarks>
    ///     With the default pointer width, that record has a finite size of eight bytes.
    /// </remarks>
    [TestMethod]
    public void Constructor_DistinguishesByValueRecursionFromSelfPointer()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct node { node next; };"));

        var pointerLayout = new CStruct("struct node { node *next; };");
        Assert.AreEqual(8, pointerLayout.GetStructSizeInBytes("node"));
    }

    /// <summary>
    ///     The test supplies a definition-length limit smaller than the text and nesting limits smaller than the
    ///     declarations require.
    /// </summary>
    /// <remarks>
    ///     Construction must reject these inputs before expensive parsing proceeds. These limits bound layout
    ///     processing separately from budgets used later to read binary data.
    /// </remarks>
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

    /// <summary>
    ///     A child record contributes its whole size, not merely its alignment requirement.
    /// </summary>
    /// <remarks>
    ///     The packed example needs three bytes; the aligned example needs eight after child placement and final
    ///     padding. Reporting the correct size is essential when allocating buffers or repeating records in an array.
    /// </remarks>
    [TestMethod]
    public void GetStructSizeInBytes_UsesNestedStructStorageSize()
    {
        const string packed = "struct inner { byte a; byte b; }; struct outer { inner item; byte tail; };";
        const string aligned = "struct inner { byte a; uint16 b; }; struct outer { byte prefix; inner item; byte tail; };";

        Assert.AreEqual(3, new CStruct(packed).GetStructSizeInBytes("outer"));
        Assert.AreEqual(8, new CStruct(aligned, aligned: true).GetStructSizeInBytes("outer"));
    }

    /// <summary>
    ///     inner has a uint64 and a uint32, then tail padding rounds its aligned size to 16 bytes.
    /// </summary>
    /// <remarks>
    ///     The second item must therefore read values 3 and 4 from offset 16, and the outer tail must remain 0xA5. A
    ///     12-byte stride would read padding as data.
    /// </remarks>
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

    /// <summary>
    ///     words[2] occupies eight bytes and already meets the union's alignment requirement.
    /// </summary>
    /// <remarks>
    ///     The union must stay eight bytes, making the packed root nine bytes with tail = 0xA5. Rounding must not add a
    ///     whole extra alignment unit when no padding is needed.
    /// </remarks>
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
    ///     The uint32 member makes choice four bytes wide even when small is selected.
    /// </summary>
    /// <remarks>
    ///     Writing small = 0xA5 must produce A5 00 00 00. The unused bytes are part of the union's storage and cannot
    ///     disappear simply because the selected member is shorter.
    /// </remarks>
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

    /// <summary>
    ///     variable_inner has no fixed size of its own, because its trailing array's length depends on a sibling
    ///     field rather than a compile-time constant. A union member of that type has no fixed storage either.
    /// </summary>
    /// <remarks>
    ///     Validating this must call into the same size-query path used everywhere else in this file, but from
    ///     inside the constructor itself, before the compiled model it would ordinarily read from exists. This
    ///     resolves through the union member's own composite lookup, not just its array-count expression, so it
    ///     specifically exercises composite-size resolution occurring mid-construction.
    /// </remarks>
    [TestMethod]
    public void Constructor_UnionMemberIsAVariablySizedNestedStruct_ThrowsLayoutException()
    {
        const string layout = """
                              struct variable_inner { uint8 len; char data[len]; };
                              union choice { uint16 fixed_member; variable_inner var_member; };
                              """;

        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(() => new CStruct(layout));

        StringAssert.Contains(exception.Message, "var_member");
    }

    /// <summary>
    ///     The first byte requests three elements in values[count].
    /// </summary>
    /// <remarks>
    ///     A lower configured element limit must stop the read before processing that array. The count comes from
    ///     binary data, so validating only the layout text would not prevent excessive work.
    /// </remarks>
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

    /// <summary>
    ///     int8 interprets FF as -1.
    /// </summary>
    /// <remarks>
    ///     Using that count for values[count] must raise a read error because an array cannot have negative length. The
    ///     reader must not convert it to a huge unsigned count or quietly treat it as empty.
    /// </remarks>
    [TestMethod]
    public void ParseStream_RejectsNegativeArrayElementCount()
    {
        const string layout = "struct root { int8 count; byte values[count]; };";
        using var stream = new MemoryStream([0xFF,]);
        var cstruct = new CStruct(layout);

        Assert.Throws<CStructReadException>(() => cstruct.ParseStream(stream, "root"));
    }

    /// <summary>
    ///     The pointer targets a record whose size depends on its count field.
    /// </summary>
    /// <remarks>
    ///     A policy requiring a known fixed target size cannot validate that record in advance. The read must fail
    ///     through the documented read exception instead of leaking an internal size-calculation failure.
    /// </remarks>
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

    /// <summary>
    ///     name[] contains abc followed by zero, but the configured per-string budget is too small for the encoded
    ///     storage.
    /// </summary>
    /// <remarks>
    ///     Reading must stop with an error. The budget includes the terminator, so a short-looking returned string can
    ///     still require more bytes than its character count.
    /// </remarks>
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

    /// <summary>
    ///     A single uint32 needs four physical bytes.
    /// </summary>
    /// <remarks>
    ///     Setting MaxTotalBytesRead to 3 must reject this plain parse even though all four input bytes exist. The
    ///     budget limits reading activity, rather than changing the width of the declared field or indicating that the
    ///     file is truncated.
    /// </remarks>
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

    /// <summary>
    ///     a contains b, which contains c, although the final value uses only one byte.
    /// </summary>
    /// <remarks>
    ///     A shallow nesting budget must reject this chain. Byte count alone cannot bound recursive structure
    ///     processing, so nesting has a separate limit.
    /// </remarks>
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

    /// <summary>
    ///     The formulas check interactions among arithmetic, shifts, and bitwise AND.
    /// </summary>
    /// <remarks>
    ///     For example, 1&lt;&lt;1+1 must mean 1 shifted by (1+1), giving 4. Consistent precedence matters when a
    ///     struct's array length is calculated from mixed operators without extra parentheses.
    /// </remarks>
    [TestMethod]
    public void Expressions_UseSharedPrecedenceRows()
    {
        Assert.AreEqual(4, CStructDefinitionParser.Expr.ParseOrThrow("6 * 2 / 3").Calc());
        Assert.AreEqual(7, CStructDefinitionParser.Expr.ParseOrThrow("1 + 2 * 3").Calc());
        Assert.AreEqual(4, CStructDefinitionParser.Expr.ParseOrThrow("1 << 1 + 1").Calc());
        Assert.AreEqual(4, CStructDefinitionParser.Expr.ParseOrThrow("8 >> 1 & 7").Calc());
    }

    /// <summary>
    ///     0A FF must become two bytes, 10 and 255.
    /// </summary>
    /// <remarks>
    ///     An odd digit count or comma-separated input must throw FormatException. Rejecting unexpected characters
    ///     keeps a copied hex fixture from silently changing before the binary parser sees it.
    /// </remarks>
    [TestMethod]
    public void ParseHexDataContent_IsStrictByDefault()
    {
        CollectionAssert.AreEqual(new byte[] { 0x0A, 0xFF, }, "0A FF".ParseHexDataContent());
        Assert.Throws<FormatException>(() => "0AF".ParseHexDataContent());
        Assert.Throws<FormatException>(() => "0A,FF".ParseHexDataContent());
    }

    /// <summary>
    ///     A seeded generator supplies 128 combinations for byte, uint16, and uint32 fields.
    /// </summary>
    /// <remarks>
    ///     Serializing and then parsing each record must recover every original number. The fixed seed makes failures
    ///     repeatable while checking more values than one hand-written byte example.
    /// </remarks>
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
    ///     The test generates 96 small layouts with different integer fields and alternates packed and aligned mode.
    /// </summary>
    /// <remarks>
    ///     Reported size must match the produced buffer length, and reading must recover the supplied values. This
    ///     checks combinations of field widths and padding rather than one fixed record shape.
    /// </remarks>
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
    ///     Random bytes are interpreted as a count, an array, and terminated text under small limits.
    /// </summary>
    /// <remarks>
    ///     Some inputs must parse successfully and others must raise documented read errors. Unrelated runtime
    ///     exceptions are failures of the test, as would be a corpus that stopped exercising either successful or
    ///     rejected reads.
    /// </remarks>
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

    /// <summary>
    ///     The supplied destination accepts writes but cannot seek or report normal positioning.
    /// </summary>
    /// <remarks>
    ///     WriteStream must reject it at the public boundary even for a one-byte struct. The writer's layout and
    ///     alignment logic requires positioning support, so failure should be clear before writing begins.
    /// </remarks>
    [TestMethod]
    public void WriteStream_RequiresSeekableStream()
    {
        const string layout = "struct root { byte value; };";
        var cstruct = new CStruct(layout);
        using var stream = new WriteOnlyNonSeekableStream();

        Assert.Throws<ArgumentException>(() => cstruct.WriteStream(stream, "root", new { value = (byte)0xA5, }));
    }

    /// <summary>
    ///     The two-byte bitfield unit starts as A5 BC, but the stream returns at most one byte per read.
    /// </summary>
    /// <remarks>
    ///     Updating low or high must gather both bytes before masking in the replacement. Expected outputs A3 BC and 35
    ///     12 prove that untouched neighboring bits survive short reads.
    /// </remarks>
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
    ///     Only A5 is supplied for a two-byte storage unit.
    /// </summary>
    /// <remarks>
    ///     Updating its low four bits cannot safely proceed because the missing byte contains other bits that must be
    ///     preserved. The operation must raise a read error and leave the existing byte unchanged rather than extend
    ///     the stream.
    /// </remarks>
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
