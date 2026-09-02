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
    ///     Verifies readers retry legal short reads until every primitive and pointer byte is available. Network and
    ///     decompression streams may fragment reads, so a single <see cref="Stream.Read(byte[], int, int)"/> result is
    ///     not evidence that a binary field has ended.
    /// </summary>
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

    /// <summary>Rejects a truncated primitive value instead of treating end-of-stream as a byte value.</summary>
    [TestMethod]
    public void ParseStream_RejectsTruncatedPrimitiveValues()
    {
        const string layout = "struct root { byte first; uint16 second; };";
        using var stream = new MemoryStream([0x01, 0x02,]);
        var cstruct = new CStruct(layout);

        Assert.Throws<CStructReadException>(() => cstruct.ParseStream(stream, "root"));
    }

    /// <summary>
    ///     Verifies pointer addresses use the configured big-endian byte order at every supported width before their
    ///     targets are dereferenced. A wrong byte order can turn a valid address into an unsafe, unrelated offset.
    /// </summary>
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

    /// <summary>Rejects a pointer whose decoded address lies outside the stream.</summary>
    [TestMethod]
    public void ParseStream_RejectsPointerTargetsOutsideTheStream()
    {
        const string layout = "struct root { byte *target; };";
        using var stream = new MemoryStream([0x80, 0x00,]);
        var cstruct = new CStruct(layout, 2, isLittleEndian: false);

        Assert.Throws<CStructReadException>(() => cstruct.ParseStream(stream, "root"));
    }

    /// <summary>
    ///     Verifies callers can retain a decoded pointer address without seeking to its target. This supports safe
    ///     inspection of incomplete, external, or intentionally opaque pointer graphs.
    /// </summary>
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

    /// <summary>Rejects recursive pointer graphs instead of recursing indefinitely.</summary>
    [TestMethod]
    public void ParseStream_RejectsCyclicPointers()
    {
        const string layout = "struct node { node *next; }; struct root { node *head; };";
        using var stream = new MemoryStream([0x02, 0x00, 0x02, 0x00,]);
        var cstruct = new CStruct(layout, 2);

        Assert.Throws<CStructReadException>(() => cstruct.ParseStream(stream, "root"));
    }

    /// <summary>
    ///     Verifies a fixed-size pointer target is rejected before decoding when it exceeds the caller's byte budget.
    ///     The limit bounds work caused by following attacker-controlled addresses.
    /// </summary>
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
    ///     Preserves the raw value of an enum member not known to the current layout while leaving its symbolic name
    ///     absent. This makes parsing forward-compatible with newer producers that add enum values.
    /// </summary>
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

    /// <summary>Rejects unresolved identifiers and unsupported calls in layout expressions.</summary>
    [TestMethod]
    public void Expressions_RejectUndefinedIdentifiersAndCalls()
    {
        Assert.Throws<KeyNotFoundException>(() => CStructDefinitionParser.Expr.ParseOrThrow("missing").Calc());
        Assert.Throws<NotSupportedException>(() => CStructDefinitionParser.Expr.ParseOrThrow("unsupported(1)").Calc());
    }

    /// <summary>Fails fast for pointer widths the binary reader cannot represent.</summary>
    [TestMethod]
    public void Constructor_RejectsUnsupportedPointerSizes()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new CStruct("struct root { byte value; };", 3));
    }

    /// <summary>
    ///     Verifies pointer serialization mirrors pointer parsing by encoding addresses in the layout's selected byte
    ///     order. This is essential when producing data for a binary format with a non-host endian convention.
    /// </summary>
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

    /// <summary>Protects updates through a null pointer unless explicitly configured otherwise.</summary>
    [TestMethod]
    public void UpdateStream_RejectsNullPointerTargetByDefault()
    {
        const string layout = "struct root { byte *target; };";
        using var stream = new MemoryStream([0x00, 0x00,]);
        var cstruct = new CStruct(layout, 2);

        Assert.Throws<CStructReadException>(() => cstruct.UpdateStream(stream, "root.target.value", (byte)0xA5));
    }

    /// <summary>
    ///     Verifies an in-place update clears the full union allocation before writing a shorter selected member. Without
    ///     this rule, bytes from a previous larger interpretation remain observable through the union's other members.
    /// </summary>
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
    ///     Resolves definitions declared after their use and confirms parsing works on an internal variable copy. Caller
    ///     variables are input context, so parsing must not add derived definitions to the supplied dictionary.
    /// </summary>
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

    /// <summary>Reports circular preprocessor definitions deterministically.</summary>
    [TestMethod]
    public void ParseStream_RejectsCircularDefines()
    {
        const string layout = "#define first second #define second first struct root { byte value[first]; };";

        // Definitions are compiled with the layout, so a cycle is rejected before a caller can start a parse.
        Assert.Throws<CStructLayoutException>(() => new CStruct(layout));
    }

    /// <summary>Exposes compiled layout and codec maps as read-only views after construction.</summary>
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
    ///     Verifies a bitfield that spans its complete unsigned 32-bit storage unit survives serialization and parsing.
    ///     The case protects mask calculations from signed shifts or narrowing that would lose the high bit.
    /// </summary>
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

    /// <summary>Rejects bitfield widths that exceed the declared scalar storage unit.</summary>
    [TestMethod]
    public void Bitfields_RejectWidthsLargerThanStorage()
    {
        const string layout = "struct root { byte flags:9; };";

        // Invalid declarations are rejected at compilation time before a caller can consume any input bytes.
        Assert.Throws<CStructLayoutException>(() => new CStruct(layout));
    }
}
