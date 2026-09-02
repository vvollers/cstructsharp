namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Verifies checked pointer-address conversion across every public read, path, and write operation.</summary>
[TestClass]
public class PointerArithmeticTests
{
    /// <summary>
    ///     Rejects a negative real target even when subtracting a more-negative relative origin produces a positive
    ///     stored offset. Serialize, direct write, and update must all fail before changing output.
    /// </summary>
    [TestMethod]
    public void RelativePointerWrites_RejectNegativeActualTargetsBeforeOutput()
    {
        var cstruct = new CStruct("struct root { byte *ptr; };", pointerSize: 1);
        var options = new WriteOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = -2,
        };
        ExpandoObject data = CreatePointerData(-1L);

        Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", data, options: options));

        byte[] original = [0xA5, 0xA5, 0xA5,];
        using var writeStream = new MemoryStream((byte[])original.Clone()) { Position = 1, };
        Assert.Throws<CStructWriteException>(
            () => cstruct.WriteStream(writeStream, "root", data, options: options));
        CollectionAssert.AreEqual(original, writeStream.ToArray());
        Assert.AreEqual(1L, writeStream.Position);

        using var updateStream = new MemoryStream((byte[])original.Clone()) { Position = 1, };
        var updateOptions = new UpdateOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = -2,
        };
        Assert.Throws<CStructWriteException>(
            () => cstruct.UpdateStream(updateStream, "root.ptr.address", -1L, options: updateOptions));
        CollectionAssert.AreEqual(original, updateStream.ToArray());
        Assert.AreEqual(1L, updateStream.Position);
    }

    /// <summary>Maps pointer conversion, relative subtraction, null ambiguity, and width failures to the write domain.</summary>
    [TestMethod]
    public void PointerWriteFailures_UseCStructWriteException()
    {
        var wide = new CStruct("struct root { byte *ptr; };", pointerSize: 8);

        CStructWriteException relativeOverflow = Assert.Throws<CStructWriteException>(
            () => wide.Serialize(
                "root",
                CreatePointerData(long.MaxValue),
                options: new WriteOptions
                {
                    AddressingMode = PointerAddressingMode.Relative,
                    Origin = -1,
                }));
        Assert.IsInstanceOfType<OverflowException>(relativeOverflow.InnerException);
        StringAssert.Contains(relativeOverflow.Message, "Relative pointer address");

        CStructWriteException conversionOverflow = Assert.Throws<CStructWriteException>(
            () => wide.Serialize("root", CreatePointerData(ulong.MaxValue)));
        Assert.IsInstanceOfType<OverflowException>(conversionOverflow.InnerException);
        StringAssert.Contains(conversionOverflow.Message, "signed stream-position range");

        _ = Assert.Throws<CStructWriteException>(
            () => wide.Serialize("root", CreatePointerData(-1L)));
        _ = Assert.Throws<CStructWriteException>(
            () => wide.Serialize(
                "root",
                CreatePointerData(1L),
                options: new WriteOptions
                {
                    AddressingMode = PointerAddressingMode.Relative,
                    Origin = 1,
                }));

        var narrow = new CStruct("struct root { byte *ptr; };", pointerSize: 1);
        _ = Assert.Throws<CStructWriteException>(
            () => narrow.Serialize("root", CreatePointerData(256L)));
    }

    /// <summary>
    ///     Accepts the largest storable signed stream address and rejects the first larger value at every configured
    ///     pointer width, both byte orders, and both addressing modes.
    /// </summary>
    /// <param name="pointerSize">The configured pointer storage width.</param>
    /// <param name="isLittleEndian">Whether the pointer storage is least-significant-byte first.</param>
    [TestMethod]
    [DataRow(1, true)]
    [DataRow(1, false)]
    [DataRow(2, true)]
    [DataRow(2, false)]
    [DataRow(4, true)]
    [DataRow(4, false)]
    [DataRow(8, true)]
    [DataRow(8, false)]
    public void PointerWrites_EnforceExactWidthBoundaries(int pointerSize, bool isLittleEndian)
    {
        var cstruct = new CStruct(
            "struct root { byte *ptr; };",
            pointerSize: (byte)pointerSize,
            isLittleEndian: isLittleEndian);
        object maximum = pointerSize switch
        {
            1 => (long)byte.MaxValue,
            2 => (long)ushort.MaxValue,
            4 => (long)uint.MaxValue,
            8 => long.MaxValue,
            _ => throw new InvalidOperationException(),
        };
        object beyondMaximum = pointerSize switch
        {
            1 => (long)byte.MaxValue + 1,
            2 => (long)ushort.MaxValue + 1,
            4 => (long)uint.MaxValue + 1,
            8 => (ulong)long.MaxValue + 1,
            _ => throw new InvalidOperationException(),
        };

        foreach (PointerAddressingMode mode in Enum.GetValues<PointerAddressingMode>())
        {
            var options = new WriteOptions { AddressingMode = mode, };
            byte[] encoded = cstruct.Serialize("root", CreatePointerData(maximum), options: options);
            CollectionAssert.AreEqual(
                EncodeUnsigned(Convert.ToUInt64(maximum), pointerSize, isLittleEndian),
                encoded,
                $"size={pointerSize}, endian={isLittleEndian}, mode={mode}");

            _ = Assert.Throws<CStructWriteException>(
                () => cstruct.Serialize("root", CreatePointerData(beyondMaximum), options: options),
                $"size={pointerSize}, endian={isLittleEndian}, mode={mode}");
        }
    }

    /// <summary>
    ///     Uses read exceptions for materialization, path exceptions for relative path arithmetic, and preserves query
    ///     and update state when an extreme origin overflows.
    /// </summary>
    /// <param name="isLittleEndian">Whether the stored address is least-significant-byte first.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void RelativeReadOverflow_UsesOperationDomainExceptions(bool isLittleEndian)
    {
        const int rootStart = 2;
        var cstruct = new CStruct(
            "struct root { char *ptr; };",
            pointerSize: 8,
            isLittleEndian: isLittleEndian);
        byte[] original = Enumerable.Repeat((byte)0xA5, 16).ToArray();
        EncodeUnsigned(1, 8, isLittleEndian).CopyTo(original, rootStart);
        var options = new ReadOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = long.MaxValue,
        };

        using (var parseStream = new MemoryStream((byte[])original.Clone()) { Position = rootStart, })
        {
            CStructReadException exception = Assert.Throws<CStructReadException>(
                () => cstruct.ParseStream(
                    parseStream,
                    "root",
                    variables: (IReadOnlyDictionary<string, int>?)null,
                    options));
            Assert.IsInstanceOfType<OverflowException>(exception.InnerException);
        }

        using (var debugStream = new MemoryStream((byte[])original.Clone()) { Position = rootStart, })
        {
            CStructReadException exception = Assert.Throws<CStructReadException>(
                () => cstruct.ParseStreamWithDebug(
                    debugStream,
                    "root",
                    new Dictionary<string, Structure.Expr>(),
                    options));
            Assert.IsInstanceOfType<OverflowException>(exception.InnerException);
        }

        using (var addressStream = new MemoryStream((byte[])original.Clone()) { Position = rootStart, })
        {
            CStructPathException exception = Assert.Throws<CStructPathException>(
                () => cstruct.ResolveAddress(addressStream, "root.ptr.value", options: options));
            Assert.IsInstanceOfType<OverflowException>(exception.InnerException);
            Assert.AreEqual(rootStart, addressStream.Position);
        }

        using (var lengthStream = new MemoryStream((byte[])original.Clone()) { Position = rootStart, })
        {
            CStructPathException exception = Assert.Throws<CStructPathException>(
                () => cstruct.GetDynamicArrayLength(lengthStream, "root.ptr.value", options: options));
            Assert.IsInstanceOfType<OverflowException>(exception.InnerException);
            Assert.AreEqual(rootStart, lengthStream.Position);
        }

        using var updateStream = new MemoryStream((byte[])original.Clone()) { Position = rootStart, };
        var updateOptions = new UpdateOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = long.MaxValue,
        };
        CStructPathException updateException = Assert.Throws<CStructPathException>(
            () => cstruct.UpdateStream(updateStream, "root.ptr.value", 'Z', options: updateOptions));
        Assert.IsInstanceOfType<OverflowException>(updateException.InnerException);
        CollectionAssert.AreEqual(original, updateStream.ToArray());
        Assert.AreEqual(rootStart, updateStream.Position);
    }

    /// <summary>Rejects unsigned 64-bit pointer payloads that cannot be represented as signed stream positions.</summary>
    /// <param name="isLittleEndian">Whether the stored address is least-significant-byte first.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void EightBytePointerReads_RejectValuesBeyondSignedStreamRange(bool isLittleEndian)
    {
        byte[] original = EncodeUnsigned(1UL << 63, 8, isLittleEndian);
        var cstruct = new CStruct(
            "struct root { byte *ptr; };",
            pointerSize: 8,
            isLittleEndian: isLittleEndian);
        var noDereference = new ReadOptions { DereferencePointers = false, };

        using (var parseStream = new MemoryStream((byte[])original.Clone()))
        {
            CStructReadException exception = Assert.Throws<CStructReadException>(
                () => cstruct.ParseStream(
                    parseStream,
                    "root",
                    variables: (IReadOnlyDictionary<string, int>?)null,
                    noDereference));
            StringAssert.Contains(exception.Message, "supported stream address range");
            Assert.IsInstanceOfType<OverflowException>(exception.InnerException);
            StringAssert.Contains(exception.InnerException.Message, "signed stream-position range");
        }

        using (var debugStream = new MemoryStream((byte[])original.Clone()))
        {
            _ = Assert.Throws<CStructReadException>(
                () => cstruct.ParseStreamWithDebug(
                    debugStream,
                    "root",
                    new Dictionary<string, Structure.Expr>(),
                    noDereference));
        }

        using (var addressStream = new MemoryStream((byte[])original.Clone()) { Position = 0, })
        {
            _ = Assert.Throws<CStructReadException>(
                () => cstruct.ResolveAddress(
                    addressStream,
                    "root.ptr.value",
                    options: new ReadOptions()));
            Assert.AreEqual(0L, addressStream.Position);
        }

        using var updateStream = new MemoryStream((byte[])original.Clone());
        _ = Assert.Throws<CStructReadException>(
            () => cstruct.UpdateStream(updateStream, "root.ptr.value", (byte)1));
        CollectionAssert.AreEqual(original, updateStream.ToArray());
        Assert.AreEqual(0L, updateStream.Position);

        using var maximumStream = new MemoryStream(
            EncodeUnsigned((ulong)long.MaxValue, 8, isLittleEndian));
        dynamic maximumResult = cstruct.ParseStream(
            maximumStream,
            "root",
            variables: (IReadOnlyDictionary<string, int>?)null,
            noDereference);
        Assert.AreEqual(long.MaxValue, ((Pointer)maximumResult.ptr).Address);

        var dependent = new CStruct(
            "struct root { byte *ptr; byte values[ptr]; };",
            pointerSize: 8,
            isLittleEndian: isLittleEndian);
        byte[] dependentBytes = new byte[9];
        EncodeUnsigned((ulong)long.MaxValue, 8, isLittleEndian).CopyTo(dependentBytes, 0);
        var staleOverride = new Dictionary<string, Structure.Expr>
        {
            ["ptr"] = new Structure.Literal(1),
        };
        using var dependentParse = new MemoryStream((byte[])dependentBytes.Clone());
        _ = Assert.Throws<CStructLayoutException>(
            () => dependent.ParseStream(dependentParse, "root", staleOverride, noDereference));

        using var dependentAddress = new MemoryStream((byte[])dependentBytes.Clone());
        _ = Assert.Throws<CStructLayoutException>(
            () => dependent.ResolveAddress(
                dependentAddress,
                "root.values[0]",
                staleOverride,
                noDereference));
        Assert.AreEqual(0L, dependentAddress.Position);
    }

    /// <summary>
    ///     Keeps encoded zero reserved for null while allowing a nonzero relative offset to resolve to physical stream
    ///     address zero.
    /// </summary>
    [TestMethod]
    public void RelativePointers_DefineAddressZeroSemanticsAtExtremeOrigins()
    {
        var cstruct = new CStruct("struct root { byte *ptr; };", pointerSize: 1);
        var targetZeroOptions = new ReadOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = -1,
        };
        using var targetZeroStream = new MemoryStream(new byte[] { 0x2A, 0x01, }) { Position = 1, };

        dynamic parsed = cstruct.ParseStream(
            targetZeroStream,
            "root",
            variables: (IReadOnlyDictionary<string, int>?)null,
            targetZeroOptions);
        var pointer = (Pointer)parsed.ptr;
        Assert.AreEqual(1L, pointer.Address);
        Assert.AreEqual((byte)0x2A, pointer.Value);

        using var absoluteStream = new MemoryStream(new byte[] { 0x01, 0x2A, });
        dynamic absoluteResult = cstruct.ParseStream(
            absoluteStream,
            "root",
            variables: (IReadOnlyDictionary<string, int>?)null,
            new ReadOptions
            {
                AddressingMode = PointerAddressingMode.Absolute,
                Origin = long.MaxValue,
            });
        Assert.AreEqual((byte)0x2A, ((Pointer)absoluteResult.ptr).Value);

        byte[] pointerObject = cstruct.Serialize(
            "root",
            CreatePointerData(new Pointer(1, null, 1, false)));
        CollectionAssert.AreEqual(new byte[] { 0x01, }, pointerObject);

        foreach (long origin in new[] { long.MinValue, long.MaxValue, })
        {
            var readOptions = new ReadOptions
            {
                AddressingMode = PointerAddressingMode.Relative,
                Origin = origin,
            };
            using var nullStream = new MemoryStream([0x00,]);
            dynamic nullResult = cstruct.ParseStream(
                nullStream,
                "root",
                variables: (IReadOnlyDictionary<string, int>?)null,
                readOptions);
            var nullPointer = (Pointer)nullResult.ptr;
            Assert.AreEqual(0L, nullPointer.Address);
            Assert.IsNull(nullPointer.Value);

            byte[] encodedNull = cstruct.Serialize(
                "root",
                CreatePointerData(0L),
                options: new WriteOptions
                {
                    AddressingMode = PointerAddressingMode.Relative,
                    Origin = origin,
                });
            CollectionAssert.AreEqual(new byte[] { 0x00, }, encodedNull);
        }
    }

    private static ExpandoObject CreatePointerData(object value)
    {
        dynamic data = new ExpandoObject();
        data.ptr = value;
        return data;
    }

    private static byte[] EncodeUnsigned(ulong value, int width, bool isLittleEndian)
    {
        byte[] result = new byte[width];
        for (int index = 0; index < width; index++)
        {
            int target = isLittleEndian ? index : width - index - 1;
            result[target] = (byte)(value >> (index * 8));
        }

        return result;
    }
}
