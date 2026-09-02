namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>Verifies semantic element selection and pointer-level updates across the COR-03 operation matrix.</summary>
[TestClass]
public class IndexedPointerUpdateTests
{
    /// <summary>
    ///     Keeps parse, debug, address, selected serialization, and update on the same primitive array element across
    ///     alignment and byte-order modes.
    /// </summary>
    /// <param name="aligned">Whether the declared fields use portable alignment.</param>
    /// <param name="isLittleEndian">Whether least-significant bytes are stored first.</param>
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(true, false)]
    public void PrimitiveArrayElement_OperationsAgree(bool aligned, bool isLittleEndian)
    {
        const string layout = "struct root { uint8 prefix; uint16 items[3]; uint8 tail; };";
        var cstruct = new CStruct(
            layout,
            pointerSize: 1,
            aligned: aligned,
            isLittleEndian: isLittleEndian);
        int itemsStart = aligned ? 2 : 1;
        int tailAddress = itemsStart + 6;
        byte[] bytes = new byte[cstruct.GetStructSizeInBytes("root")];
        bytes[0] = 0xEE;
        WriteUnsigned(bytes, itemsStart, 2, 0x1111, isLittleEndian);
        WriteUnsigned(bytes, itemsStart + 2, 2, 0x2222, isLittleEndian);
        WriteUnsigned(bytes, itemsStart + 4, 2, 0x3333, isLittleEndian);
        bytes[tailAddress] = 0x7E;
        using var stream = new MemoryStream(bytes);

        dynamic parsed = cstruct.ParseStream(stream, "root");
        Assert.AreEqual((ushort)0x2222, (ushort)parsed.items[1]);
        Assert.AreEqual((byte)0x7E, (byte)parsed.tail);

        stream.Position = 0;
        Assert.AreEqual(itemsStart + 2, cstruct.ResolveAddress(stream, "root.items[1]"));
        stream.Position = 0;
        (List<DebugData> debug, _) = cstruct.ParseStreamWithDebug(stream, "root");
        Assert.IsTrue(debug.Any(item => item.CurPos == itemsStart + 2 && item.EndPos == itemsStart + 4));

        byte[] selected = cstruct.Serialize("root.items[1]", (ushort)0xABCD);
        byte[] expectedSelected = new byte[2];
        WriteUnsigned(expectedSelected, 0, 2, 0xABCD, isLittleEndian);
        CollectionAssert.AreEqual(expectedSelected, selected);
        byte[] originalSelected = new byte[2];
        WriteUnsigned(originalSelected, 0, 2, 0x2222, isLittleEndian);
        CollectionAssert.AreEqual(
            originalSelected,
            cstruct.Serialize("root.items[1]", parsed));
        CollectionAssert.AreEqual(bytes, cstruct.Serialize("root", parsed));

        stream.Position = 0;
        cstruct.UpdateStream(stream, "root.items[1]", (ushort)0xABCD);

        byte[] expected = (byte[])bytes.Clone();
        WriteUnsigned(expected, itemsStart + 2, 2, 0xABCD, isLittleEndian);
        CollectionAssert.AreEqual(expected, stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>Serializes one selected fixed-array item through its element codec instead of the collection writer.</summary>
    [TestMethod]
    public void Serialize_ArrayElement_UsesSelectedElementShape()
    {
        var cstruct = new CStruct("struct root { uint16 items[3]; };", pointerSize: 1);

        byte[] bytes = cstruct.Serialize("root.items[1]", (ushort)0xABCD);

        CollectionAssert.AreEqual(new byte[] { 0xCD, 0xAB, }, bytes);
    }

    /// <summary>Rejects the first index beyond a fixed array before selected serialization creates output.</summary>
    [TestMethod]
    public void Serialize_ArrayElement_RejectsIndexAtDeclaredLength()
    {
        var cstruct = new CStruct("struct root { uint16 items[2]; };", pointerSize: 1);

        Assert.Throws<CStructPathException>(
            () => cstruct.Serialize("root.items[2]", (ushort)0x1234));
    }

    /// <summary>Writes a primitive pointer target at its exact stored address even when aligned mode is enabled.</summary>
    [TestMethod]
    public void UpdateStream_AlignedPointerTarget_UsesExactResolvedAddress()
    {
        var cstruct = new CStruct(
            "struct root { uint16 *ptr; uint8 tail; };",
            pointerSize: 1,
            aligned: true);
        using var stream = new MemoryStream(new byte[] { 0x03, 0xEE, 0xA5, 0x34, 0x12, 0x7E, });

        cstruct.UpdateStream(stream, "root.ptr.value", (ushort)0xBEEF);

        CollectionAssert.AreEqual(new byte[] { 0x03, 0xEE, 0xA5, 0xEF, 0xBE, 0x7E, }, stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>Rejects relative-address subtraction overflow before pointer storage or stream position changes.</summary>
    [TestMethod]
    public void UpdateStream_RelativePointerAddressOverflow_LeavesStreamUntouched()
    {
        var cstruct = new CStruct("struct root { uint8 *ptr; };", pointerSize: 8);
        byte[] original = Enumerable.Repeat((byte)0xA5, 8).ToArray();
        using var stream = new MemoryStream((byte[])original.Clone());
        var options = new UpdateOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = 1,
        };

        Assert.Throws<CStructWriteException>(
            () => cstruct.UpdateStream(stream, "root.ptr.address", long.MinValue, options: options));
        Assert.Throws<CStructWriteException>(
            () => cstruct.UpdateStream(stream, "root.ptr.address", 1, options: options));

        CollectionAssert.AreEqual(original, stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>Uses scalar enum and character codecs when one item is selected from their fixed arrays.</summary>
    [TestMethod]
    public void EnumAndCharacterArrayElements_UseTheirElementCodecs()
    {
        const string enumLayout = """
                                  enum mode : uint16 { One=1, Two=2 };
                                  struct root { mode values[2]; uint8 tail; };
                                  """;
        var enumStruct = new CStruct(enumLayout, pointerSize: 1, isLittleEndian: false);
        using var enumStream = new MemoryStream(new byte[] { 0x00, 0x01, 0x00, 0x01, 0x7E, });

        enumStruct.UpdateStream(enumStream, "root.values[1]", "Two");

        CollectionAssert.AreEqual(new byte[] { 0x00, 0x01, 0x00, 0x02, 0x7E, }, enumStream.ToArray());
        CollectionAssert.AreEqual(
            new byte[] { 0x00, 0x02, },
            enumStruct.Serialize("root.values[1]", "Two"));

        var characters = new CStruct("struct root { char values[3]; uint8 tail; };", pointerSize: 1);
        using var characterStream = new MemoryStream(new byte[] { (byte)'A', (byte)'B', (byte)'C', 0x7E, });

        characters.UpdateStream(characterStream, "root.values[1]", 'Z');

        CollectionAssert.AreEqual(
            new byte[] { (byte)'A', (byte)'Z', (byte)'C', 0x7E, },
            characterStream.ToArray());
        CollectionAssert.AreEqual(new byte[] { (byte)'Q', }, characters.Serialize("root.values[0]", 'Q'));
        characterStream.Position = 0;
        dynamic parsedCharacters = characters.ParseStream(characterStream, "root");
        Assert.AreEqual("AZC", (string)parsedCharacters.values);
    }

    /// <summary>Updates one nested struct and one union array item without changing sibling elements or sentinels.</summary>
    [TestMethod]
    public void CompositeArrayElements_UpdateOnlyTheirSelectedExtent()
    {
        const string structLayout = """
                                    struct item { uint8 code; uint16 value; };
                                    struct root { uint8 prefix; item items[2]; uint8 tail; };
                                    """;
        var nested = new CStruct(structLayout, pointerSize: 1, aligned: true);
        dynamic original = new ExpandoObject();
        original.prefix = (byte)0xEE;
        original.items = new List<object>
        {
            CreateItem(0x11, 0x1111),
            CreateItem(0x22, 0x2222),
        };
        original.tail = (byte)0x7E;
        byte[] nestedBytes = nested.Serialize("root", original);
        dynamic replacement = CreateItem(0xAA, 0xBEEF);
        using var nestedStream = new MemoryStream((byte[])nestedBytes.Clone());
        nestedStream.Position = 0;
        long nestedAddress = nested.ResolveAddress(nestedStream, "root.items[1]");

        nestedStream.Position = 0;
        dynamic selected = nested.ParseStream(nestedStream, "root.items[1]");
        Assert.AreEqual((byte)0x22, (byte)selected.code);
        nestedStream.Position = 0;
        (List<DebugData> nestedDebug, _) = nested.ParseStreamWithDebug(nestedStream, "root.items[1]");
        Assert.IsTrue(nestedDebug.Any(item => item.CurPos == nestedAddress));

        nestedStream.Position = 0;
        nested.UpdateStream(nestedStream, "root.items[1]", replacement);

        byte[] expectedNested = (byte[])nestedBytes.Clone();
        byte[] serializedReplacement = nested.Serialize("item", replacement);
        Array.Copy(serializedReplacement, 0, expectedNested, nestedAddress, serializedReplacement.Length);
        CollectionAssert.AreEqual(expectedNested, nestedStream.ToArray());

        const string unionLayout = """
                                   union choice { uint16 wide; uint8 small; };
                                   struct root { choice values[2]; uint8 tail; };
                                   """;
        var unions = new CStruct(unionLayout, pointerSize: 1);
        using var unionStream = new MemoryStream(new byte[] { 0x11, 0x11, 0x22, 0x22, 0x7E, });
        UnionValue unionValue = UnionValue.FromMember("choice", "small", (byte)0xA5);

        unions.UpdateStream(unionStream, "root.values[1]", unionValue);

        CollectionAssert.AreEqual(new byte[] { 0x11, 0x11, 0xA5, 0x00, 0x7E, }, unionStream.ToArray());
    }

    /// <summary>Retains collection shape for whole-array writes while validating selected indexes before output.</summary>
    [TestMethod]
    public void WholeArrayWrites_PreserveShapeRules()
    {
        var cstruct = new CStruct("struct root { uint16 items[2]; uint8 tail; };", pointerSize: 1);
        byte[] original = new byte[] { 0x11, 0x11, 0x22, 0x22, 0x7E, };
        using var stream = new MemoryStream((byte[])original.Clone());

        cstruct.UpdateStream(stream, "root.items", new ushort[] { 0xAAAA, 0xBBBB, });

        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xAA, 0xBB, 0xBB, 0x7E, }, stream.ToArray());
        Assert.Throws<CStructWriteException>(
            () => cstruct.UpdateStream(stream, "root.items", new ushort[] { 0x1111, }));
        CollectionAssert.AreEqual(new byte[] { 0xAA, 0xAA, 0xBB, 0xBB, 0x7E, }, stream.ToArray());

        Assert.Throws<CStructPathException>(
            () => cstruct.UpdateStream(stream, "root.items[2]", (ushort)0x1234));
        Assert.Throws<CStructPathException>(
            () => cstruct.Serialize("root.items[2]", (ushort)0x1234));
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>Updates pointer-array storage and one selected pointee without treating either as the whole collection.</summary>
    [TestMethod]
    public void PointerArrayElements_DistinguishStorageFromTarget()
    {
        var cstruct = new CStruct(
            "struct root { uint16 *values[2]; uint8 tail; };",
            pointerSize: 2,
            isLittleEndian: false);
        byte[] bytes = Enumerable.Repeat((byte)0xA5, 16).ToArray();
        WriteUnsigned(bytes, 0, 2, 8, false);
        WriteUnsigned(bytes, 2, 2, 10, false);
        bytes[4] = 0x7E;
        WriteUnsigned(bytes, 8, 2, 0x1111, false);
        WriteUnsigned(bytes, 10, 2, 0x2222, false);

        using var storage = new MemoryStream((byte[])bytes.Clone());
        cstruct.UpdateStream(storage, "root.values[1]", 12);
        byte[] expectedStorage = (byte[])bytes.Clone();
        WriteUnsigned(expectedStorage, 2, 2, 12, false);
        CollectionAssert.AreEqual(expectedStorage, storage.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x00, 0x0C, }, cstruct.Serialize("root.values[1]", 12));

        using var target = new MemoryStream((byte[])bytes.Clone());
        Assert.AreEqual(10L, cstruct.ResolveAddress(target, "root.values[1].value"));
        target.Position = 0;
        cstruct.UpdateStream(target, "root.values[1].value", (ushort)0xBEEF);
        byte[] expectedTarget = (byte[])bytes.Clone();
        WriteUnsigned(expectedTarget, 10, 2, 0xBEEF, false);
        CollectionAssert.AreEqual(expectedTarget, target.ToArray());
    }

    /// <summary>Handles zero- and one-length array boundaries consistently for address, serialization, and update.</summary>
    [TestMethod]
    public void ArrayElementSelection_HandlesZeroAndOneLengths()
    {
        var cstruct = new CStruct(
            "struct root { uint16 empty[0]; uint16 one[1]; uint8 tail; };",
            pointerSize: 1);
        using var stream = new MemoryStream(new byte[] { 0x11, 0x11, 0x7E, });

        Assert.AreEqual(0L, cstruct.ResolveAddress(stream, "root.one[0]"));
        cstruct.UpdateStream(stream, "root.one[0]", (ushort)0xBEEF);
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBE, 0x7E, }, stream.ToArray());
        CollectionAssert.AreEqual(new byte[] { 0x34, 0x12, }, cstruct.Serialize("root.one[0]", (ushort)0x1234));

        Assert.Throws<CStructPathException>(
            () => cstruct.UpdateStream(stream, "root.empty[0]", (ushort)0x1234));
        Assert.Throws<CStructPathException>(
            () => cstruct.Serialize("root.empty[0]", (ushort)0x1234));
        CollectionAssert.AreEqual(new byte[] { 0xEF, 0xBE, 0x7E, }, stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>
    ///     Resolves and updates root storage, intermediate storage, and the final primitive across every pointer width
    ///     and both byte orders.
    /// </summary>
    /// <param name="pointerSize">The encoded address width.</param>
    /// <param name="isLittleEndian">Whether least-significant address and value bytes are stored first.</param>
    [TestMethod]
    [DataRow(1, true)]
    [DataRow(1, false)]
    [DataRow(2, true)]
    [DataRow(2, false)]
    [DataRow(4, true)]
    [DataRow(4, false)]
    [DataRow(8, true)]
    [DataRow(8, false)]
    public void MultiLevelPointerTargets_AgreeAcrossWidthsAndEndianness(int pointerSize, bool isLittleEndian)
    {
        const int rootStart = 1;
        int firstTarget = rootStart + pointerSize + 5;
        int finalTarget = firstTarget + pointerSize + 5;
        int alternateTarget = finalTarget + 2;
        byte[] bytes = Enumerable.Repeat((byte)0xA5, alternateTarget + 3).ToArray();
        WriteUnsigned(bytes, rootStart, pointerSize, (ulong)firstTarget, isLittleEndian);
        bytes[rootStart + pointerSize] = 0x7E;
        WriteUnsigned(bytes, firstTarget, pointerSize, (ulong)finalTarget, isLittleEndian);
        WriteUnsigned(bytes, finalTarget, 2, 0x1234, isLittleEndian);
        var cstruct = new CStruct(
            "struct root { uint16 **ptr; uint8 tail; };",
            pointerSize: (byte)pointerSize,
            isLittleEndian: isLittleEndian);
        using var parseStream = new MemoryStream((byte[])bytes.Clone()) { Position = rootStart, };

        dynamic parsed = cstruct.ParseStream(parseStream, "root");
        var outer = (Pointer)parsed.ptr;
        Assert.AreEqual(firstTarget, outer.Address);
        Assert.AreEqual(finalTarget, outer.Next!.Address);
        Assert.AreEqual((ushort)0x1234, (ushort)outer.Next.Value!);
        Assert.AreEqual((byte)0x7E, (byte)parsed.tail);

        parseStream.Position = rootStart;
        (List<DebugData> debug, _) = cstruct.ParseStreamWithDebug(parseStream, "root");
        Assert.IsTrue(debug.Any(item => item.CurPos == rootStart && item.EndPos == rootStart + pointerSize));
        foreach ((string path, long address) in new[]
                 {
                     ("root.ptr.address", (long)rootStart),
                     ("root.ptr.value", (long)firstTarget),
                     ("root.ptr.value.address", (long)firstTarget),
                     ("root.ptr.value.value", (long)finalTarget),
                 })
        {
            parseStream.Position = rootStart;
            Assert.AreEqual(address, cstruct.ResolveAddress(parseStream, path), path);
            Assert.AreEqual(rootStart, parseStream.Position, path);
        }

        using var implicitIntermediate = new MemoryStream((byte[])bytes.Clone()) { Position = rootStart, };
        cstruct.UpdateStream(implicitIntermediate, "root.ptr.value", alternateTarget);
        byte[] expectedIntermediate = (byte[])bytes.Clone();
        WriteUnsigned(expectedIntermediate, firstTarget, pointerSize, (ulong)alternateTarget, isLittleEndian);
        CollectionAssert.AreEqual(expectedIntermediate, implicitIntermediate.ToArray());
        Assert.AreEqual(rootStart, implicitIntermediate.Position);

        using var explicitIntermediate = new MemoryStream((byte[])bytes.Clone()) { Position = rootStart, };
        cstruct.UpdateStream(explicitIntermediate, "root.ptr.value.address", alternateTarget);
        CollectionAssert.AreEqual(expectedIntermediate, explicitIntermediate.ToArray());

        using var finalValue = new MemoryStream((byte[])bytes.Clone()) { Position = rootStart, };
        cstruct.UpdateStream(finalValue, "root.ptr.value.value", (ushort)0xBEEF);
        byte[] expectedFinal = (byte[])bytes.Clone();
        WriteUnsigned(expectedFinal, finalTarget, 2, 0xBEEF, isLittleEndian);
        CollectionAssert.AreEqual(expectedFinal, finalValue.ToArray());
        Assert.AreEqual(rootStart, finalValue.Position);

        finalValue.Position = rootStart;
        dynamic reparsed = cstruct.ParseStream(finalValue, "root");
        Assert.AreEqual((ushort)0xBEEF, (ushort)((Pointer)reparsed.ptr).Next!.Value!);
    }

    /// <summary>Applies the same relative origin at every pointer level for resolution and pointer-storage updates.</summary>
    /// <param name="isLittleEndian">Whether least-significant address bytes are stored first.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void RelativePointerTargets_UseTheConfiguredOriginAtEveryLevel(bool isLittleEndian)
    {
        const int origin = 10;
        const int rootStart = 1;
        const int firstTarget = 20;
        const int finalTarget = 30;
        const int alternateTarget = 40;
        byte[] bytes = Enumerable.Repeat((byte)0xA5, 44).ToArray();
        WriteUnsigned(bytes, rootStart, 2, firstTarget - origin, isLittleEndian);
        WriteUnsigned(bytes, firstTarget, 2, finalTarget - origin, isLittleEndian);
        WriteUnsigned(bytes, finalTarget, 2, 0x1234, isLittleEndian);
        var cstruct = new CStruct(
            "struct root { uint16 **ptr; };",
            pointerSize: 2,
            isLittleEndian: isLittleEndian);
        var readOptions = new ReadOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = origin,
        };
        var updateOptions = new UpdateOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = origin,
        };
        using var stream = new MemoryStream((byte[])bytes.Clone()) { Position = rootStart, };

        Assert.AreEqual(
            finalTarget,
            cstruct.ResolveAddress(stream, "root.ptr.value.value", options: readOptions));
        Assert.AreEqual(rootStart, stream.Position);

        cstruct.UpdateStream(stream, "root.ptr.value", alternateTarget, options: updateOptions);

        byte[] expected = (byte[])bytes.Clone();
        WriteUnsigned(expected, firstTarget, 2, alternateTarget - origin, isLittleEndian);
        CollectionAssert.AreEqual(expected, stream.ToArray());
        Assert.AreEqual(rootStart, stream.Position);

        using var rootAddress = new MemoryStream((byte[])bytes.Clone()) { Position = rootStart, };
        cstruct.UpdateStream(rootAddress, "root.ptr.address", alternateTarget, options: updateOptions);
        byte[] expectedRoot = (byte[])bytes.Clone();
        WriteUnsigned(expectedRoot, rootStart, 2, alternateTarget - origin, isLittleEndian);
        CollectionAssert.AreEqual(expectedRoot, rootAddress.ToArray());
    }

    /// <summary>Uses each final target's enum, struct, union, or terminated-string codec after pointer traversal.</summary>
    [TestMethod]
    public void PointerTargets_UseTheirFinalDeclaredCodecs()
    {
        const string enumLayout = """
                                  enum mode : uint16 { One=1, Two=2 };
                                  struct root { mode *ptr; uint8 tail; };
                                  """;
        var enumStruct = new CStruct(enumLayout, pointerSize: 1, isLittleEndian: false);
        using var enumStream = new MemoryStream(new byte[] { 0x03, 0x7E, 0xA5, 0x00, 0x01, });
        enumStruct.UpdateStream(enumStream, "root.ptr.value", "Two");
        CollectionAssert.AreEqual(new byte[] { 0x03, 0x7E, 0xA5, 0x00, 0x02, }, enumStream.ToArray());
        enumStream.Position = 0;
        dynamic parsedEnum = enumStruct.ParseStream(enumStream, "root");
        Assert.AreEqual(2, ((EnumValueResult)((Pointer)parsedEnum.ptr).Value!).Value);

        const string structLayout = """
                                    struct item { uint8 code; uint16 value; };
                                    struct root { item *ptr; uint8 tail; };
                                    """;
        var structs = new CStruct(structLayout, pointerSize: 1);
        using var structStream = new MemoryStream(new byte[] { 0x03, 0x7E, 0xA5, 0x11, 0x22, 0x22, });
        dynamic structReplacement = CreateItem(0xAA, 0xBEEF);
        dynamic selectedStruct = structs.ParseStream(structStream, "root.ptr.value");
        Assert.AreEqual((byte)0x11, (byte)selectedStruct.code);
        structStream.Position = 0;
        (List<DebugData> structDebug, _) = structs.ParseStreamWithDebug(structStream, "root.ptr.value");
        Assert.IsTrue(structDebug.Any(item => item.CurPos == 3));
        structStream.Position = 0;
        structs.UpdateStream(structStream, "root.ptr.value", structReplacement);
        CollectionAssert.AreEqual(
            new byte[] { 0x03, 0x7E, 0xA5, 0xAA, 0xEF, 0xBE, },
            structStream.ToArray());

        const string unionLayout = """
                                   union choice { uint16 wide; uint8 small; };
                                   struct root { choice *ptr; uint8 tail; };
                                   """;
        var unions = new CStruct(unionLayout, pointerSize: 1);
        using var unionStream = new MemoryStream(new byte[] { 0x03, 0x7E, 0xA5, 0x34, 0x12, });
        dynamic selectedUnion = unions.ParseStream(unionStream, "root.ptr.value");
        Assert.AreEqual((ushort)0x1234, (ushort)selectedUnion.wide);
        unionStream.Position = 0;
        (List<DebugData> unionDebug, _) = unions.ParseStreamWithDebug(unionStream, "root.ptr.value");
        Assert.IsTrue(unionDebug.All(item => item.CurPos == 3));
        UnionValue unionReplacement = UnionValue.FromMember("choice", "small", (byte)0x11);
        unionStream.Position = 0;
        unions.UpdateStream(unionStream, "root.ptr.value", unionReplacement);
        CollectionAssert.AreEqual(new byte[] { 0x03, 0x7E, 0xA5, 0x11, 0x00, }, unionStream.ToArray());

        var strings = new CStruct("struct root { char **name; uint8 tail; };", pointerSize: 1);
        using var stringStream = new MemoryStream(
            new byte[] { 0x03, 0x7E, 0xA5, 0x05, 0xA5, (byte)'o', (byte)'l', (byte)'d', 0x00, });
        strings.UpdateStream(stringStream, "root.name.value.value", "hi");
        CollectionAssert.AreEqual(
            new byte[] { 0x03, 0x7E, 0xA5, 0x05, 0xA5, (byte)'h', (byte)'i', 0x00, 0x00, },
            stringStream.ToArray());
        stringStream.Position = 0;
        dynamic parsedString = strings.ParseStream(stringStream, "root");
        Assert.AreEqual("hi", (string)((Pointer)parsedString.name).Next!.Value!);
    }

    /// <summary>Applies null policy at the selected pointer level and never traverses beyond an intermediate null.</summary>
    [TestMethod]
    public void NullPointerUpdates_ApplyPolicyAtTheSelectedLevel()
    {
        var oneLevel = new CStruct("struct root { uint8 *ptr; };", pointerSize: 1);
        using var finalNull = new MemoryStream(new byte[] { 0x00, 0xA5, });
        Assert.Throws<CStructReadException>(
            () => oneLevel.UpdateStream(finalNull, "root.ptr.value", (byte)0x11));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xA5, }, finalNull.ToArray());

        oneLevel.UpdateStream(
            finalNull,
            "root.ptr.value",
            (byte)0x11,
            options: new UpdateOptions { RequireExistingPointerTarget = false, });
        CollectionAssert.AreEqual(new byte[] { 0x11, 0xA5, }, finalNull.ToArray());

        var twoLevels = new CStruct("struct root { uint8 **ptr; };", pointerSize: 1);
        using var secondNull = new MemoryStream(new byte[] { 0x02, 0xA5, 0x00, 0xA5, });
        Assert.Throws<CStructReadException>(
            () => twoLevels.UpdateStream(secondNull, "root.ptr.value.value", (byte)0x11));
        CollectionAssert.AreEqual(new byte[] { 0x02, 0xA5, 0x00, 0xA5, }, secondNull.ToArray());

        twoLevels.UpdateStream(secondNull, "root.ptr.value", 3);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0xA5, 0x03, 0xA5, }, secondNull.ToArray());

        using var firstNull = new MemoryStream(new byte[] { 0x00, 0xA5, 0xA5, });
        Assert.Throws<CStructPathException>(
            () => twoLevels.UpdateStream(
                firstNull,
                "root.ptr.value.value",
                (byte)0x11,
                options: new UpdateOptions { RequireExistingPointerTarget = false, }));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xA5, 0xA5, }, firstNull.ToArray());

        var relativeOptions = new UpdateOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = 10,
        };
        using var relativeNull = new MemoryStream(new byte[] { 0x00, 0xA5, });
        Assert.Throws<CStructReadException>(
            () => oneLevel.UpdateStream(
                relativeNull,
                "root.ptr.value",
                (byte)0x11,
                options: relativeOptions));
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xA5, }, relativeNull.ToArray());
    }

    /// <summary>Stores a null address at root and intermediate pointer levels, including relative layouts.</summary>
    [TestMethod]
    public void PointerAddressUpdates_CanStoreNullAtEveryLevel()
    {
        var cstruct = new CStruct("struct root { uint8 **ptr; };", pointerSize: 1);
        byte[] bytes = new byte[] { 0x02, 0xA5, 0x04, 0xA5, 0x11, };

        using var rootStorage = new MemoryStream((byte[])bytes.Clone());
        cstruct.UpdateStream(rootStorage, "root.ptr.address", 0);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xA5, 0x04, 0xA5, 0x11, }, rootStorage.ToArray());
        Assert.AreEqual(0, rootStorage.Position);

        using var intermediateStorage = new MemoryStream((byte[])bytes.Clone());
        cstruct.UpdateStream(intermediateStorage, "root.ptr.value.address", 0);
        CollectionAssert.AreEqual(new byte[] { 0x02, 0xA5, 0x00, 0xA5, 0x11, }, intermediateStorage.ToArray());
        Assert.AreEqual(0, intermediateStorage.Position);

        var relativeOptions = new UpdateOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            Origin = 10,
        };
        using var relativeStorage = new MemoryStream((byte[])bytes.Clone());
        cstruct.UpdateStream(relativeStorage, "root.ptr.address", 0, options: relativeOptions);
        CollectionAssert.AreEqual(new byte[] { 0x00, 0xA5, 0x04, 0xA5, 0x11, }, relativeStorage.ToArray());
        Assert.AreEqual(0, relativeStorage.Position);
    }

    /// <summary>Leaves bytes and caller position unchanged when selected element or pointer validation fails.</summary>
    [TestMethod]
    public void SelectedUpdateFailures_AreNonMutating()
    {
        var array = new CStruct("struct root { uint16 values[2]; };", pointerSize: 1);
        byte[] arrayBytes = new byte[] { 0x11, 0x11, 0x22, 0x22, };
        using var arrayStream = new MemoryStream((byte[])arrayBytes.Clone()) { Position = 1, };
        CStructWriteException conversion = Assert.Throws<CStructWriteException>(
            () => array.UpdateStream(arrayStream, "root.values[1]", -1));
        Assert.IsInstanceOfType<OverflowException>(conversion.InnerException);
        CollectionAssert.AreEqual(arrayBytes, arrayStream.ToArray());
        Assert.AreEqual(1, arrayStream.Position);

        var pointer = new CStruct("struct root { uint16 **ptr; };", pointerSize: 1);
        byte[] pointerBytes = new byte[] { 0x02, 0xA5, 0x04, 0xA5, 0x34, 0x12, };
        using var oversizedAddress = new MemoryStream((byte[])pointerBytes.Clone());
        Assert.Throws<CStructWriteException>(
            () => pointer.UpdateStream(oversizedAddress, "root.ptr.value", 256));
        CollectionAssert.AreEqual(pointerBytes, oversizedAddress.ToArray());
        Assert.AreEqual(0, oversizedAddress.Position);

        using var invalidTarget = new MemoryStream(
            new byte[] { 0x7F, 0xA5, 0xA5, 0xA5, 0xA5, 0xA5, });
        Assert.Throws<CStructReadException>(
            () => pointer.UpdateStream(invalidTarget, "root.ptr.value.value", (ushort)0xBEEF));
        CollectionAssert.AreEqual(
            new byte[] { 0x7F, 0xA5, 0xA5, 0xA5, 0xA5, 0xA5, },
            invalidTarget.ToArray());
        Assert.AreEqual(0, invalidTarget.Position);
    }

    /// <summary>Reads only pointer storage on the selected branch before writing the final target.</summary>
    [TestMethod]
    public void PointerTargetUpdate_DoesNotReadUnrelatedOrFinalStorage()
    {
        const string layout = "struct root { uint16 **selected; uint8 *unrelated; uint8 tail; };";
        byte[] bytes = new byte[] { 0x04, 0x7F, 0x7E, 0xA5, 0x08, 0xA5, 0xA5, 0xA5, 0x34, 0x12, };
        var cstruct = new CStruct(layout, pointerSize: 1);
        using var stream = new TrackingStream(bytes);

        cstruct.UpdateStream(stream, "root.selected.value.value", (ushort)0xBEEF);

        CollectionAssert.AreEqual(new long[] { 0, 4, }, stream.ReadStarts.ToArray());
        CollectionAssert.AreEqual(
            new byte[] { 0x04, 0x7F, 0x7E, 0xA5, 0x08, 0xA5, 0xA5, 0xA5, 0xEF, 0xBE, },
            stream.ToArray());
        Assert.AreEqual(0, stream.Position);
    }

    /// <summary>Creates a dynamic nested-struct value for array and pointer replacement tests.</summary>
    private static ExpandoObject CreateItem(byte code, ushort value)
    {
        dynamic item = new ExpandoObject();
        item.code = code;
        item.value = value;
        return item;
    }

    /// <summary>Writes an unsigned test value without using the production writer under test.</summary>
    private static void WriteUnsigned(byte[] target, int offset, int size, ulong value, bool isLittleEndian)
    {
        for (int index = 0; index < size; index++)
        {
            int destination = isLittleEndian ? offset + index : offset + size - index - 1;
            target[destination] = (byte)(value >> (index * 8));
        }
    }

    /// <summary>Records read start positions while retaining ordinary seekable in-memory stream behavior.</summary>
    private sealed class TrackingStream : Stream
    {
        private readonly MemoryStream inner;

        /// <summary>Initializes a new instance of the <see cref="TrackingStream"/> class.</summary>
        public TrackingStream(byte[] bytes)
        {
            this.inner = new MemoryStream(bytes, writable: true);
        }

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => this.inner.Length;

        public override long Position
        {
            get => this.inner.Position;
            set => this.inner.Position = value;
        }

        public List<long> ReadStarts { get; } = [];

        public override void Flush()
        {
            this.inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            this.ReadStarts.Add(this.inner.Position);
            return this.inner.Read(buffer, offset, count);
        }

        public override int Read(Span<byte> buffer)
        {
            this.ReadStarts.Add(this.inner.Position);
            return this.inner.Read(buffer);
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.inner.Write(buffer, offset, count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            this.inner.Write(buffer);
        }

        public byte[] ToArray()
        {
            return this.inner.ToArray();
        }
    }
}
