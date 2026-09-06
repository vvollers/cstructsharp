namespace CStructSharp.Tests;

using System;
using System.IO;
using CStructSharp.Structure;
using Pidgin;

/// <summary>Groups tests for robustness tests so changes to this behavior are caught.</summary>
[TestClass]
public class RobustnessTests
{
    /// <summary>
    ///     The stream returns only one byte per read, but the layout needs a uint64 and a two-byte pointer.
    /// </summary>
    /// <remarks>
    ///     Repeated reads must assemble 0x0102030405060708 and follow address 10 to 0x1234. A short read is not end-of-
    ///     stream when more bytes remain.
    /// </remarks>
    [TestMethod]
    public void ParseStream_ReadsNumericValuesAndPointersFromChunkedStream()
    {
        const string layout = "struct root { uint64 value; uint16 *target; };";
        byte[] bytes = [0x08, 0x07, 0x06, 0x05, 0x04, 0x03, 0x02, 0x01, 0x0A, 0x00, 0x34, 0x12,];
        using ChunkedMemoryStream stream = RegressionTestSupport.CreateChunkedStream(bytes, 1);
        var cstruct = new CStruct(layout, 2);

        dynamic result = cstruct.ParseStream(stream, "root");

        Assert.AreEqual(0x0102030405060708UL, (ulong)result.value);
        Pointer pointer = result.target;
        Assert.AreEqual(10L, pointer.Address);
        Assert.AreEqual((ushort)0x1234, (ushort)pointer.Value!);
    }

    /// <summary>
    ///     The first byte is available, but second is a uint16 with only one remaining byte.
    /// </summary>
    /// <remarks>
    ///     Parsing must throw a read error instead of filling the missing byte with zero or treating end-of-stream as
    ///     data. A complete field requires its declared number of bytes.
    /// </remarks>
    [TestMethod]
    public void ParseStream_RejectsTruncatedPrimitiveValues()
    {
        const string layout = "struct root { byte first; uint16 second; };";
        using var stream = new MemoryStream([0x01, 0x02,]);
        var cstruct = new CStruct(layout);

        Assert.Throws<CStructReadException>(() => cstruct.ParseStream(stream, "root"));
    }

    /// <summary>
    ///     For pointer widths 1, 2, 4, and 8, the encoded address points just after its own slot.
    /// </summary>
    /// <remarks>
    ///     Big-endian decoding must reach the target byte 0xA5 each time. Incorrect address byte order would send the
    ///     reader to a different, usually invalid location.
    /// </remarks>
    [TestMethod]
    public void ParseStream_DecodesBigEndianPointersForEverySupportedWidth()
    {
        const string layout = "struct root { byte *target; };";

        foreach (byte pointerSize in new byte[] { 1, 2, 4, 8, })
        {
            byte[] bytes = new byte[pointerSize + 1];
            bytes[pointerSize - 1] = pointerSize;
            bytes[pointerSize] = 0xA5;
            using var stream = new MemoryStream(bytes);
            var cstruct = new CStruct(layout, pointerSize, isLittleEndian: false);

            dynamic result = cstruct.ParseStream(stream, "root");
            Pointer pointer = result.target;

            Assert.AreEqual((long)pointerSize, pointer.Address, "Pointer size " + pointerSize);
            Assert.AreEqual((byte)0xA5, (byte)pointer.Value!, "Pointer size " + pointerSize);
        }
    }

    /// <summary>
    ///     The big-endian two-byte pointer contains 80 00, meaning address 32768, but the input is only two bytes long.
    /// </summary>
    /// <remarks>
    ///     Following it must fail with a read error. The library interprets pointers within the supplied stream and
    ///     cannot read arbitrary process memory.
    /// </remarks>
    [TestMethod]
    public void ParseStream_RejectsPointerTargetsOutsideTheStream()
    {
        const string layout = "struct root { byte *target; };";
        using var stream = new MemoryStream([0x80, 0x00,]);
        var cstruct = new CStruct(layout, 2, isLittleEndian: false);

        Assert.Throws<CStructReadException>(() => cstruct.ParseStream(stream, "root"));
    }

    /// <summary>
    ///     The pointer contains address 2, but DereferencePointers is false.
    /// </summary>
    /// <remarks>
    ///     Parsing must expose that address with IsDereferenced false and Value null, without visiting the 0xA5 target.
    ///     This supports inspecting pointer storage independently of reading what it points to.
    /// </remarks>
    [TestMethod]
    public void ParseStream_CanLeavePointersUndereferenced()
    {
        const string layout = "struct root { byte *target; };";
        using var stream = new MemoryStream([0x02, 0x00, 0xA5,]);
        var cstruct = new CStruct(layout, 2);

        dynamic result = cstruct.ParseStream(
                                             stream,
                                             "root",
                                             new System.Collections.Generic.Dictionary<string, Expr>(),
                                             new ReadOptions { DereferencePointers = false, });
        Pointer pointer = result.target;

        Assert.AreEqual(2L, pointer.Address);
        Assert.IsFalse(pointer.IsDereferenced);
        Assert.IsNull(pointer.Value);
    }

    /// <summary>
    ///     head points to a node at offset 2, whose next pointer points back to the same node.
    /// </summary>
    /// <remarks>
    ///     Parsing must detect this cycle and raise a read error. A finite byte buffer can describe an endless pointer
    ///     walk, so checking stream length alone is insufficient.
    /// </remarks>
    [TestMethod]
    public void ParseStream_RejectsCyclicPointers()
    {
        const string layout = "struct node { node *next; }; struct root { node *head; };";
        using var stream = new MemoryStream([0x02, 0x00, 0x02, 0x00,]);
        var cstruct = new CStruct(layout, 2);

        Assert.Throws<CStructReadException>(() => cstruct.ParseStream(stream, "root"));
    }

    /// <summary>
    ///     The uint32 target occupies four bytes at address 2, but the configured target-size allowance is smaller.
    /// </summary>
    /// <remarks>
    ///     Following it must fail before decoding the target. A pointer slot's own two-byte width does not determine
    ///     the work needed to read its target.
    /// </remarks>
    [TestMethod]
    public void ParseStream_EnforcesFixedPointerTargetLimit()
    {
        const string layout = "struct root { uint32 *target; };";
        using var stream = new MemoryStream([0x02, 0x00, 0x78, 0x56, 0x34, 0x12,]);
        var cstruct = new CStruct(layout, 2);

        Assert.Throws<CStructReadException>(
                                             () => cstruct.ParseStream(
                                                 stream,
                                                 "root",
                                                 new System.Collections.Generic.Dictionary<string, Expr>(),
                                                 new ReadOptions { MaxPointerTargetBytes = 2, }));
    }

    /// <summary>
    ///     The enum names only ready = 1, while the input contains 127.
    /// </summary>
    /// <remarks>
    ///     Parsing must preserve numeric value 127 with no name. This lets a reader retain values introduced by a newer
    ///     file producer without inventing a meaning or rejecting valid integer storage.
    /// </remarks>
    [TestMethod]
    public void ParseStream_PreservesUnknownEnumValues()
    {
        const string layout = "enum status { ready = 1 }; struct root { status value; };";
        using var stream = new MemoryStream([0x7F,]);
        var cstruct = new CStruct(layout);

        dynamic result = cstruct.ParseStream(stream, "root");
        EnumValueResult enumValue = result.value;

        Assert.IsNull(enumValue.Name);
        Assert.AreEqual(127, enumValue.Value);
    }

    /// <summary>
    ///     Evaluating missing without a supplied variable must raise KeyNotFoundException.
    /// </summary>
    /// <remarks>
    ///     Evaluating unsupported(1) must raise NotSupportedException because arbitrary function calls are not
    ///     implemented. These are direct expression tests; public layout construction wraps invalid definitions in its
    ///     own layout-error category.
    /// </remarks>
    [TestMethod]
    public void Expressions_RejectUndefinedIdentifiersAndCalls()
    {
        Assert.Throws<KeyNotFoundException>(() => CStructDefinitionParser.Expr.ParseOrThrow("missing").Calc());
        Assert.Throws<NotSupportedException>(() => CStructDefinitionParser.Expr.ParseOrThrow("unsupported(1)").Calc());
    }

    /// <summary>
    ///     The constructor is asked for three-byte pointers, while supported widths are 1, 2, 4, and 8.
    /// </summary>
    /// <remarks>
    ///     It must reject this option even though the sample record has no pointer field. Invalid configuration should
    ///     be caught immediately, not delayed until a later layout needs it.
    /// </remarks>
    [TestMethod]
    public void Constructor_RejectsUnsupportedPointerSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CStruct("struct root { byte value; };", 3));
    }

    /// <summary>
    ///     The supplied target address is 2 and the pointer occupies two bytes.
    /// </summary>
    /// <remarks>
    ///     Big-endian serialization must output 00 02. Only the address slot is written; serializing a pointer does not
    ///     automatically create or place its target data.
    /// </remarks>
    [TestMethod]
    public void Serialize_WritesPointersUsingConfiguredEndianness()
    {
        const string layout = "struct root { byte *target; };";
        dynamic data = new System.Dynamic.ExpandoObject();
        data.target = 2L;
        var cstruct = new CStruct(layout, 2, isLittleEndian: false);

        byte[] bytes = cstruct.Serialize("root", data);

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x02, }, bytes);
    }

    /// <summary>
    ///     The two-byte pointer contains zero, so it has no target.
    /// </summary>
    /// <remarks>
    ///     Updating target.value must fail under the default policy rather than write 0xA5 at stream offset zero.
    ///     Replacing a pointer address and writing through a null pointer are different operations.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_RejectsNullPointerTargetByDefault()
    {
        const string layout = "struct root { byte *target; };";
        using var stream = new MemoryStream([0x00, 0x00,]);
        var cstruct = new CStruct(layout, 2);

        Assert.Throws<CStructReadException>(() => cstruct.UpdateStream(stream, "root.target.value", (byte)0xA5));
    }

    /// <summary>
    ///     The union is four bytes wide, initially all FF.
    /// </summary>
    /// <remarks>
    ///     Replacing the whole union with selected small = 0x11 must yield 11 00 00 00. Default clearing removes
    ///     leftover bytes from a previous wider interpretation instead of keeping them observable through other member
    ///     views.
    /// </remarks>
    [TestMethod]
    public void UpdateStream_ClearsUnusedUnionStorageByDefault()
    {
        const string layout = "union choice { uint32 wide; byte small; }; struct root { choice value; };";
        using var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF,]);
        UnionValue value = UnionValue.FromMember("choice", "small", (byte)0x11);
        var cstruct = new CStruct(layout);

        cstruct.UpdateStream(stream, "root.value", value);

        CollectionAssert.AreEqual(new byte[] { 0x11, 0x00, 0x00, 0x00, }, stream.ToArray());
    }

    /// <summary>
    ///     second refers to first before first is declared.
    /// </summary>
    /// <remarks>
    ///     Resolving first = 2 must make second = 3 and read three array elements. The caller's dictionary must still
    ///     contain only external = 42, showing that derived definitions are kept in operation-owned state.
    /// </remarks>
    [TestMethod]
    public void ParseStream_ResolvesForwardDefinesWithoutMutatingSuppliedVariables()
    {
        const string layout = "#define second first + 1 #define first 2 struct root { byte values[second]; };";
        var variables = new System.Collections.Generic.Dictionary<string, Expr>
        {
            ["external"] = new Literal(42),
        };
        using var stream = new MemoryStream([0x01, 0x02, 0x03,]);
        var cstruct = new CStruct(layout);

        dynamic result = cstruct.ParseStream(stream, "root", variables);

        Assert.AreEqual(3, result.values.Count);
        Assert.AreEqual(1, variables.Count);
        Assert.AreEqual(42, variables["external"].Calc());
    }

    /// <summary>
    ///     first depends on second and second depends on first, so neither can produce an array count.
    /// </summary>
    /// <remarks>
    ///     Construction must detect the cycle and report a layout error. It must not endlessly substitute names or
    ///     defer an impossible dependency until binary parsing.
    /// </remarks>
    [TestMethod]
    public void ParseStream_RejectsCircularDefines()
    {
        const string layout = "#define first second #define second first struct root { byte value[first]; };";

        // Definitions are compiled with the layout, so a cycle is rejected before a caller can start a parse.
        Assert.Throws<CStructLayoutException>(() => new CStruct(layout));
    }

    /// <summary>
    ///     After construction, the public declaration, alignment, reader, and writer maps must be read-only.
    /// </summary>
    /// <remarks>
    ///     These maps describe the validated layout and supported conversions. Allowing callers to mutate them later
    ///     could make size calculations and stream operations disagree.
    /// </remarks>
    [TestMethod]
    public void Constructor_ExposesReadOnlyCompiledCollections()
    {
        var cstruct = new CStruct("struct root { byte value; };");

        Assert.IsTrue(cstruct.CStructElements is System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, CStructElement>> elements && elements.IsReadOnly);
        Assert.IsTrue(cstruct.FieldAlignments is System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, byte>> alignments && alignments.IsReadOnly);
        Assert.IsTrue(cstruct.FieldHandlers is System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, Func<Stream, object>>> readers && readers.IsReadOnly);
        Assert.IsTrue(cstruct.WriteHandlers is System.Collections.Generic.ICollection<System.Collections.Generic.KeyValuePair<string, Action<Stream, object>>> writers && writers.IsReadOnly);
    }

    /// <summary>
    ///     flags:32 uses all bits of a uint32.
    /// </summary>
    /// <remarks>
    ///     Four FF bytes must read as 4294967295 and serialize unchanged. Mask calculations must handle a full-width
    ///     slice without losing the top bit or treating it as a negative value.
    /// </remarks>
    [TestMethod]
    public void Bitfields_SupportFullStorageWidth()
    {
        const string layout = "struct root { uint32 flags:32; };";
        var cstruct = new CStruct(layout);
        using var input = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF,]);

        dynamic parsed = cstruct.ParseStream(input, "root");
        dynamic value = new System.Dynamic.ExpandoObject();
        value.flags = uint.MaxValue;
        byte[] serialized = cstruct.Serialize("root", value);

        Assert.AreEqual(uint.MaxValue, (ulong)parsed.flags);
        CollectionAssert.AreEqual(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, }, serialized);
    }

    /// <summary>
    ///     A byte has eight bits, so byte flags:9 cannot fit its declared backing storage.
    /// </summary>
    /// <remarks>
    ///     CStruct construction must reject it before any stream operation. The reader must not silently spill the
    ///     field into another byte or discard its extra bit.
    /// </remarks>
    [TestMethod]
    public void Bitfields_RejectWidthsLargerThanStorage()
    {
        const string layout = "struct root { byte flags:9; };";

        // Invalid declarations are rejected at compilation time before a caller can consume any input bytes.
        Assert.Throws<CStructLayoutException>(() => new CStruct(layout));
    }
}
