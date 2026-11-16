namespace CStructSharp.Tests;

using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Independent MSF/CodeView fixtures exercise native preparation without a debugger or publisher PDB dependency.</summary>
[TestClass]
public class MemoryPdbMetadataTests
{
    /// <summary>Target identities, logical segments and mixed-width fields survive preparation and real memory reads.</summary>
    [TestMethod]
    public void PreparedProfile_KeepsIdentitySymbolsAndMixedPointerStorage()
    {
        Fixture fixture = CreateFixture(BasicTypes());
        using var input = new MemoryStream(fixture.Bytes);
        input.Position = 17;
        var metadata = new PdbMetadata(input);
        Assert.AreEqual(17L, input.Position);
        Assert.AreEqual(new Guid("12345678-9abc-def0-1234-56789abcdef0"), metadata.Guid);
        Assert.AreEqual(7U, metadata.InformationAge);
        Assert.AreEqual(2U, metadata.DbiAge);
        Assert.AreEqual(0x1020UL, metadata.Symbols["OriginalData"]);
        Assert.AreEqual(0x1040UL, metadata.Symbols["TypedData"]);
        Assert.AreEqual(0x12345678UL, metadata.AbsoluteSymbols["absoluteGuard"]);
        Assert.IsFalse(metadata.Symbols.ContainsKey("absoluteGuard"));
        using var output = new MemoryStream();
        metadata.WriteIsf(output, "fixture.pdb");
        byte[] prepared = output.ToArray();
        using JsonDocument profile = JsonDocument.Parse(prepared);
        Assert.AreEqual(0x8664, profile.RootElement.GetProperty("metadata").GetProperty("windows").GetProperty("pdb").GetProperty("machine_type").GetInt32());
        var isf = new IsfMetadata(prepared);
        MetadataImportResult imported = isf.Import("record");
        var session = new MemorySession(imported.Schema);
        byte[] bytes = [0xD4, 0xC3, 0xB2, 0xA1, 0x78, 0x56, 0x34, 0x12, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,];
        var region = new MemoryRegion(new ByteArrayMemorySource("native-record", bytes), 0, bytes.Length);
        Assert.AreEqual(0xA1B2C3D4U, session.Read(region, imported.RootTypeId, "value"));
        Assert.AreEqual(new StoredPointer(0x12345678, 4), session.Read(region, imported.RootTypeId, "narrow"));
        Assert.AreEqual(new StoredPointer(0x0123456789ABCDEF, 8), session.Read(region, imported.RootTypeId, "wide"));
        CollectionAssert.AreEqual(bytes, session.Serialize(imported.RootTypeId, session.Read(region, imported.RootTypeId)));
        Assert.AreEqual(0x1040UL, isf.Symbols["TypedData"]);
    }

    /// <summary>Conventional native-pointer references decode with the DBI machine width independently of explicit narrow storage.</summary>
    /// <param name="machine">Original COFF machine identifier.</param>
    /// <param name="nativeWidth">Expected native address storage, in bytes.</param>
    [TestMethod]
    [DataRow(0x14C, 4)]
    [DataRow(0x8664, 8)]
    [DataRow(0xAA64, 8)]
    public void PreparedNativePointerBase_DecodesOriginalArchitectureWidth(int machine, int nativeWidth)
    {
        Fixture fixture = CreateFixture(BasicTypes());
        BinaryPrimitives.WriteUInt16LittleEndian(fixture.Bytes.AsSpan(fixture.StreamStarts[3] + 58), checked((ushort)machine));
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        using var output = new MemoryStream();
        metadata.WriteIsf(output, "fixture.pdb");
        JsonNode profile = JsonNode.Parse(output.ToArray())!;

        // An ordinary ISF consumer can reference the native pointer base instead of a CodeView-width alias.
        profile["user_types"]!["record"]!["fields"]!["wide"]!["type"]!["base"] = "pointer";
        var isf = new IsfMetadata(Encoding.UTF8.GetBytes(profile.ToJsonString()));
        MetadataImportResult imported = isf.Import("record");
        var session = new MemorySession(imported.Schema);
        byte[] bytes = [0xD4, 0xC3, 0xB2, 0xA1, 0x78, 0x56, 0x34, 0x12, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,];
        var region = new MemoryRegion(new ByteArrayMemorySource("native-pointer-consumer", bytes), 0, bytes.Length);
        ulong address = nativeWidth == 4 ? 0x89ABCDEFUL : 0x0123456789ABCDEFUL;

        Assert.AreEqual(new StoredPointer(address, nativeWidth), session.Read(region, imported.RootTypeId, "wide"));
        Assert.AreEqual(new StoredPointer(0x12345678, 4), session.Read(region, imported.RootTypeId, "narrow"));
    }

    /// <summary>Legacy flat-pointer kinds retain their own width when no size bits were written, including mixed-width arrays.</summary>
    /// <param name="machine">The independently recorded native machine, not the width of every pointer.</param>
    [TestMethod]
    [DataRow(0x14C)]
    [DataRow(0x8664)]
    public void LegacyFlatPointerKinds_DecodeImplicitWidthsAndArrayExtents(int machine)
    {
        byte[][] types =
        [

            // Older producers omit the separate size bits even though the flat kind defines four bytes.
            Record(0x1002, Payload(writer =>
            {
                writer.Write(0x75U);
                writer.Write(10U);
            })),

            // A near64 pointer stays eight bytes even inside an x86 database.
            Record(0x1002, Payload(writer =>
            {
                writer.Write(0x75U);
                writer.Write(12U);
            })),

            // Array element sizing must use that same implicit pointer representation.
            Record(0x1503, Payload(writer =>
            {
                writer.Write(0x1000U);
                writer.Write(0x75U);
                writer.Write((ushort)8);
                writer.Write((byte)0);
            })),
            Record(0x1203, [.. Member("narrow", 0x1000, 0), .. Member("wide", 0x1001, 4), .. Member("links", 0x1002, 12)]),
            Composite("record", 3, 0x1003, 20),
        ];
        Fixture fixture = CreateFixture(types, rootType: 0x1004);
        BinaryPrimitives.WriteUInt16LittleEndian(fixture.Bytes.AsSpan(fixture.StreamStarts[3] + 58), checked((ushort)machine));
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        using var output = new MemoryStream();
        metadata.WriteIsf(output, "fixture.pdb");
        var isf = new IsfMetadata(output.ToArray());
        MetadataImportResult imported = isf.Import("record");
        var session = new MemorySession(imported.Schema);
        byte[] bytes = [0x44, 0x33, 0x22, 0x11, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01, 0x88, 0x77, 0x66, 0x55, 0xCC, 0xBB, 0xAA, 0x99];
        var region = new MemoryRegion(new ByteArrayMemorySource("legacy-native-pointers", bytes), 0, bytes.Length);

        Assert.AreEqual(new StoredPointer(0x11223344, 4), session.Read(region, imported.RootTypeId, "narrow"));
        Assert.AreEqual(new StoredPointer(0x0123456789ABCDEF, 8), session.Read(region, imported.RootTypeId, "wide"));
        Assert.AreEqual(new StoredPointer(0x55667788, 4), session.Read(region, imported.RootTypeId, "links[0]"));
        Assert.AreEqual(new StoredPointer(0x99AABBCC, 4), session.Read(region, imported.RootTypeId, "links[1]"));
        CollectionAssert.AreEqual(bytes, session.Serialize(imported.RootTypeId, session.Read(region, imported.RootTypeId)));
    }

    /// <summary>Implicit legacy sizes do not admit contradictory widths, segmented storage or member-pointer modes.</summary>
    /// <param name="attributes">Original CodeView kind, mode and optional size bits.</param>
    [TestMethod]
    [DataRow(10U | (8U << 13))]
    [DataRow(12U | (4U << 13))]
    [DataRow(11U)]
    [DataRow(10U | (2U << 5))]
    public void LegacyFlatPointerKinds_RejectContradictoryOrNonflatRepresentations(uint attributes)
    {
        byte[][] types =
        [

            // Keep the conflicting attributes intact until ordinary descriptor validation.
            Record(0x1002, Payload(writer =>
            {
                writer.Write(0x75U);
                writer.Write(attributes);
            })),
            Record(0x1203, Member("value", 0x1000, 0)),
            Composite("record", 1, 0x1001, 8),
        ];
        Fixture fixture = CreateFixture(types, rootType: 0x1002);
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        using var output = new MemoryStream();

        // Invalid native storage must not become an apparently usable imported field.
        Assert.Throws<CStructLayoutException>(() => metadata.WriteIsf(output, "fixture.pdb"));
    }

    /// <summary>An explicit target supplies missing ABI information without changing the original DBI machine.</summary>
    /// <param name="targetMachine">Caller-declared target, independent of the host and zero original DBI field.</param>
    /// <param name="pointerSize">The resulting native pointer storage in bytes.</param>
    [TestMethod]
    [DataRow(0x14C, 4)]
    [DataRow(0x8664, 8)]
    public void LegacyUnknownMachine_RequiresExplicitTargetAndPreservesOriginalZero(int targetMachine, int pointerSize)
    {
        Fixture fixture = CreateFixture(BasicTypes());
        Put32(fixture.Bytes, fixture.StreamStarts[2], 19990903);
        BinaryPrimitives.WriteUInt16LittleEndian(fixture.Bytes.AsSpan(fixture.StreamStarts[3] + 58), 0);
        using var input = new MemoryStream(fixture.Bytes);
        input.Position = 17;
        var metadata = new PdbMetadata(input, targetMachine: checked((ushort)targetMachine));
        Assert.AreEqual(17L, input.Position);
        Assert.AreEqual((ushort)0, metadata.Machine);
        Assert.AreEqual(checked((ushort)targetMachine), metadata.TargetMachine);
        using var output = new MemoryStream();
        metadata.WriteIsf(output, "fixture.pdb");
        JsonNode profile = JsonNode.Parse(output.ToArray())!;
        Assert.AreEqual(0, profile["metadata"]!["windows"]!["pdb"]!["machine_type"]!.GetValue<int>());
        Assert.AreEqual(pointerSize, profile["base_types"]!["pointer"]!["size"]!.GetValue<int>());

        // An ordinary consumer can use the supplied native pointer base while retaining the unknown original machine.
        profile["user_types"]!["record"]!["fields"]!["wide"]!["type"]!["base"] = "pointer";
        var isf = new IsfMetadata(Encoding.UTF8.GetBytes(profile.ToJsonString()));
        MetadataImportResult imported = isf.Import("record");
        var session = new MemorySession(imported.Schema);
        byte[] bytes = [0xD4, 0xC3, 0xB2, 0xA1, 0x78, 0x56, 0x34, 0x12, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01];
        var region = new MemoryRegion(new ByteArrayMemorySource("explicit-legacy-abi", bytes), 0, bytes.Length);
        ulong address = pointerSize == 4 ? 0x89ABCDEFUL : 0x0123456789ABCDEFUL;

        Assert.AreEqual(new StoredPointer(address, pointerSize), session.Read(region, imported.RootTypeId, "wide"));
    }

    /// <summary>Unknown original architecture cannot silently adopt the analyzer's ABI.</summary>
    [TestMethod]
    public void LegacyUnknownMachine_WithoutExplicitTargetRejectsAndRestoresBorrowedInput()
    {
        Fixture fixture = CreateFixture(BasicTypes());
        BinaryPrimitives.WriteUInt16LittleEndian(fixture.Bytes.AsSpan(fixture.StreamStarts[3] + 58), 0);
        using var input = new MemoryStream(fixture.Bytes);
        input.Position = 17;

        // The database remains valid evidence, but it cannot choose a native ABI without target information.
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input));
        Assert.AreEqual(17L, input.Position);
        Assert.IsTrue(input.CanRead);
    }

    /// <summary>An explicit target cannot override contradictory recorded architecture, even at the same pointer width.</summary>
    /// <param name="machine">Original nonzero DBI machine.</param>
    /// <param name="targetMachine">Conflicting caller declaration.</param>
    [TestMethod]
    [DataRow(0x14C, 0x8664)]
    [DataRow(0x8664, 0x14C)]
    [DataRow(0xAA64, 0x8664)]
    public void ExplicitTarget_RejectsRecordedMachineContradictions(int machine, int targetMachine)
    {
        Fixture fixture = CreateFixture(BasicTypes());
        BinaryPrimitives.WriteUInt16LittleEndian(fixture.Bytes.AsSpan(fixture.StreamStarts[3] + 58), checked((ushort)machine));
        using var input = new MemoryStream(fixture.Bytes);
        input.Position = 17;

        // Matching widths alone do not make different instruction-set identities interchangeable.
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input, targetMachine: checked((ushort)targetMachine)));
        Assert.AreEqual(17L, input.Position);
    }

    /// <summary>Invalid caller architecture declarations fail before consuming the borrowed stream.</summary>
    /// <param name="targetMachine">An unspecified or unsupported target machine.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(0x1C4)]
    public void ExplicitTarget_RejectsInvalidDeclarations(int targetMachine)
    {
        Fixture fixture = CreateFixture(BasicTypes());
        using var input = new MemoryStream(fixture.Bytes);
        input.Position = 17;

        // A caller hint cannot turn an unsupported target into a valid native layout.
        Assert.Throws<ArgumentOutOfRangeException>(() => new PdbMetadata(input, targetMachine: checked((ushort)targetMachine)));
        Assert.AreEqual(17L, input.Position);
    }

    /// <summary>Unknown header revisions are not accepted just because one legacy layout is supported.</summary>
    /// <param name="revision">Absent, earlier or unrecognized future TPI revision.</param>
    [TestMethod]
    [DataRow(0U)]
    [DataRow(19970605U)]
    [DataRow(20040204U)]
    public void LegacyTpiHeader_RejectsOtherRevisions(uint revision)
    {
        Fixture fixture = CreateFixture(BasicTypes());
        Put32(fixture.Bytes, fixture.StreamStarts[2], revision);
        using var input = new MemoryStream(fixture.Bytes);

        // The shared fixed-header bounds apply only to explicitly supported revisions.
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input));
    }

    /// <summary>Discontiguous streams and positive short reads are joined without reading unrelated file payloads.</summary>
    [TestMethod]
    public void MetadataReads_StayBoundedAndHandlePositiveShortReads()
    {
        Fixture fixture = CreateFixture(BasicTypes(), unrelatedBytes: 2 * 1024 * 1024);
        using var input = new ShortReadStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        Assert.AreEqual(0x1020UL, metadata.Symbols["OriginalData"]);
        Assert.IsTrue(input.ObservedBytes < 128 * 1024, $"Only selected metadata should be read, not {input.ObservedBytes} bytes.");
        Assert.IsTrue(input.CanRead);
    }

    /// <summary>Malformed directory and identity claims fail at their boundary rather than becoming prepared profiles.</summary>
    /// <param name="corruption">Independent wire invariant to violate.</param>
    [TestMethod]
    [DataRow("magic")]
    [DataRow("block-width")]
    [DataRow("free-map")]
    [DataRow("file-extent")]
    [DataRow("reserved-map")]
    [DataRow("duplicate-stream-block")]
    [DataRow("stream-count")]
    [DataRow("identity-version")]
    [DataRow("debug-version")]
    [DataRow("type-count")]
    [DataRow("type-record-length")]
    public void MalformedNativeInput_RejectsContradictoryMetadata(string corruption)
    {
        Fixture fixture = CreateFixture(BasicTypes());
        byte[] bytes = fixture.Bytes;
        switch (corruption)
        {
        case "magic": bytes[0] = 0; break;
        case "block-width": Put32(bytes, 32, 3); break;
        case "free-map": Put32(bytes, 36, 3); break;
        case "file-extent": Put32(bytes, 40, uint.MaxValue); break;
        case "reserved-map": Put32(bytes, 52, 1); break;
        case "duplicate-stream-block": Put32(bytes, fixture.Directory + 36, (uint)(fixture.StreamStarts[1] / 512)); break;
        case "stream-count": Put32(bytes, fixture.Directory, uint.MaxValue); break;
        case "identity-version": Put32(bytes, fixture.StreamStarts[1], 0); break;
        case "debug-version": Put32(bytes, fixture.StreamStarts[3] + 4, 0); break;
        case "type-count": Put32(bytes, fixture.StreamStarts[2] + 12, 0x1000); break;
        case "type-record-length": BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(fixture.StreamStarts[2] + 56), 1); break;
        default: throw new ArgumentException("Unknown synthetic corruption.", nameof(corruption));
        }

        using var input = new MemoryStream(bytes);
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input));
    }

    /// <summary>A truncated original extent is an invalid input, including a file shorter than its superblock.</summary>
    /// <param name="length">Retained prefix of the otherwise valid fixture.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(55)]
    [DataRow(512)]
    public void TruncatedNativeInput_RejectsMissingOriginalBytes(int length)
    {
        Fixture fixture = CreateFixture(BasicTypes());
        using var input = new MemoryStream(fixture.Bytes, 0, length, writable: false);
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input));
    }

    /// <summary>Input, record and export work budgets remain separate from each other and from the containing file size.</summary>
    [TestMethod]
    public void PreparationBudgets_RejectExcessWorkAndRetainInputOwnership()
    {
        Fixture fixture = CreateFixture(BasicTypes());
        using var input = new MemoryStream(fixture.Bytes);
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input, maxMetadataBytes: 16));
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input, maxRecords: 1));

        // Input tables that fit maxRecords must not make the default export fail: export work has its own limit.
        var metadata = new PdbMetadata(input, maxRecords: 4);
        using var output = new MemoryStream();
        metadata.WriteIsf(output, "fixture.pdb");
        Assert.IsTrue(output.Length > 0);
        using var limited = new MemoryStream();
        Assert.Throws<CStructLayoutException>(() => metadata.WriteIsf(limited, "fixture.pdb", maxTraversalSteps: 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.WriteIsf(limited, "fixture.pdb", maxTraversalSteps: 0));
        Assert.AreEqual(0L, input.Position);
        Assert.IsTrue(input.CanRead);
    }

    /// <summary>C++ methods, bases, nested types, static members and vtable entries are skipped without failing the export.</summary>
    [TestMethod]
    public void CppFieldLists_SkipNonStorageElementsAndKeepInstanceMembers()
    {
        byte[] derivedFields =
        [
            .. FieldLeaf(0x1400, writer =>
            {
                // LF_BCLASS: public base at offset 8.
                writer.Write((ushort)3);
                writer.Write(0x1001U);
                writer.Write((ushort)8);
            }),
            .. FieldLeaf(0x1409, writer =>
            {
                // LF_VFUNCTAB: padding word and vtable-pointer type.
                writer.Write((ushort)0);
                writer.Write(0x0603U);
            }),
            .. FieldLeaf(0x1511, writer =>
            {
                // LF_ONEMETHOD, introducing virtual (property 4): the extra vtable offset must be consumed.
                writer.Write((ushort)((4 << 2) | 3));
                writer.Write(0x75U);
                writer.Write(0x10U);
                WriteName(writer, "Run");
            }),
            .. FieldLeaf(0x1511, writer =>
            {
                // LF_ONEMETHOD, plain method: no vtable offset.
                writer.Write((ushort)3);
                writer.Write(0x75U);
                WriteName(writer, "Stop");
            }),
            .. FieldLeaf(0x150F, writer =>
            {
                // LF_METHOD: two overloads in a method list.
                writer.Write((ushort)2);
                writer.Write(0x75U);
                WriteName(writer, "Reset");
            }),
            .. FieldLeaf(0x150E, writer =>
            {
                // LF_STMEMBER: static storage lives elsewhere, not in the instance.
                writer.Write((ushort)3);
                writer.Write(0x75U);
                WriteName(writer, "instances");
            }),
            .. FieldLeaf(0x1510, writer =>
            {
                // LF_NESTTYPE: a type declared inside the class.
                writer.Write((ushort)0);
                writer.Write(0x1001U);
                WriteName(writer, "Inner");
            }),
            .. Member("value", 0x75, 12),
        ];
        byte[][] types =
        [
            Record(0x1203, Member("baseValue", 0x75, 0)),
            Composite("base", 1, 0x1000, 4),
            Record(0x1203, derivedFields),
            Composite("derived", 8, 0x1002, 16),
        ];
        Fixture fixture = CreateFixture(types, rootType: 0x1003);
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        using var output = new MemoryStream();
        metadata.WriteIsf(output, "fixture.pdb");
        var isf = new IsfMetadata(output.ToArray());
        MetadataImportResult imported = isf.Import("derived");
        CollectionAssert.AreEqual(new[] { "value", }, imported.Schema.GetType(imported.RootTypeId).Fields.Select(field => field.Name).ToArray());
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(12), 0xCAFEF00D);
        var session = new MemorySession(imported.Schema);
        Assert.AreEqual(0xCAFEF00DU, session.Read(new MemoryRegion(new ByteArrayMemorySource("cpp", bytes), 0, 16), imported.RootTypeId, "value"));
    }

    /// <summary>The declared count is an upper bound with C++ elements present, but more instance members than declared still fail.</summary>
    [TestMethod]
    public void CppFieldLists_RejectMoreInstanceMembersThanDeclared()
    {
        byte[] stMember = FieldLeaf(0x150E, writer =>
        {
            writer.Write((ushort)3);
            writer.Write(0x75U);
            WriteName(writer, "instances");
        });
        byte[][] types = [Record(0x1203, [.. stMember, .. Member("first", 0x75, 0), .. Member("second", 0x75, 4)]), Composite("record", 1, 0x1000, 8)];
        Fixture fixture = CreateFixture(types, rootType: 0x1001);
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        using var output = new MemoryStream();
        Assert.Throws<CStructLayoutException>(() => metadata.WriteIsf(output, "fixture.pdb"));
    }

    /// <summary>A PDB without a symbol-record stream still provides its types and an empty symbol table.</summary>
    [TestMethod]
    public void MissingSymbolStream_KeepsTypesWithEmptySymbols()
    {
        Fixture fixture = CreateFixture(BasicTypes());
        BinaryPrimitives.WriteUInt16LittleEndian(fixture.Bytes.AsSpan(fixture.StreamStarts[3] + 20), ushort.MaxValue);
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        Assert.AreEqual(0, metadata.Symbols.Count);
        Assert.AreEqual(0, metadata.AbsoluteSymbols.Count);
        Assert.AreEqual(0, metadata.UnmappedSymbolCount);
        CollectionAssert.Contains(metadata.UserTypeNames.ToArray(), "record");
        using var output = new MemoryStream();
        metadata.WriteIsf(output, "fixture.pdb");
        MetadataImportResult imported = new IsfMetadata(output.ToArray()).Import("record");
        Assert.AreEqual(MemoryTypeKind.Struct, imported.Schema.GetType(imported.RootTypeId).Kind);
    }

    /// <summary>File-local data names repeated across compilation units do not reject the database or publish a guessed address.</summary>
    [TestMethod]
    public void FileLocalSymbols_KeepUnambiguousNamesAndReportConflicts()
    {
        byte[] locals =
        [
            .. SymbolRecord("s_lock", 0x110C, 0x75, 0x50, 1),
            .. SymbolRecord("s_lock", 0x110C, 0x75, 0x60, 1),
            .. SymbolRecord("solo", 0x110C, 0x75, 0x70, 1),
            .. SymbolRecord("TypedData", 0x110C, 0x1001, 0x40, 1),
            .. SymbolRecord("OriginalData", 0x110C, 0x75, 0x90, 1),
        ];
        Fixture fixture = CreateFixture(BasicTypes(), extraSymbols: locals);
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);

        Assert.IsFalse(metadata.Symbols.ContainsKey("s_lock"));
        Assert.AreEqual(0x1070UL, metadata.Symbols["solo"]);
        Assert.AreEqual(0x1040UL, metadata.Symbols["TypedData"]);
        Assert.AreEqual(0x1020UL, metadata.Symbols["OriginalData"]);
        Assert.IsTrue(metadata.Diagnostics.Any(message => message.Contains("'s_lock'", StringComparison.Ordinal)));
        Assert.IsTrue(metadata.Diagnostics.Any(message => message.Contains("'OriginalData'", StringComparison.Ordinal)));
        Assert.AreEqual(5, metadata.SymbolRecordKinds[0x110C]);

        using var output = new MemoryStream();
        metadata.WriteIsf(output, "fixture.pdb");
        JsonNode profile = JsonNode.Parse(output.ToArray())!;
        Assert.AreEqual(0x1070, profile["symbols"]!["solo"]!["address"]!.GetValue<int>());
        Assert.AreEqual("unsigned int", profile["symbols"]!["solo"]!["type"]!["name"]!.GetValue<string>());
        Assert.IsNull(profile["symbols"]!["s_lock"]);
    }

    /// <summary>A name recorded by both a public and a global record counts once among unmapped symbols.</summary>
    [TestMethod]
    public void UnmappedSymbolCount_CountsDistinctNames()
    {
        Fixture fixture = CreateFixture(BasicTypes(), extraSymbols: SymbolRecord("absoluteGuard", 0x110D, 0x75, 0x12345678, 2));
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        Assert.AreEqual(0x12345678UL, metadata.AbsoluteSymbols["absoluteGuard"]);
        Assert.AreEqual(1, metadata.UnmappedSymbolCount);
    }

    /// <summary>Cancellation refuses both original reads and CPU-only output traversal without owning either stream.</summary>
    [TestMethod]
    public void Cancellation_LeavesBorrowedStreamsOpen()
    {
        Fixture fixture = CreateFixture(BasicTypes());
        using var input = new MemoryStream(fixture.Bytes);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => new PdbMetadata(input, cancellationToken: cancelled.Token));
        var metadata = new PdbMetadata(input);
        using var output = new MemoryStream();
        Assert.Throws<OperationCanceledException>(() => metadata.WriteIsf(output, "fixture.pdb", cancellationToken: cancelled.Token));
        Assert.AreEqual(0L, output.Length);
        Assert.IsTrue(input.CanRead && output.CanWrite);
    }

    /// <summary>A self-referencing continuation cannot spin or publish a field list with invented members.</summary>
    [TestMethod]
    public void FieldListContinuationCycle_RejectsPreparation()
    {
        // This continuation points back to its own native type index.
        byte[] continuation = Record(0x1203, Payload(writer =>
        {
            writer.Write((ushort)0x1404);
            writer.Write((ushort)0);
            writer.Write(0x1000U);
        }));
        Fixture fixture = CreateFixture([continuation, Composite("record", 0, 0x1000, 16)]);
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        using var output = new MemoryStream();
        Assert.Throws<CStructLayoutException>(() => metadata.WriteIsf(output, "fixture.pdb"));
    }

    /// <summary>Every original duplicate same-extent definition remains inspectable through an explicit union, including embedded forward uses.</summary>
    [TestMethod]
    public void DuplicateNativeDefinitions_PreserveAllEmbeddedInterpretations()
    {
        byte[][] types =
        [
            Composite("alternative", 0, 0, 0, forward: true),
            Record(0x1203, Member("first", 0x75, 0)),
            Composite("alternative", 1, 0x1001, 4),
            Record(0x1203, Member("second", 0x75, 0)),
            Composite("alternative", 1, 0x1003, 4),
            Record(0x1203, Member("embedded", 0x1000, 0)),
            Composite("record", 1, 0x1005, 4),
        ];
        Fixture fixture = CreateFixture(types, rootType: 0x1006);
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        using var output = new MemoryStream();
        metadata.WriteIsf(output, "fixture.pdb");
        var isf = new IsfMetadata(output.ToArray());
        MetadataImportResult imported = isf.Import("record");
        var session = new MemorySession(imported.Schema);
        byte[] bytes = [0x78, 0x56, 0x34, 0x12,];
        var region = new MemoryRegion(new ByteArrayMemorySource("ambiguous-originals", bytes), 0, 4);
        Assert.AreEqual(0x12345678U, session.Read(region, imported.RootTypeId, "embedded.cv_00001002.first"));
        Assert.AreEqual(0x12345678U, session.Read(region, imported.RootTypeId, "embedded.cv_00001004.second"));
        CollectionAssert.AreEqual(bytes, session.Serialize(imported.RootTypeId, session.Read(region, imported.RootTypeId)));
    }

    /// <summary>OMAP changes only represented image coordinates; deleted intervals never become fake zero-RVA symbols.</summary>
    [TestMethod]
    public void OriginalOmap_PreservesMappedDeletedAndAbsoluteCoordinates()
    {
        byte[] map = new byte[16];
        Put32(map, 0, 0x1000);
        Put32(map, 4, 0x2000);
        Put32(map, 8, 0x1030);
        Fixture fixture = CreateFixture(BasicTypes(), omap: map);
        using var input = new MemoryStream(fixture.Bytes);
        var metadata = new PdbMetadata(input);
        Assert.AreEqual(0x2020UL, metadata.Symbols["OriginalData"]);
        Assert.IsFalse(metadata.Symbols.ContainsKey("TypedData"));
        Assert.AreEqual(0x12345678UL, metadata.AbsoluteSymbols["absoluteGuard"]);
        Assert.AreEqual(2, metadata.UnmappedSymbolCount);
    }

    /// <summary>Truncated, unordered, duplicate and wrapping remaps cannot publish invented symbol coordinates.</summary>
    /// <param name="corruption">Original OMAP invariant to violate.</param>
    [TestMethod]
    [DataRow("truncated")]
    [DataRow("unordered")]
    [DataRow("duplicate")]
    [DataRow("wrap")]
    [DataRow("missing-original-sections")]
    public void InvalidOmap_RejectsAmbiguousOrUnboundedCoordinates(string corruption)
    {
        byte[] map = new byte[16];
        Put32(map, 0, 0x1000);
        Put32(map, 4, 0x2000);
        Put32(map, 8, 0x1030);
        switch (corruption)
        {
            case "truncated": map = map[..7]; break;
            case "unordered": Put32(map, 8, 0x0FFF); break;
            case "duplicate": Put32(map, 8, 0x1000); break;
            case "wrap": Put32(map, 4, uint.MaxValue); break;
            case "missing-original-sections": break;
            default: throw new ArgumentException("Unknown synthetic remap corruption.", nameof(corruption));
        }

        Fixture fixture = CreateFixture(BasicTypes(), omap: map);
        if (corruption == "missing-original-sections")
        {
            BinaryPrimitives.WriteUInt16LittleEndian(fixture.Bytes.AsSpan(fixture.StreamStarts[3] + 128), ushort.MaxValue);
        }

        using var input = new MemoryStream(fixture.Bytes);
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input));
    }

    /// <summary>A source that changes its original extent during a read cannot produce trusted metadata.</summary>
    [TestMethod]
    public void SourceLengthChange_RejectsThePreparationInput()
    {
        Fixture fixture = CreateFixture(BasicTypes());
        using var input = new ChangedLengthStream(fixture.Bytes);
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input));
    }

    /// <summary>Malformed original UTF-8 type names fail instead of acquiring replacement characters.</summary>
    [TestMethod]
    public void InvalidNativeName_RejectsLossyMetadata()
    {
        byte[][] types = BasicTypes();
        Fixture fixture = CreateFixture(types);
        int name = fixture.StreamStarts[2] + 56 + types[0].Length + 4 + 18;
        fixture.Bytes[name] = 0xFF;
        using var input = new MemoryStream(fixture.Bytes);
        Assert.Throws<CStructLayoutException>(() => new PdbMetadata(input));
    }

    /// <summary>Creates independently declared native storage records, including explicit 32/64-bit pointer modes.</summary>
    /// <returns>Full CodeView records in original index order.</returns>
    private static byte[][] BasicTypes() =>
    [
        Record(0x1203, [.. Member("value", 0x75, 0), .. Member("narrow", 0x403, 4), .. Member("wide", 0x603, 8)]),
        Composite("record", 3, 0x1000, 16),
    ];

    /// <summary>Builds a small native MSF container with original identities, logical/absolute segments and nonzero symbol padding.</summary>
    /// <param name="types">Full original CodeView type records.</param>
    /// <param name="unrelatedBytes">Optional non-metadata stream that the reader must not materialize.</param>
    /// <param name="rootType">Actual complete record identity for the typed global.</param>
    /// <param name="omap">Optional original source-to-image remapping entries.</param>
    /// <param name="extraSymbols">Optional complete symbol records appended after the standard public, global and absolute records.</param>
    /// <returns>The original bytes and mutation coordinates for boundary tests.</returns>
    private static Fixture CreateFixture(byte[][] types, int unrelatedBytes = 0, uint rootType = 0x1001, byte[]? omap = null, byte[]? extraSymbols = null)
    {
        byte[] information = new byte[28];
        Put32(information, 0, 20000404);
        Put32(information, 8, 7);
        new Guid("12345678-9abc-def0-1234-56789abcdef0").TryWriteBytes(information.AsSpan(12));
        byte[] tpi = new byte[56 + types.Sum(type => type.Length)];
        Put32(tpi, 0, 20040203);
        Put32(tpi, 4, 56);
        Put32(tpi, 8, 0x1000);
        Put32(tpi, 12, 0x1000U + (uint)types.Length);
        Put32(tpi, 16, (uint)tpi.Length - 56);
        int typeOffset = 56;
        foreach (byte[] type in types)
        {
            type.CopyTo(tpi, typeOffset);
            typeOffset += type.Length;
        }

        int optionalLength = omap is null ? 12 : 22;
        byte[] debug = new byte[108 + optionalLength];
        Put32(debug, 0, uint.MaxValue);
        Put32(debug, 4, 19990903);
        Put32(debug, 8, 2);
        BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(20), 6);
        Put32(debug, 32, 44);
        Put32(debug, 48, (uint)optionalLength);
        BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(58), 0x8664);
        BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(64), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(66), 2);
        BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(68), 0x109);
        BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(74), 1);
        Put32(debug, 84, 0x2000);
        BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(88), 0x208);
        Put32(debug, 104, uint.MaxValue);
        debug.AsSpan(108, optionalLength).Fill(0xFF);
        BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(118), 5);
        if (omap is not null)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(116), 7);
            BinaryPrimitives.WriteUInt16LittleEndian(debug.AsSpan(128), 8);
        }

        byte[] sections = new byte[40];
        Put32(sections, 8, 0x2000);
        Put32(sections, 12, 0x1000);
        byte[] symbols = [.. SymbolRecord("OriginalData", 0x110E, 0, 0x20, 1), .. SymbolRecord("TypedData", 0x110D, rootType, 0x40, 1), .. SymbolRecord("absoluteGuard", 0x110E, 0, 0x12345678, 2), .. extraSymbols ?? []];
        byte[][] streams = omap is not null
            ? [[], information, tpi, debug, [], sections, symbols, omap, sections]
            : unrelatedBytes == 0 ? [[], information, tpi, debug, [], sections, symbols] : [[], information, tpi, debug, [], sections, symbols, new byte[unrelatedBytes]];
        int directoryLength = 4 + (streams.Length * 4) + (streams.Sum(bytes => (bytes.Length + 511) / 512) * 4);
        int nextBlock = 4;

        // Directory pages must be reserved before assigning discontiguous stream payloads.
        int[] directoryBlocks = Enumerable.Range(0, (directoryLength + 511) / 512).Select(_ => AllocateBlock(ref nextBlock)).ToArray();
        int[][] blocks = new int[streams.Length][];
        for (int index = 0; index < streams.Length; index++)
        {
            blocks[index] = new int[(streams[index].Length + 511) / 512];
            for (int page = 0; page < blocks[index].Length; page++)
            {
                blocks[index][page] = AllocateBlock(ref nextBlock);
            }
        }

        byte[] file = new byte[nextBlock * 512];
        "Microsoft C/C++ MSF 7.00\r\n\u001aDS\0\0\0"u8.CopyTo(file);
        Put32(file, 32, 512);
        Put32(file, 36, 1);
        Put32(file, 40, (uint)nextBlock);
        Put32(file, 44, (uint)directoryLength);
        Put32(file, 52, 3);
        byte[] directory = new byte[directoryLength];
        Put32(directory, 0, (uint)streams.Length);
        int cursor = 4 + (streams.Length * 4);
        int[] starts = new int[streams.Length];
        for (int index = 0; index < streams.Length; index++)
        {
            Put32(directory, 4 + (index * 4), (uint)streams[index].Length);
            starts[index] = blocks[index].Length == 0 ? 0 : blocks[index][0] * 512;
            for (int page = 0; page < blocks[index].Length; page++)
            {
                int block = blocks[index][page];
                Put32(directory, cursor, (uint)block);
                cursor += 4;
                streams[index].AsSpan(page * 512, Math.Min(512, streams[index].Length - (page * 512))).CopyTo(file.AsSpan(block * 512));
            }
        }

        for (int page = 0; page < directoryBlocks.Length; page++)
        {
            Put32(file, (3 * 512) + (page * 4), (uint)directoryBlocks[page]);
            directory.AsSpan(page * 512, Math.Min(512, directory.Length - (page * 512))).CopyTo(file.AsSpan(directoryBlocks[page] * 512));
        }

        return new(file, directoryBlocks[0] * 512, starts);
    }

    /// <summary>Allocates a data block without using either periodic free-map slot.</summary>
    /// <param name="next">Next unassigned physical block.</param>
    /// <returns>One original data-block index.</returns>
    private static int AllocateBlock(ref int next)
    {
        while (next % 512 is 1 or 2)
        {
            next++;
        }

        return next++;
    }

    /// <summary>Creates a complete named structure record with explicit byte size and member-list identity.</summary>
    /// <param name="name">Original native name.</param>
    /// <param name="count">Declared field count.</param>
    /// <param name="fields">Original field-list index.</param>
    /// <param name="size">Original byte extent.</param>
    /// <param name="forward">Whether the record is an incomplete declaration.</param>
    /// <returns>A full native type record.</returns>
    private static byte[] Composite(string name, ushort count, uint fields, ushort size, bool forward = false)
    {
        // Native sizes and field-list identities are explicit, not recomputed from a host ABI.
        return Record(0x1505, Payload(writer =>
        {
            writer.Write(count);
            writer.Write((ushort)(forward ? 0x80 : 0));
            writer.Write(fields);
            writer.Write(0U);
            writer.Write(0U);
            writer.Write(size);
            WriteName(writer, name);
        }));
    }

    /// <summary>Creates an instance-member leaf with a known storage reference and original placement.</summary>
    /// <param name="name">Original member name.</param>
    /// <param name="type">Original storage-type index.</param>
    /// <param name="offset">Byte offset in the containing record.</param>
    /// <returns>The unframed field leaf including alignment.</returns>
    private static byte[] Member(string name, uint type, ushort offset)
    {
        // Field-list leaves have their own alignment but no enclosing length prefix.
        byte[] bytes = Payload(writer =>
        {
            writer.Write((ushort)0x150D);
            writer.Write((ushort)0);
            writer.Write(type);
            writer.Write(offset);
            WriteName(writer, name);
        });
        return Pad(bytes);
    }

    /// <summary>Creates one aligned field-list leaf from its code and payload.</summary>
    /// <param name="kind">Original field leaf code.</param>
    /// <param name="write">Payload writer, starting with the leaf's leading two-byte word.</param>
    /// <returns>The unframed field leaf including alignment.</returns>
    private static byte[] FieldLeaf(ushort kind, Action<BinaryWriter> write)
    {
        return Pad(Payload(writer =>
        {
            writer.Write(kind);
            write(writer);
        }));
    }

    /// <summary>Frames one original CodeView type record and applies its descending leaf-padding bytes.</summary>
    /// <param name="kind">Original leaf code.</param>
    /// <param name="payload">Original payload excluding prefix.</param>
    /// <returns>The complete aligned type record.</returns>
    private static byte[] Record(ushort kind, byte[] payload)
    {
        // The native length excludes only its own two-byte prefix.
        byte[] bytes = Payload(writer =>
        {
            writer.Write((ushort)(payload.Length + 2));
            writer.Write(kind);
            writer.Write(payload);
        });
        byte[] padded = Pad(bytes);
        BinaryPrimitives.WriteUInt16LittleEndian(padded, (ushort)(padded.Length - 2));
        return padded;
    }

    /// <summary>Creates public or typed-global symbols whose nonzero alignment bytes are not part of the name.</summary>
    /// <param name="name">Original symbol name.</param>
    /// <param name="kind">Native symbol kind.</param>
    /// <param name="type">Stored type identity or public flags.</param>
    /// <param name="offset">Original logical segment offset.</param>
    /// <param name="segment">Original logical segment.</param>
    /// <returns>A full native symbol record.</returns>
    private static byte[] SymbolRecord(string name, ushort kind, uint type, uint offset, ushort segment)
    {
        // Public flags and typed-global indices share the same wire slot.
        byte[] payload = Payload(writer =>
        {
            writer.Write(type);
            writer.Write(offset);
            writer.Write(segment);
            WriteName(writer, name);
        });
        int length = (payload.Length + 7) & ~3;
        byte[] bytes = new byte[length];
        bytes.AsSpan().Fill(0xA5);
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)(length - 2));
        BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(2), kind);
        payload.CopyTo(bytes, 4);
        return bytes;
    }

    /// <summary>Builds wire payloads through explicit little-endian primitive writes.</summary>
    /// <param name="write">Independent fixture fields.</param>
    /// <returns>The original payload bytes.</returns>
    private static byte[] Payload(Action<BinaryWriter> write)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        write(writer);
        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Terminates original fixture names without introducing replacement text.</summary>
    /// <param name="writer">Fixture output.</param>
    /// <param name="name">Complete synthetic name.</param>
    private static void WriteName(BinaryWriter writer, string name)
    {
        writer.Write(Encoding.UTF8.GetBytes(name));
        writer.Write((byte)0);
    }

    /// <summary>Appends descending CodeView alignment bytes to a four-byte boundary.</summary>
    /// <param name="bytes">Original unaligned storage.</param>
    /// <returns>A complete aligned record.</returns>
    private static byte[] Pad(byte[] bytes)
    {
        int padding = (4 - (bytes.Length & 3)) & 3;
        byte[] result = new byte[bytes.Length + padding];
        bytes.CopyTo(result, 0);
        for (int index = 0; index < padding; index++)
        {
            result[bytes.Length + index] = (byte)(0xF0 + padding - index);
        }

        return result;
    }

    /// <summary>Writes a fixture's explicit little-endian unsigned field.</summary>
    /// <param name="bytes">Containing fixture.</param>
    /// <param name="offset">Byte coordinate in the fixture.</param>
    /// <param name="value">Original wire value.</param>
    private static void Put32(byte[] bytes, int offset, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(offset), value);

    /// <summary>Mutation coordinates belong only to this independently authored synthetic fixture.</summary>
    /// <param name="Bytes">Complete original bytes.</param>
    /// <param name="Directory">First directory page.</param>
    /// <param name="StreamStarts">First physical byte of each present stream.</param>
    private sealed record Fixture(byte[] Bytes, int Directory, int[] StreamStarts);

    /// <summary>Changes the declared source length after a successful transport operation.</summary>
    private sealed class ChangedLengthStream : MemoryStream
    {
        private int reads;

        /// <summary>Wraps independently authored mutable storage for the source-change control.</summary>
        /// <param name="bytes">Original synthetic input.</param>
        public ChangedLengthStream(byte[] bytes)
            : base(bytes, writable: true)
        {
        }

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            int read = base.Read(buffer);
            if (++this.reads == 2)
            {
                this.SetLength(this.Length - 1);
            }

            return read;
        }
    }

    /// <summary>Forces positive short reads while observing only confirmed original bytes transferred.</summary>
    private sealed class ShortReadStream : MemoryStream
    {
        /// <summary>Wraps synthetic bytes without copying their storage.</summary>
        /// <param name="bytes">Original fixture bytes.</param>
        public ShortReadStream(byte[] bytes)
            : base(bytes, writable: false)
        {
        }

        /// <summary>Gets the confirmed original byte count observed by the metadata parser.</summary>
        public long ObservedBytes { get; private set; }

        /// <inheritdoc/>
        public override int Read(Span<byte> buffer)
        {
            int read = base.Read(buffer[..Math.Min(buffer.Length, 7)]);
            this.ObservedBytes += read;
            return read;
        }
    }
}
