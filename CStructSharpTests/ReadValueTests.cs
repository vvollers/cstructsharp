namespace CStructSharp.Tests;

using System.Numerics;

/// <summary>Defines the natural and strongly typed read contract for root and selected layout values.</summary>
[TestClass]
public class ReadValueTests
{
    private enum Mode : ushort
    {
        Ready = 2,
    }

    /// <summary>Reads every major natural result shape through one selected-value API.</summary>
    [TestMethod]
    public void ReadValue_ReturnsNaturalRootNestedArrayEnumUnionAndPointerShapes()
    {
        const string layout = """
                              enum mode : uint16 { Ready = 0x1234 };
                              union choice { uint8 small; uint16 wide; };
                              struct child { uint16 number; };
                              struct root {
                                  byte count;
                                  uint16 values[count];
                                  mode state;
                                  choice data;
                                  child nested;
                                  uint16 *target;
                              };
                              """;
        byte[] bytes =
        [
            2,
            0x11, 0x11,
            0x22, 0x22,
            0x34, 0x12,
            0x78, 0x56,
            0xBC, 0x9A,
            13, 0,
            0xEF, 0xBE,
        ];
        var cstruct = new CStruct(layout, pointerSize: 2);

        using (var stream = new MemoryStream(bytes))
        {
            Assert.AreEqual((byte)2, cstruct.ReadValue(stream, "root.count"));
        }

        using (var stream = new MemoryStream(bytes))
        {
            CollectionAssert.AreEqual(
                new object?[] { (ushort)0x1111, (ushort)0x2222, },
                ((IList<object?>)cstruct.ReadValue(stream, "root.values")!).ToArray());
        }

        using (var stream = new MemoryStream(bytes))
        {
            Assert.AreEqual((ushort)0x2222, cstruct.ReadValue(stream, "root.values[1]"));
        }

        using (var stream = new MemoryStream(bytes))
        {
            var state = (EnumValueResult)cstruct.ReadValue(stream, "root.state")!;
            Assert.AreEqual(new BigInteger(0x1234), state.Value);
            Assert.AreEqual("Ready", state.Name);
        }

        using (var stream = new MemoryStream(bytes))
        {
            var union = (UnionValue)cstruct.ReadValue(stream, "root.data")!;
            CollectionAssert.AreEqual(new byte[] { 0x78, 0x56, }, union.RawStorage!.Value.ToArray());
        }

        using (var stream = new MemoryStream(bytes))
        {
            dynamic nested = cstruct.ReadValue(stream, "root.nested")!;
            Assert.AreEqual((ushort)0x9ABC, (ushort)nested.number);
        }

        using (var stream = new MemoryStream(bytes))
        {
            var pointer = (Pointer)cstruct.ReadValue(stream, "root.target")!;
            Assert.AreEqual(13L, pointer.Address);
            Assert.AreEqual((ushort)0xBEEF, pointer.Value);
        }

        using (var stream = new MemoryStream(bytes))
        {
            Assert.AreEqual(13L, cstruct.ReadValue(stream, "root.target.address"));
        }

        using (var stream = new MemoryStream(bytes))
        {
            Assert.AreEqual((ushort)0xBEEF, cstruct.ReadValue(stream, "root.target.value"));
        }

        using (var stream = new MemoryStream(bytes))
        {
            var singleRoot = new CStruct("struct root { byte count; };");
            dynamic root = singleRoot.ReadValue(stream)!;
            Assert.AreEqual((byte)2, (byte)root.count);
        }
    }

    /// <summary>Maps nested dynamic values to ordinary POCOs, arrays, nullable values, and CLR enums.</summary>
    [TestMethod]
    public void ReadValueOfT_MapsTypedRootAndNestedValuesWithExplicitConversions()
    {
        const string layout = """
                              enum mode : uint16 { Ready = 2 };
                              struct child { uint16 value; };
                              struct root {
                                  byte count;
                                  child children[count];
                                  mode state;
                                  uint16 *optional;
                              };
                              """;
        byte[] bytes =
        [
            2,
            0x34, 0x12,
            0x78, 0x56,
            2, 0,
            0, 0,
        ];
        var cstruct = new CStruct(layout, pointerSize: 2);

        using var stream = new MemoryStream(bytes);
        RootModel root = cstruct.ReadValue<RootModel>(stream, "root");

        Assert.AreEqual(2, root.Count);
        Assert.AreEqual(2, root.Children.Length);
        Assert.AreEqual(0x1234, root.Children[0].Value);
        Assert.AreEqual(0x5678, root.Children[1].Value);
        Assert.AreEqual(Mode.Ready, root.State);
        Assert.IsTrue(root.Optional.IsNull);

        stream.Position = 0;
        Assert.AreEqual(0x5678, cstruct.ReadValue<int>(stream, "root.children[1].value"));

        stream.Position = 0;
        CollectionAssert.AreEqual(
            new[] { 0x1234, 0x5678, },
            cstruct.ReadValue<ChildModel[]>(stream, "root.children").Select(item => item.Value).ToArray());
    }

    /// <summary>Normalizes conversion failures and gives probing callers a position-safe non-throwing path.</summary>
    [TestMethod]
    public void TypedReadFailures_AreDomainErrorsAndTryReadRestoresPosition()
    {
        var cstruct = new CStruct("struct root { uint16 value; uint16 *missing; };", pointerSize: 2);
        using var stream = new MemoryStream([0x34, 0x12, 0, 0,]) { Position = 0, };

        CStructReadException mismatch = Assert.Throws<CStructReadException>(
            () => cstruct.ReadValue<DateTime>(stream, "root.value"));
        Assert.AreEqual(CStructErrorCode.ReadFailed, mismatch.Code);
        Assert.AreEqual("root.value", mismatch.Path);

        stream.Position = 0;
        Assert.IsFalse(cstruct.TryReadValue<DateTime>(stream, "root.value", out DateTime _));
        Assert.AreEqual(0L, stream.Position);

        Assert.IsFalse(cstruct.TryReadValue<int>(stream, "root.unknown", out int _));
        Assert.AreEqual(0L, stream.Position);

        Assert.IsTrue(cstruct.TryReadValue<int>(stream, "root.value", out int value));
        Assert.AreEqual(0x1234, value);

        stream.Position = 0;
        Assert.IsNull(cstruct.ReadValue<int?>(stream, "root.missing.value"));
    }

    /// <summary>Uses the compiled endian, alignment, bitfield, union, and relative-pointer rules for selected values.</summary>
    [TestMethod]
    public void ReadValue_UsesSharedBinarySemanticsForExactTargets()
    {
        const string alignedLayout = """
                                     struct flag_parts { uint8 low:4; uint8 high:4; };
                                     union flags { flag_parts split; uint8 all; };
                                     struct root {
                                         uint8 prefix;
                                         uint16 number;
                                         uint16 first:3;
                                         uint16 center:5;
                                         flags data;
                                     };
                                     """;
        byte[] alignedBytes =
        [
            0x7F,
            0,
            0x12, 0x34,
            0, 0x52,
            0xA5,
            0,
        ];
        var aligned = new CStruct(
            alignedLayout,
            pointerSize: 2,
            aligned: true,
            isLittleEndian: false);

        using (var stream = new MemoryStream(alignedBytes))
        {
            Assert.AreEqual((ushort)0x1234, aligned.ReadValue(stream, "root.number"));
        }

        using (var stream = new MemoryStream(alignedBytes))
        {
            Assert.AreEqual(0x0A, aligned.ReadValue(stream, "root.center"));
        }

        using (var stream = new MemoryStream(alignedBytes))
        {
            Assert.AreEqual(0x0A, aligned.ReadValue(stream, "root.data.split.high"));
        }

        const string pointerLayout = "struct root { uint16 *target; };";
        byte[] pointerBytes = [0, 2, 0, 0, 0xBE, 0xEF,];
        var pointer = new CStruct(pointerLayout, pointerSize: 2, isLittleEndian: false);
        var options = new ReadOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = 2,
        };

        using (var stream = new MemoryStream(pointerBytes))
        {
            Assert.AreEqual(2L, pointer.ReadValue(stream, "root.target.address", options: options));
        }

        using (var stream = new MemoryStream(pointerBytes))
        {
            Assert.AreEqual((ushort)0xBEEF, pointer.ReadValue(stream, "root.target.value", options: options));
        }
    }

    /// <summary>Supports scalar declarations, common list targets, fields, and position-safe default-root probing.</summary>
    [TestMethod]
    public void TypedRead_HandlesScalarRootsCollectionsFieldsAndDefaultTry()
    {
        var scalar = new CStruct("typedef uint16 word;");
        using (var stream = new MemoryStream([0x34, 0x12,]))
        {
            Assert.AreEqual(0x1234, scalar.ReadValue<int>(stream, "word"));
        }

        const string layout = """
                              struct item { uint16 value; };
                              struct root { byte count; item items[count]; };
                              """;
        var cstruct = new CStruct(layout);
        using (var stream = new MemoryStream([2, 0x34, 0x12, 0x78, 0x56,]))
        {
            IReadOnlyList<ItemFields> items =
                cstruct.ReadValue<IReadOnlyList<ItemFields>>(stream, "root.items");
            Assert.AreEqual(2, items.Count);
            Assert.AreEqual(0x1234, items[0].Value);
            Assert.AreEqual(0x5678, items[1].Value);
        }

        var singleRoot = new CStruct("struct root { uint16 value; };");
        using (var stream = new MemoryStream([0xCD, 0xAB,]))
        {
            Assert.IsTrue(singleRoot.TryReadValue(stream, out ItemFields? root));
            Assert.IsNotNull(root);
            Assert.AreEqual(0xABCD, root.Value);
        }
    }

    /// <summary>Rejects overflow, missing POCO members, and ambiguous case folding with precise conversion paths.</summary>
    [TestMethod]
    public void TypedRead_RejectsUnsafeOrAmbiguousMappings()
    {
        var cstruct = new CStruct("struct root { uint16 value; };");

        using (var stream = new MemoryStream([0, 1,]))
        {
            CStructReadException overflow = Assert.Throws<CStructReadException>(
                () => cstruct.ReadValue<ByteValue>(stream, "root"));
            Assert.AreEqual("root.value", overflow.Path);
        }

        using (var stream = new MemoryStream([1, 0,]))
        {
            CStructReadException missing = Assert.Throws<CStructReadException>(
                () => cstruct.ReadValue<MissingMember>(stream, "root"));
            Assert.AreEqual("root", missing.Path);
            StringAssert.Contains(missing.Message, "source member 'Other' is missing");
        }

        var ambiguous = new CStruct("struct root { byte value; byte Value; };");
        using (var stream = new MemoryStream([1, 2,]))
        {
            CStructReadException error = Assert.Throws<CStructReadException>(
                () => ambiguous.ReadValue<UpperValue>(stream, "root"));
            Assert.AreEqual("root", error.Path);
            StringAssert.Contains(error.Message, "ambiguous");
        }
    }

    /// <summary>Reads character buffers, terminated strings, inline objects, and complete multi-pointer chains.</summary>
    [TestMethod]
    public void ReadValue_HandlesStringsInlineStructsAndMultiPointers()
    {
        const string stringLayout = """
                                    struct root {
                                        char fixed[2];
                                        cstring terminated;
                                        struct { uint16 value; } nested;
                                    };
                                    """;
        var strings = new CStruct(stringLayout);
        using (var stream = new MemoryStream([.. "AZHi\0"u8.ToArray(), 0x34, 0x12,]))
        {
            Assert.AreEqual("AZ", strings.ReadValue<string>(stream, "root.fixed"));
        }

        using (var stream = new MemoryStream([.. "AZHi\0"u8.ToArray(), 0x34, 0x12,]))
        {
            Assert.AreEqual("Hi", strings.ReadValue<string>(stream, "root.terminated"));
        }

        using (var stream = new MemoryStream([.. "AZHi\0"u8.ToArray(), 0x34, 0x12,]))
        {
            ItemFields nested = strings.ReadValue<ItemFields>(stream, "root.nested");
            Assert.AreEqual(0x1234, nested.Value);
        }

        using (var stream = new MemoryStream([.. "AZHi\0"u8.ToArray(), 0x34, 0x12,]))
        {
            Assert.AreEqual(5L, strings.ResolveAddress(stream, "root.nested"));
            Assert.AreEqual(0L, stream.Position);
        }

        var pointers = new CStruct("struct root { uint8 **target; };", pointerSize: 1);
        using (var stream = new MemoryStream([1, 2, 0x7F,]))
        {
            Assert.AreEqual((byte)0x7F, pointers.ReadValue<byte>(stream, "root.target.value.value"));
        }

        using (var stream = new MemoryStream([1, 2, 0x7F,]))
        {
            var pointer = pointers.ReadValue<Pointer>(stream, "root.target");
            Assert.AreEqual(1L, pointer.Address);
            Assert.AreEqual(2L, pointer.Next!.Address);
            Assert.AreEqual((byte)0x7F, pointer.Next.Value);
        }
    }

    /// <summary>Applies read budgets to selected values and restores failed probe positions.</summary>
    [TestMethod]
    public void ReadValue_EnforcesReadBudgets()
    {
        var cstruct = new CStruct("struct root { uint16 value; };");
        var options = new ReadOptions { MaxTotalBytesRead = 1, };
        using var stream = new MemoryStream([0x34, 0x12,]);

        CStructReadLimitException error = Assert.Throws<CStructReadLimitException>(
            () => cstruct.ReadValue(stream, "root.value", options: options));
        Assert.AreEqual(CStructErrorCode.ReadLimitExceeded, error.Code);
        Assert.AreEqual("root.value", error.Path);

        stream.Position = 0;
        Assert.IsFalse(cstruct.TryReadValue<int>(stream, "root.value", out _, options: options));
        Assert.AreEqual(0L, stream.Position);
    }

    private sealed class ChildModel
    {
        public int Value { get; set; }
    }

    private sealed class RootModel
    {
        public int Count { get; set; }

        public ChildModel[] Children { get; set; } = [];

        public Mode State { get; set; }

        public Pointer Optional { get; set; } = null!;
    }

    private sealed class ItemFields
    {
#pragma warning disable SA1401 // This fixture intentionally verifies public-field POCO mapping.
        public int Value = -1;
#pragma warning restore SA1401
    }

    private sealed class ByteValue
    {
        public byte Value { get; set; }
    }

    private sealed class MissingMember
    {
        public int Other { get; set; }
    }

    private sealed class UpperValue
    {
        public byte VALUE { get; set; }
    }
}
