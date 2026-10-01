namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>
///     Small hand-written ISF documents with the shapes that Linux kernel profiles contain: references to types that
///     only have a forward declaration, bitfields recorded at the byte that holds their first bit, and the symbol table.
/// </summary>
[TestClass]
public class MemoryIsfKernelProfileTests
{
    /// <summary>
    ///     A task with pointers to a struct, an enum and a base type that the document never defines. In C,
    ///     <c>struct files_struct *files;</c> compiles with only a forward declaration in scope, and kernel profiles keep
    ///     such pointers while leaving the pointed-to type out of <c>user_types</c>.
    /// </summary>
    private const string OpaqueTargets = """
        {"metadata":{"format":"6.2.0"},
         "base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},
         "user_types":{
           "task":{"kind":"struct","size":32,"fields":{
             "pid":{"offset":0,"type":{"kind":"base","name":"u32"}},
             "files":{"offset":8,"type":{"kind":"pointer","subtype":{"kind":"struct","name":"files_struct"}}},
             "state":{"offset":16,"type":{"kind":"pointer","subtype":{"kind":"enum","name":"task_state"}}},
             "cookie":{"offset":24,"type":{"kind":"pointer","subtype":{"kind":"base","name":"cookie_t"}}}
           }},
           "holder":{"kind":"struct","size":16,"fields":{
             "pid":{"offset":0,"type":{"kind":"base","name":"u32"}},
             "files":{"offset":8,"type":{"kind":"struct","name":"files_struct"}}
           }},
           "outer":{"kind":"struct","size":24,"fields":{
             "holder":{"offset":0,"type":{"kind":"struct","name":"holder"}},
             "task":{"offset":16,"type":{"kind":"pointer","subtype":{"kind":"struct","name":"task"}}}
           }}
         },
         "enums":{},
         "symbols":{}}
        """;

    /// <summary>
    ///     The x86 segment descriptor <c>struct desc_struct</c> exactly as a kernel profile records it: every bitfield's
    ///     <c>offset</c> is the byte holding its first bit and <c>bit_position</c> counts from that byte, so <c>base2</c>
    ///     claims a two-byte storage unit at byte 7 of an eight-byte struct.
    /// </summary>
    private const string DescriptorByByte = """
        {"metadata":{"format":"6.2.0"},
         "base_types":{"unsigned short":{"kind":"int","size":2,"signed":false,"endian":"little"}},
         "user_types":{"desc_struct":{"kind":"struct","size":8,"fields":{
           "limit0":{"offset":0,"type":{"kind":"base","name":"unsigned short"}},
           "base0":{"offset":2,"type":{"kind":"base","name":"unsigned short"}},
           "base1":{"offset":4,"type":{"kind":"bitfield","bit_position":0,"bit_length":8,"type":{"kind":"base","name":"unsigned short"}}},
           "type":{"offset":5,"type":{"kind":"bitfield","bit_position":0,"bit_length":4,"type":{"kind":"base","name":"unsigned short"}}},
           "s":{"offset":5,"type":{"kind":"bitfield","bit_position":4,"bit_length":1,"type":{"kind":"base","name":"unsigned short"}}},
           "dpl":{"offset":5,"type":{"kind":"bitfield","bit_position":5,"bit_length":2,"type":{"kind":"base","name":"unsigned short"}}},
           "p":{"offset":5,"type":{"kind":"bitfield","bit_position":7,"bit_length":1,"type":{"kind":"base","name":"unsigned short"}}},
           "limit1":{"offset":6,"type":{"kind":"bitfield","bit_position":0,"bit_length":4,"type":{"kind":"base","name":"unsigned short"}}},
           "g":{"offset":6,"type":{"kind":"bitfield","bit_position":7,"bit_length":1,"type":{"kind":"base","name":"unsigned short"}}},
           "base2":{"offset":7,"type":{"kind":"bitfield","bit_position":0,"bit_length":8,"type":{"kind":"base","name":"unsigned short"}}}
         }}},
         "enums":{},"symbols":{}}
        """;

    /// <summary>The same descriptor as <see cref="DescriptorByByte"/>, recorded the other way ISF allows: each offset is the start of the two-byte storage unit.</summary>
    private const string DescriptorByUnit = """
        {"metadata":{"format":"6.2.0"},
         "base_types":{"unsigned short":{"kind":"int","size":2,"signed":false,"endian":"little"}},
         "user_types":{"desc_struct":{"kind":"struct","size":8,"fields":{
           "limit0":{"offset":0,"type":{"kind":"base","name":"unsigned short"}},
           "base0":{"offset":2,"type":{"kind":"base","name":"unsigned short"}},
           "base1":{"offset":4,"type":{"kind":"bitfield","bit_position":0,"bit_length":8,"type":{"kind":"base","name":"unsigned short"}}},
           "type":{"offset":4,"type":{"kind":"bitfield","bit_position":8,"bit_length":4,"type":{"kind":"base","name":"unsigned short"}}},
           "s":{"offset":4,"type":{"kind":"bitfield","bit_position":12,"bit_length":1,"type":{"kind":"base","name":"unsigned short"}}},
           "dpl":{"offset":4,"type":{"kind":"bitfield","bit_position":13,"bit_length":2,"type":{"kind":"base","name":"unsigned short"}}},
           "p":{"offset":4,"type":{"kind":"bitfield","bit_position":15,"bit_length":1,"type":{"kind":"base","name":"unsigned short"}}},
           "limit1":{"offset":6,"type":{"kind":"bitfield","bit_position":0,"bit_length":4,"type":{"kind":"base","name":"unsigned short"}}},
           "g":{"offset":6,"type":{"kind":"bitfield","bit_position":7,"bit_length":1,"type":{"kind":"base","name":"unsigned short"}}},
           "base2":{"offset":6,"type":{"kind":"bitfield","bit_position":8,"bit_length":8,"type":{"kind":"base","name":"unsigned short"}}}
         }}},
         "enums":{},"symbols":{}}
        """;

    /// <summary>A pointer to a struct, enum, or base type the document never defines imports as an address-only type in strict mode, with one diagnostic per missing name.</summary>
    [TestMethod]
    public void Isf_PointerToUndefinedTypeImportsAsIncomplete()
    {
        MetadataImportResult imported = Parse(OpaqueTargets).Import("task");

        foreach (string id in new[] { "isf:user:files_struct", "isf:enum:task_state", "isf:base:cookie_t", })
        {
            Assert.AreEqual(MemoryTypeKind.Incomplete, imported.Schema.GetType(id).Kind, id);
            Assert.AreEqual(1, imported.Diagnostics.Count(note => note.StartsWith(id + ":", StringComparison.Ordinal)), id);
        }

        // The pointer itself is an ordinary 8-byte value, so it can be read; only following it is refused.
        byte[] bytes = new byte[32];
        bytes[8] = 0x40;
        var session = new MemorySession(imported.Schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual(new StoredPointer(0x40), session.Read(region, imported.RootTypeId, "files"));
    }

    /// <summary>
    ///     Embedding an undefined struct by value has no size to lay out, so strict mode fails with the type's name, and
    ///     best effort demotes only the struct that embeds it while pointers to that struct still import.
    /// </summary>
    [TestMethod]
    public void Isf_UndefinedTypeByValueFailsStrictAndDemotesInBestEffort()
    {
        IsfMetadata metadata = Parse(OpaqueTargets);

        CStructLayoutException error = Assert.Throws<CStructLayoutException>(() => metadata.Import("holder"));
        StringAssert.Contains(error.Message, "isf:user:files_struct");

        MetadataImportResult tolerant = metadata.Import("outer", new MetadataImportOptions { BestEffort = true, });
        Assert.AreEqual(MemoryTypeKind.RawBytes, tolerant.Schema.GetType("isf:user:holder").Kind);
        Assert.AreEqual(16, tolerant.Schema.GetType("isf:user:holder").Size);
        Assert.AreEqual(MemoryTypeKind.Struct, tolerant.Schema.GetType("isf:user:task").Kind);
        Assert.AreEqual(MemoryTypeKind.Struct, tolerant.Schema.GetType("isf:user:outer").Kind);
    }

    /// <summary>A root name that is not in <c>user_types</c> is reported by name rather than as a generic lookup failure.</summary>
    [TestMethod]
    public void Isf_MissingRootIsReportedByName()
    {
        CStructLayoutException error = Assert.Throws<CStructLayoutException>(() => Parse(OpaqueTargets).Import("files_struct"));
        StringAssert.Contains(error.Message, "'files_struct'");
        Assert.IsNull(error.InnerException);
    }

    /// <summary>
    ///     Bitfields recorded at the byte holding their first bit move to the start of their aligned storage unit, so
    ///     strict import accepts the descriptor and every slice reads the bits a C compiler gives it.
    /// </summary>
    [TestMethod]
    public void Isf_BitfieldAtByteOffsetIsPlacedInItsStorageUnit()
    {
        MetadataImportResult imported = Parse(DescriptorByByte).Import("desc_struct");
        Assert.AreEqual(0, imported.Diagnostics.Count);
        AssertDescriptorPlacement(imported);

        // limit0 = 0xffff, base0 = 0x1234, then the two 16-bit units 0xf356 and 0x789a in little-endian order.
        byte[] bytes = [0xff, 0xff, 0x34, 0x12, 0x56, 0xf3, 0x9a, 0x78,];
        var session = new MemorySession(imported.Schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual(0x56UL, Bits(session, region, imported, "base1"));
        Assert.AreEqual(3UL, Bits(session, region, imported, "type"));
        Assert.AreEqual(1UL, Bits(session, region, imported, "s"));
        Assert.AreEqual(3UL, Bits(session, region, imported, "dpl"));
        Assert.AreEqual(1UL, Bits(session, region, imported, "p"));
        Assert.AreEqual(0xaUL, Bits(session, region, imported, "limit1"));
        Assert.AreEqual(1UL, Bits(session, region, imported, "g"));
        Assert.AreEqual(0x78UL, Bits(session, region, imported, "base2"));
    }

    /// <summary>A profile that already records storage-unit offsets imports to the same placement as one that records byte offsets.</summary>
    [TestMethod]
    public void Isf_BitfieldAtStorageUnitOffsetKeepsItsPlacement()
    {
        AssertDescriptorPlacement(Parse(DescriptorByUnit).Import("desc_struct"));
    }

    /// <summary>
    ///     ISF reads a bitfield as an integer of its storage type at <c>offset</c>, in that type's byte order, and then
    ///     selects bits counted from the integer's low bit. In a big-endian unit the low bits are in its last byte, so a
    ///     slice recorded one byte before an aligned unit moves into that unit at a higher bit, and its bytes stay put.
    /// </summary>
    [TestMethod]
    public void Isf_BigEndianBitfieldIsPlacedInItsStorageUnit()
    {
        const string BigEndian = """
            {"metadata":{"format":"6.2.0"},
             "base_types":{"be16":{"kind":"int","size":2,"signed":false,"endian":"big"}},
             "user_types":{"word":{"kind":"struct","size":4,"fields":{
               "middle":{"offset":1,"type":{"kind":"bitfield","bit_position":0,"bit_length":8,"type":{"kind":"base","name":"be16"}}}
             }}},
             "enums":{},"symbols":{}}
            """;
        MetadataImportResult imported = Parse(BigEndian).Import("word");
        MemoryField middle = imported.Schema.GetField(imported.RootTypeId, "middle");
        Assert.AreEqual(2, middle.Offset);
        Assert.AreEqual(8, middle.BitOffset);

        byte[] bytes = [0x11, 0x22, 0xab, 0x33,];
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual(0xabUL, Bits(new MemorySession(imported.Schema), region, imported, "middle"));
    }

    /// <summary>
    ///     A bitfield that crosses an aligned storage unit, as in a packed struct, cannot be moved to one; it keeps its
    ///     recorded placement when that placement fits the struct.
    /// </summary>
    [TestMethod]
    public void Isf_BitfieldThatCrossesAnAlignedUnitKeepsItsRecordedPlacement()
    {
        const string Packed = """
            {"metadata":{"format":"6.2.0"},
             "base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},
             "user_types":{"packed":{"kind":"struct","size":5,"fields":{
               "tag":{"offset":0,"type":{"kind":"bitfield","bit_position":0,"bit_length":8,"type":{"kind":"base","name":"u32"}}},
               "value":{"offset":1,"type":{"kind":"bitfield","bit_position":0,"bit_length":32,"type":{"kind":"base","name":"u32"}}}
             }}},
             "enums":{},"symbols":{}}
            """;
        MetadataImportResult imported = Parse(Packed).Import("packed");
        MemoryField value = imported.Schema.GetField(imported.RootTypeId, "value");
        Assert.AreEqual(1, value.Offset);
        Assert.AreEqual(0, value.BitOffset);

        byte[] bytes = [0x7f, 0x78, 0x56, 0x34, 0x12,];
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual(0x12345678UL, Bits(new MemorySession(imported.Schema), region, imported, "value"));
    }

    /// <summary>
    ///     An enum's own <c>size</c> is its storage width even when its base type is wider, as for a packed C enum, and
    ///     a negative constant makes its storage signed even when the recorded base type is unsigned.
    /// </summary>
    [TestMethod]
    public void Isf_EnumStorageFollowsItsSizeAndConstants()
    {
        const string Enums = """
            {"metadata":{"format":"6.2.0"},
             "base_types":{"unsigned int":{"kind":"int","size":4,"signed":false,"endian":"little"}},
             "user_types":{"file":{"kind":"struct","size":8,"fields":{
               "hint":{"offset":0,"type":{"kind":"enum","name":"rw_hint"}},
               "state":{"offset":4,"type":{"kind":"enum","name":"perf_event_state"}}
             }}},
             "enums":{
               "rw_hint":{"size":1,"base":"unsigned int","constants":{"WRITE_LIFE_NOT_SET":0,"WRITE_LIFE_SHORT":2}},
               "perf_event_state":{"size":4,"base":"unsigned int","constants":{"PERF_EVENT_STATE_DEAD":-4,"PERF_EVENT_STATE_ACTIVE":1}}
             },
             "symbols":{}}
            """;
        MetadataImportResult imported = Parse(Enums).Import("file");
        Assert.AreEqual(1, imported.Schema.GetType("isf:enum:rw_hint").Size);

        byte[] bytes = [2, 0xee, 0xee, 0xee, 0xfc, 0xff, 0xff, 0xff,];
        var session = new MemorySession(imported.Schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual("WRITE_LIFE_SHORT", ((CStructSharp.Values.EnumValueResult)session.Read(region, imported.RootTypeId, "hint")!).Name);
        Assert.AreEqual("PERF_EVENT_STATE_DEAD", ((CStructSharp.Values.EnumValueResult)session.Read(region, imported.RootTypeId, "state")!).Name);
    }

    /// <summary>
    ///     A C <c>_Bool</c> bitfield and a slice in the tail of a packed struct, too short for a whole storage unit of
    ///     the declared type, read through a narrower unsigned integer that holds the same bits.
    /// </summary>
    [TestMethod]
    public void Isf_BoolAndPackedTailBitfieldsReadThroughNarrowerStorage()
    {
        const string Packed = """
            {"metadata":{"format":"6.2.0"},
             "base_types":{
               "short int":{"kind":"int","size":2,"signed":true,"endian":"little"},
               "unsigned int":{"kind":"int","size":4,"signed":false,"endian":"little"},
               "_Bool":{"kind":"bool","size":1,"signed":false,"endian":"little"}
             },
             "user_types":{"orc_entry":{"kind":"struct","size":6,"fields":{
               "sp_offset":{"offset":0,"type":{"kind":"base","name":"short int"}},
               "called":{"offset":2,"type":{"kind":"bitfield","bit_position":0,"bit_length":1,"type":{"kind":"base","name":"_Bool"}}},
               "verified":{"offset":2,"type":{"kind":"bitfield","bit_position":1,"bit_length":1,"type":{"kind":"base","name":"_Bool"}}},
               "sp_reg":{"offset":4,"type":{"kind":"bitfield","bit_position":0,"bit_length":4,"type":{"kind":"base","name":"unsigned int"}}},
               "bp_reg":{"offset":4,"type":{"kind":"bitfield","bit_position":4,"bit_length":4,"type":{"kind":"base","name":"unsigned int"}}},
               "type":{"offset":5,"type":{"kind":"bitfield","bit_position":0,"bit_length":2,"type":{"kind":"base","name":"unsigned int"}}}
             }}},
             "enums":{},"symbols":{}}
            """;
        MetadataImportResult imported = Parse(Packed).Import("orc_entry");
        MemoryField type = imported.Schema.GetField(imported.RootTypeId, "type");
        Assert.AreEqual(4, type.Offset);
        Assert.AreEqual(8, type.BitOffset);
        Assert.AreEqual(2, imported.Schema.GetType(type.TypeId).Size);

        byte[] bytes = [0, 0, 0b10, 0, 0x3a, 0b01,];
        var session = new MemorySession(imported.Schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual(0UL, Bits(session, region, imported, "called"));
        Assert.AreEqual(1UL, Bits(session, region, imported, "verified"));
        Assert.AreEqual(0xaUL, Bits(session, region, imported, "sp_reg"));
        Assert.AreEqual(3UL, Bits(session, region, imported, "bp_reg"));
        Assert.AreEqual(1UL, Bits(session, region, imported, "type"));
    }

    /// <summary>
    ///     Symbol addresses are returned as unsigned 64-bit values; a negative JSON number, which some profile
    ///     generators write for kernel addresses at the top of the address space, is its two's-complement bit pattern.
    /// </summary>
    [TestMethod]
    public void Isf_ExposesSymbolsAndUserTypeNames()
    {
        const string WithSymbols = """
            {"metadata":{"format":"6.2.0"},"base_types":{},
             "user_types":{"task_struct":{"kind":"struct","size":0,"fields":{}},"mm_struct":{"kind":"struct","size":0,"fields":{}}},
             "enums":{},
             "symbols":{
               "init_task":{"address":18446744071603109888,"type":{"kind":"struct","name":"task_struct"}},
               "__per_cpu_start":{"address":-417333248},
               "jiffies":{"address":0}
             }}
            """;
        IsfMetadata metadata = Parse(WithSymbols);

        Assert.IsTrue(metadata.TryGetSymbol("init_task", out ulong initTask));
        Assert.AreEqual(0xffffffff82724000UL, initTask);
        Assert.IsTrue(metadata.TryGetSymbol("__per_cpu_start", out ulong perCpu));
        Assert.AreEqual(0xffffffffe7200000UL, perCpu);
        Assert.IsFalse(metadata.TryGetSymbol("missing", out ulong missing));
        Assert.AreEqual(0UL, missing);

        Assert.AreEqual(3, metadata.Symbols.Count);
        Assert.AreEqual(0UL, metadata.Symbols["jiffies"]);
        CollectionAssert.AreEqual(new[] { "mm_struct", "task_struct", }, metadata.UserTypeNames.ToArray());
    }

    /// <summary>A document without a symbol table has no symbols; a symbol without an integer address is malformed metadata.</summary>
    [TestMethod]
    public void Isf_SymbolTableIsOptionalButMustBeWellFormed()
    {
        IsfMetadata empty = Parse("""{"metadata":{"format":"6.2.0"},"base_types":{},"user_types":{},"enums":{}}""");
        Assert.AreEqual(0, empty.Symbols.Count);
        Assert.AreEqual(0, empty.UserTypeNames.Count);
        Assert.IsFalse(empty.TryGetSymbol("init_task", out _));

        foreach (string entry in new[] { """{"type":{"kind":"base","name":"u32"}}""", """{"address":1.5}""", """{"address":"0x10"}""", "3", })
        {
            IsfMetadata malformed = Parse("""{"metadata":{"format":"6.2.0"},"user_types":{},"symbols":{"broken":""" + entry + "}}");
            Assert.Throws<CStructLayoutException>(() => malformed.TryGetSymbol("broken", out _), entry);
        }

        Assert.Throws<ArgumentNullException>(() => empty.TryGetSymbol(null!, out _));
    }

    /// <summary>Parses a UTF-8 ISF document with the default limits.</summary>
    /// <param name="json">The document text.</param>
    /// <returns>The parsed document.</returns>
    private static IsfMetadata Parse(string json) => new(Encoding.UTF8.GetBytes(json));

    /// <summary>Reads an unsigned bitfield and widens it, since the boxed type of a decoded slice depends on its width.</summary>
    /// <param name="session">The session over the imported schema.</param>
    /// <param name="region">The record's bytes.</param>
    /// <param name="imported">The import whose root type the record has.</param>
    /// <param name="name">The bitfield's name.</param>
    /// <returns>The slice's value.</returns>
    private static ulong Bits(MemorySession session, MemoryRegion region, MetadataImportResult imported, string name)
        => Convert.ToUInt64(session.Read(region, imported.RootTypeId, name), System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Checks that every descriptor bitfield sits in its 16-bit storage unit at byte 4 or 6, at the low bit a C compiler assigns it.</summary>
    /// <param name="imported">The imported descriptor.</param>
    private static void AssertDescriptorPlacement(MetadataImportResult imported)
    {
        foreach ((string name, int offset, int bit) in new[]
        {
            ("base1", 4, 0), ("type", 4, 8), ("s", 4, 12), ("dpl", 4, 13), ("p", 4, 15), ("limit1", 6, 0), ("g", 6, 7), ("base2", 6, 8),
        })
        {
            MemoryField field = imported.Schema.GetField(imported.RootTypeId, name);
            Assert.AreEqual(offset, field.Offset, name);
            Assert.AreEqual(bit, field.BitOffset, name);
        }
    }
}
