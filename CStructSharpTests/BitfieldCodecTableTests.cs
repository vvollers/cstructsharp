namespace CStructSharp.Tests;

using CStructSharp.Structure;

/// <summary>
///     Exercises <see cref="BitfieldCodecTable"/> directly, independent of a compiled <see cref="CStruct"/> layout.
///     Only reachable indirectly through the public API before this type was extracted from the God-Object
///     <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class BitfieldCodecTableTests
{
    /// <summary>
    ///     A complete, self-consistent primitive registration (every scalar integral type the constructor expects)
    ///     builds a table whose entries have the expected byte size, byte order, and bit capacity, and whose
    ///     C-style aliases resolve to the same entry as their canonical spelling.
    /// </summary>
    [TestMethod]
    public void Constructor_BuildsEntriesAndAliasesFromConsistentPrimitiveTables()
    {
        BitfieldCodecTable table = CreateValidTable(isLittleEndian: true, out _, out _, out _);

        BitfieldCodecTable.Entry byteEntry = table.ValidateBitField(BitfieldField("uint8", bitSize: 3));
        Assert.AreEqual(1, byteEntry.ByteSize);
        Assert.AreEqual(8, byteEntry.BitCapacity);

        BitfieldCodecTable.Entry neutralEntry = table.ValidateBitField(BitfieldField("uint32", bitSize: 5));
        Assert.AreEqual(4, neutralEntry.ByteSize);
        Assert.AreEqual(32, neutralEntry.BitCapacity);
        Assert.IsTrue(neutralEntry.IsLittleEndian, "The neutral uint32 spelling must follow the instance's default byte order.");

        BitfieldCodecTable.Entry bigEndianEntry = table.ValidateBitField(BitfieldField("uint32>", bitSize: 5));
        Assert.IsFalse(bigEndianEntry.IsLittleEndian, "An explicit '>' suffix is always big-endian regardless of the default.");

        BitfieldCodecTable.Entry littleEndianEntry = table.ValidateBitField(BitfieldField("uint32<", bitSize: 5));
        Assert.IsTrue(littleEndianEntry.IsLittleEndian, "An explicit '<' suffix is always little-endian regardless of the default.");

        // "uint" is registered as an alias of "uint32" and must resolve to an identical entry.
        BitfieldCodecTable.Entry aliasedEntry = table.ValidateBitField(BitfieldField("uint", bitSize: 5));
        Assert.AreEqual(neutralEntry, aliasedEntry);
    }

    /// <summary>
    ///     The neutral (unsuffixed) multi-byte entries must follow the instance's default byte order, matching a
    ///     big-endian instance instead of always defaulting to little-endian.
    /// </summary>
    [TestMethod]
    public void Constructor_NeutralEntries_FollowBigEndianDefaultWhenRequested()
    {
        BitfieldCodecTable table = CreateValidTable(isLittleEndian: false, out _, out _, out _);

        BitfieldCodecTable.Entry neutralEntry = table.ValidateBitField(BitfieldField("uint32", bitSize: 5));

        Assert.IsFalse(neutralEntry.IsLittleEndian);
    }

    /// <summary>
    ///     "byte" is declared as one byte everywhere else in the layout, but the alignment table the constructor is
    ///     given here claims it is two bytes.
    /// </summary>
    /// <remarks>
    ///     Registration must fail loudly instead of silently trusting a size that disagrees with the rest of the
    ///     compiled primitive tables, since a bitfield storage-unit size mismatch would corrupt neighboring bits.
    /// </remarks>
    [TestMethod]
    public void Constructor_RejectsAnAlignmentThatDisagreesWithTheDeclaredByteSize()
    {
        Dictionary<string, byte> alignments = ValidAlignments();
        alignments["byte"] = 2;

        Assert.Throws<InvalidOperationException>(
            () => new BitfieldCodecTable(
                true,
                alignments,
                ValidFieldHandlers(),
                ValidWriteHandlers(),
                new Dictionary<string, string>()));
    }

    /// <summary>A primitive type with no registered reader cannot be trusted as bitfield storage.</summary>
    [TestMethod]
    public void Constructor_RejectsAPrimitiveWithNoRegisteredReader()
    {
        Dictionary<string, Func<Stream, object>> handlers = ValidFieldHandlers();
        handlers.Remove("byte");

        Assert.Throws<InvalidOperationException>(
            () => new BitfieldCodecTable(
                true,
                ValidAlignments(),
                handlers,
                ValidWriteHandlers(),
                new Dictionary<string, string>()));
    }

    /// <summary>A primitive type with no registered writer cannot be trusted as bitfield storage.</summary>
    [TestMethod]
    public void Constructor_RejectsAPrimitiveWithNoRegisteredWriter()
    {
        Dictionary<string, Action<Stream, object>> handlers = ValidWriteHandlers();
        handlers.Remove("byte");

        Assert.Throws<InvalidOperationException>(
            () => new BitfieldCodecTable(
                true,
                ValidAlignments(),
                ValidFieldHandlers(),
                handlers,
                new Dictionary<string, string>()));
    }

    /// <summary>An array field, regardless of its element type, cannot be a bitfield.</summary>
    [TestMethod]
    public void ValidateBitField_RejectsAnArrayField()
    {
        BitfieldCodecTable table = CreateValidTable(true, out _, out _, out _);
        var arrayField = new Field(new Identifier("uint8"), new Identifier("values"), new Literal(4), 3);

        Assert.Throws<InvalidOperationException>(() => table.ValidateBitField(arrayField));
    }

    /// <summary>A pointer field cannot be a bitfield, even if its pointee type is otherwise valid storage.</summary>
    [TestMethod]
    public void ValidateBitField_RejectsAPointerField()
    {
        BitfieldCodecTable table = CreateValidTable(true, out _, out _, out _);
        var pointerField = new Field(new Identifier("uint8"), new Identifier("ptr"), Field.NoArray, 3, pointerDepth: 1);

        Assert.Throws<InvalidOperationException>(() => table.ValidateBitField(pointerField));
    }

    /// <summary>A type that was never registered as bitfield-capable storage (here, a made-up name) is rejected.</summary>
    [TestMethod]
    public void ValidateBitField_RejectsAnUnregisteredStorageType()
    {
        BitfieldCodecTable table = CreateValidTable(true, out _, out _, out _);

        Assert.Throws<InvalidOperationException>(() => table.ValidateBitField(BitfieldField("not_a_real_type", bitSize: 3)));
    }

    /// <summary>A declared bit width that exceeds the storage unit's capacity is rejected.</summary>
    [TestMethod]
    public void ValidateBitField_RejectsAWidthLargerThanTheStorageUnit()
    {
        BitfieldCodecTable table = CreateValidTable(true, out _, out _, out _);

        Assert.Throws<InvalidOperationException>(() => table.ValidateBitField(BitfieldField("uint8", bitSize: 9)));
    }

    /// <summary>Extracting then merging the same slice back must reproduce the original storage value exactly.</summary>
    [TestMethod]
    public void ExtractAndMergeBitfieldValue_AreInverseOperations()
    {
        const ulong original = 0b1010_1100_0011_0101UL;

        ulong extracted = BitfieldCodecTable.ExtractBitfieldValue((ushort)original, bitOffset: 4, bitSize: 6);
        ulong merged = BitfieldCodecTable.MergeBitfieldValue(original, extracted, bitOffset: 4, bitSize: 6);

        Assert.AreEqual(original, merged);
    }

    /// <summary>Merging a new slice must leave every bit outside the slice's range untouched.</summary>
    [TestMethod]
    public void MergeBitfieldValue_LeavesNeighboringBitsUntouched()
    {
        ulong merged = BitfieldCodecTable.MergeBitfieldValue(
            storageValue: 0b1111_1111UL,
            fieldValue: 0b00UL,
            bitOffset: 2,
            bitSize: 4);

        Assert.AreEqual(0b1100_0011UL, merged);
    }

    /// <summary>A 64-bit mask must be the full unsigned range without an overflowing left shift.</summary>
    [TestMethod]
    public void GetBitfieldMask_HandlesTheFullSixtyFourBitCase()
    {
        Assert.AreEqual(ulong.MaxValue, BitfieldCodecTable.GetBitfieldMask(64));
        Assert.AreEqual(0b1111UL, BitfieldCodecTable.GetBitfieldMask(4));
        Assert.AreEqual(0UL, BitfieldCodecTable.GetBitfieldMask(0));
    }

    /// <summary>Every signed primitive width reinterprets to the same bit pattern as its unsigned counterpart.</summary>
    [TestMethod]
    public void ConvertBitfieldStorageToUnsigned_ReinterpretsSignedValuesBitwise()
    {
        Assert.AreEqual((ulong)byte.MaxValue, BitfieldCodecTable.ConvertBitfieldStorageToUnsigned((sbyte)-1));
        Assert.AreEqual((ulong)ushort.MaxValue, BitfieldCodecTable.ConvertBitfieldStorageToUnsigned((short)-1));
        Assert.AreEqual((ulong)uint.MaxValue, BitfieldCodecTable.ConvertBitfieldStorageToUnsigned(-1));
        Assert.AreEqual(ulong.MaxValue, BitfieldCodecTable.ConvertBitfieldStorageToUnsigned(-1L));
    }

    /// <summary>A null value is never a valid bitfield replacement.</summary>
    [TestMethod]
    public void ValidateBitfieldWriteValue_RejectsNull()
    {
        Field field = BitfieldField("uint8", bitSize: 4);

        Assert.Throws<CStructWriteException>(() => BitfieldCodecTable.ValidateBitfieldWriteValue(field, null));
    }

    /// <summary>A boolean, and a fractional decimal/double/float, are outside the bitfield's unsigned integer domain.</summary>
    [TestMethod]
    public void ValidateBitfieldWriteValue_RejectsValuesOutsideTheIntegerDomain()
    {
        Field field = BitfieldField("uint8", bitSize: 4);

        Assert.Throws<CStructWriteException>(() => BitfieldCodecTable.ValidateBitfieldWriteValue(field, true));
        Assert.Throws<CStructWriteException>(() => BitfieldCodecTable.ValidateBitfieldWriteValue(field, 1.5));
        Assert.Throws<CStructWriteException>(() => BitfieldCodecTable.ValidateBitfieldWriteValue(field, 1.5f));
        Assert.Throws<CStructWriteException>(() => BitfieldCodecTable.ValidateBitfieldWriteValue(field, 1.5m));
    }

    /// <summary>A value that fits the declared bit width converts cleanly; one that does not is rejected.</summary>
    [TestMethod]
    public void ValidateBitfieldWriteValue_AcceptsInRangeAndRejectsOutOfRangeValues()
    {
        Field field = BitfieldField("uint8", bitSize: 4);

        Assert.AreEqual(15UL, BitfieldCodecTable.ValidateBitfieldWriteValue(field, 15));
        Assert.Throws<CStructWriteException>(() => BitfieldCodecTable.ValidateBitfieldWriteValue(field, 16));
    }

    private static Field BitfieldField(string typeName, int bitSize)
    {
        return new Field(new Identifier(typeName), new Identifier("value"), Field.NoArray, bitSize);
    }

    private static BitfieldCodecTable CreateValidTable(
        bool isLittleEndian,
        out Dictionary<string, byte> alignments,
        out Dictionary<string, Func<Stream, object>> fieldHandlers,
        out Dictionary<string, Action<Stream, object>> writeHandlers)
    {
        alignments = ValidAlignments();
        fieldHandlers = ValidFieldHandlers();
        writeHandlers = ValidWriteHandlers();
        var aliases = new Dictionary<string, string> { ["uint"] = "uint32", };
        return new BitfieldCodecTable(isLittleEndian, alignments, fieldHandlers, writeHandlers, aliases);
    }

    /// <summary>
    ///     Every scalar integral type name <see cref="BitfieldCodecTable"/>'s constructor unconditionally tries to
    ///     register, with the same byte widths <c>CStruct.BuildFieldHandlers</c> would have measured for them.
    /// </summary>
    private static Dictionary<string, byte> ValidAlignments()
    {
        var alignments = new Dictionary<string, byte>
        {
            ["byte"] = 1,
            ["int8"] = 1,
            ["uint8"] = 1,
            ["char"] = 1,
        };
        foreach ((string name, byte byteSize) in MultiByteTypes())
        {
            alignments[name] = byteSize;
            alignments[name + ">"] = byteSize;
            alignments[name + "<"] = byteSize;
        }

        return alignments;
    }

    private static Dictionary<string, Func<Stream, object>> ValidFieldHandlers()
    {
        return ValidAlignments().Keys.ToDictionary(name => name, _ => (Func<Stream, object>)(_ => 0));
    }

    private static Dictionary<string, Action<Stream, object>> ValidWriteHandlers()
    {
        return ValidAlignments().Keys.ToDictionary(name => name, _ => (Action<Stream, object>)((_, _) => { }));
    }

    private static IEnumerable<(string Name, byte ByteSize)> MultiByteTypes()
    {
        yield return ("wchar", 2);
        yield return ("int16", 2);
        yield return ("uint16", 2);
        yield return ("int32", 4);
        yield return ("uint32", 4);
        yield return ("int64", 8);
        yield return ("uint64", 8);
    }
}
