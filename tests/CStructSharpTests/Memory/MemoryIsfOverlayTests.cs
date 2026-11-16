namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;
using CStructSharp.Values;

/// <summary>Independent native-style ISF aliases preserve named byte views without relaxing ordinary struct validation.</summary>
[TestClass]
public class MemoryIsfOverlayTests
{
    private const string Overlaid = """
        {"metadata":{"format":"6.2.0"},
         "base_types":{
           "u16":{"kind":"int","size":2,"signed":false,"endian":"little"},
           "u32":{"kind":"int","size":4,"signed":false,"endian":"little"}
         },
         "user_types":{"record":{"kind":"struct","size":8,"fields":{
           "whole":{"offset":0,"type":{"kind":"base","name":"u32"}},
           "part":{"offset":0,"type":{"kind":"base","name":"u16"}},
           "flag":{"offset":0,"type":{"kind":"bitfield","bit_position":16,"bit_length":8,"type":{"kind":"base","name":"u32"}}},
           "tail":{"offset":6,"type":{"kind":"base","name":"u16"}}
         }}},"enums":{},"symbols":{}}
        """;

    /// <summary>Both documented revisions import native scalar records without rewriting the input version.</summary>
    /// <param name="version">The source document's declared revision.</param>
    [TestMethod]
    [DataRow("6.1.0")]
    [DataRow("6.2.0")]
    public void SupportedVersions_ImportTheirOriginalBytes(string version)
    {
        string json = "{\"metadata\":{\"format\":\"" + version + "\"},\"base_types\":{\"u32\":{\"kind\":\"int\",\"size\":4,\"signed\":false,\"endian\":\"little\"}},\"user_types\":{\"record\":{\"kind\":\"struct\",\"size\":4,\"fields\":{\"value\":{\"offset\":0,\"type\":{\"kind\":\"base\",\"name\":\"u32\"}}}}},\"enums\":{},\"symbols\":{}}";
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(json));
        MetadataImportResult imported = metadata.Import("record");
        var session = new MemorySession(imported.Schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("input", [0x78, 0x56, 0x34, 0x12,]), 0, 4);

        Assert.AreEqual(MemoryTypeKind.Struct, imported.Schema.GetType(imported.RootTypeId).Kind);
        Assert.AreEqual(0x12345678U, session.Read(region, imported.RootTypeId, "value"));
        Assert.AreEqual("struct", metadata.Describe("record").Kind);
    }

    /// <summary>Flat aliases move into one promoted union view; original paths, offsets and bit slices stay valid.</summary>
    [TestMethod]
    public void FlatAliases_GroupIntoPromotedUnionAndKeepOriginalPaths()
    {
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(Overlaid));
        MetadataImportResult imported = metadata.Import("record");
        MemoryTypeDefinition record = imported.Schema.GetType(imported.RootTypeId);
        var session = new MemorySession(imported.Schema);
        byte[] bytes = [0x78, 0x56, 0x34, 0x12, 0xA5, 0x5A, 0xEF, 0xBE,];
        var source = new ByteArrayMemorySource("input", bytes);
        var region = new MemoryRegion(source, 0, bytes.Length);

        Assert.AreEqual(MemoryTypeKind.Struct, record.Kind);
        Assert.AreEqual("struct", metadata.Describe("record").Kind);
        CollectionAssert.AreEqual(new[] { "__overlay_0", "tail", }, record.Fields.Select(field => field.Name).ToArray());
        Assert.IsTrue(record.Fields[0].Promoted);
        MemoryTypeDefinition overlay = imported.Schema.GetType(record.Fields[0].TypeId);
        Assert.AreEqual(MemoryTypeKind.Union, overlay.Kind);
        Assert.AreEqual(4, overlay.Size);
        CollectionAssert.AreEqual(new[] { "whole", "part", "flag", }, overlay.Fields.Select(field => field.Name).ToArray());
        Assert.AreEqual(6, imported.Schema.GetField(record.Id, "tail").Offset);
        Assert.AreEqual(0x12345678U, session.Read(region, record.Id, "whole"));
        Assert.AreEqual((ushort)0x5678, session.Read(region, record.Id, "part"));
        Assert.AreEqual(0x34, session.Read(region, record.Id, "flag"));
        Assert.AreEqual((ushort)0xBEEF, session.Read(region, record.Id, "tail"));

        // A whole value names the group by its overlay member; the aliased bytes round-trip from the read union.
        var group = (UnionValue)session.Read(region, record.Id, "__overlay_0")!;
        var whole = new Dictionary<string, object?> { ["__overlay_0"] = group, ["tail"] = (ushort)0xBEEF, };
        CollectionAssert.AreEqual(new byte[] { 0x78, 0x56, 0x34, 0x12, 0, 0, 0xEF, 0xBE, }, session.Serialize(record.Id, whole));
        Assert.Throws<CStructWriteException>(() => session.Serialize(record.Id, new Dictionary<string, object?> { ["whole"] = 1U, ["part"] = (ushort)2, ["tail"] = (ushort)3, }));
        session.PlanUpdate(region, record.Id, "part", (ushort)0xCAFE).Commit();
        CollectionAssert.AreEqual(new byte[] { 0xFE, 0xCA, 0x34, 0x12, 0xA5, 0x5A, 0xEF, 0xBE, }, source.ToArray());
    }

    /// <summary>Selecting one alias of a group rewrites only that group; unrelated struct members keep their values.</summary>
    [TestMethod]
    public void OverlayWrite_LeavesUnrelatedMembersIntact()
    {
        const string Profile = """
            {"metadata":{"format":"6.2.0"},"base_types":{
              "u16":{"kind":"int","size":2,"signed":false,"endian":"little"},
              "u32":{"kind":"int","size":4,"signed":false,"endian":"little"}
            },"user_types":{"record":{"kind":"struct","size":8,"fields":{
              "a":{"offset":0,"type":{"kind":"base","name":"u32"}},
              "whole":{"offset":4,"type":{"kind":"base","name":"u32"}},
              "low":{"offset":4,"type":{"kind":"base","name":"u16"}}
            }}},"enums":{},"symbols":{}}
            """;
        MetadataImportResult imported = new IsfMetadata(Encoding.UTF8.GetBytes(Profile)).Import("record");
        MemoryTypeDefinition record = imported.Schema.GetType(imported.RootTypeId);
        var session = new MemorySession(imported.Schema);

        Assert.AreEqual(MemoryTypeKind.Struct, record.Kind);
        CollectionAssert.AreEqual(new[] { "a", "__overlay_4", }, record.Fields.Select(field => field.Name).ToArray());
        var value = new Dictionary<string, object?>
        {
            ["a"] = 0x11223344U,
            ["__overlay_4"] = UnionValue.FromMember("__overlay_4", "whole", 0xAABBCCDDU),
        };
        byte[] bytes = session.Serialize(record.Id, value);
        CollectionAssert.AreEqual(new byte[] { 0x44, 0x33, 0x22, 0x11, 0xDD, 0xCC, 0xBB, 0xAA, }, bytes);
        var region = new MemoryRegion(new ByteArrayMemorySource("input", bytes), 0, bytes.Length);
        Assert.AreEqual(0x11223344U, session.Read(region, record.Id, "a"));
        Assert.AreEqual((ushort)0xCCDD, session.Read(region, record.Id, "low"));
    }

    /// <summary>A selected overlay stays addressable by its original member paths and keeps projection metadata.</summary>
    [TestMethod]
    public void SelectedAliases_PreserveProjectionAndOriginalPaths()
    {
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(Overlaid));
        MetadataImportResult imported = metadata.ImportProjection("record", ["whole", "part"]);
        var session = new MemorySession(imported.Schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("input", [0x78, 0x56, 0x34, 0x12,]), 0, 8);

        Assert.AreEqual(MemoryTypeKind.Struct, imported.Schema.GetType(imported.RootTypeId).Kind);
        Assert.IsTrue(imported.Schema.GetType(imported.RootTypeId).IsProjected);
        CollectionAssert.AreEqual(new[] { "flag", "tail", }, imported.Schema.GetType(imported.RootTypeId).UnselectedMembers.ToArray());
        Assert.AreEqual((ushort)0x5678, session.Read(region, imported.RootTypeId, "part"));
        Assert.AreEqual(0x12345678U, session.Read(region, imported.RootTypeId, "whole"));
        Assert.Throws<CStructPathException>(() => session.Read(region, imported.RootTypeId, "tail"));
    }

    /// <summary>Disjoint physical bits remain a struct even when their scalar storage units overlap in bytes.</summary>
    [TestMethod]
    public void DisjointBigEndianBits_AreNotChangedIntoAlternativeViews()
    {
        const string Profile = """
            {"metadata":{"format":"6.2.0"},"base_types":{
              "u8":{"kind":"int","size":1,"signed":false,"endian":"little"},
              "u16":{"kind":"int","size":2,"signed":false,"endian":"big"}
            },"user_types":{"record":{"kind":"struct","size":2,"fields":{
              "first":{"offset":0,"type":{"kind":"base","name":"u8"}},
              "low":{"offset":0,"type":{"kind":"bitfield","bit_position":0,"bit_length":4,"type":{"kind":"base","name":"u16"}}}
            }}},"enums":{},"symbols":{}}
            """;
        MetadataImportResult imported = new IsfMetadata(Encoding.UTF8.GetBytes(Profile)).Import("record");
        var session = new MemorySession(imported.Schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("input", [0x12, 0x3F,]), 0, 2);

        Assert.AreEqual(MemoryTypeKind.Struct, imported.Schema.GetType(imported.RootTypeId).Kind);
        Assert.AreEqual((byte)0x12, session.Read(region, imported.RootTypeId, "first"));
        Assert.AreEqual(15, session.Read(region, imported.RootTypeId, "low"));
    }

    /// <summary>Allowing native aliases does not make out-of-extent members or by-value recursive records valid.</summary>
    [TestMethod]
    public void InvalidNativeViews_StillFailTheirIndependentSchemaConstraints()
    {
        string outside = Overlaid.Replace("\"offset\":6", "\"offset\":7", StringComparison.Ordinal);
        Assert.Throws<CStructLayoutException>(() => new IsfMetadata(Encoding.UTF8.GetBytes(outside)).Import("record"));
        string recursive = Overlaid.Replace("\"whole\":{\"offset\":0,\"type\":{\"kind\":\"base\",\"name\":\"u32\"}}", "\"whole\":{\"offset\":0,\"type\":{\"kind\":\"struct\",\"name\":\"record\"}}", StringComparison.Ordinal);
        Assert.Throws<CStructLayoutException>(() => new IsfMetadata(Encoding.UTF8.GetBytes(recursive)).Import("record"));
        var scalar = new MemoryTypeDefinition("u32", "u32", MemoryTypeKind.Scalar, 4, scalarType: "uint32");
        var ordinary = new MemoryTypeDefinition("ordinary", "ordinary", MemoryTypeKind.Struct, 4, [new("first", "u32", 0), new("second", "u32", 0),]);
        Assert.Throws<CStructLayoutException>(() => new MemorySchema([scalar, ordinary]));
    }

    /// <summary>Recursive native pointers retain the requested target width and aliases do not consume extra descriptor identities.</summary>
    /// <param name="pointerSize">The captured target's pointer width in bytes.</param>
    [TestMethod]
    [DataRow(4)]
    [DataRow(8)]
    public void NativePointers_KeepTargetWidthAndDescriptorBudget(int pointerSize)
    {
        const string Profile = """
            {"metadata":{"format":"6.1.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},
             "user_types":{"record":{"kind":"struct","size":16,"fields":{
               "value":{"offset":0,"type":{"kind":"base","name":"u32"}},
               "alias":{"offset":0,"type":{"kind":"base","name":"u32"}},
               "next":{"offset":8,"type":{"kind":"pointer","subtype":{"kind":"struct","name":"record"}}}
             }}},"enums":{},"symbols":{}}
            """;
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(Profile));
        MetadataImportResult imported = metadata.Import("record", new MetadataImportOptions { PointerSize = pointerSize, MaxTypes = 3, });
        var session = new MemorySession(imported.Schema);
        byte[] bytes = new byte[32];
        bytes[0] = 42;
        bytes[8] = 16;
        bytes[16] = 99;
        var region = new MemoryRegion(new ByteArrayMemorySource("input", bytes), 0, 16);

        Assert.AreEqual(new StoredPointer(16, pointerSize), session.Read(region, imported.RootTypeId, "next"));
        Assert.AreEqual(99U, session.Read(region, imported.RootTypeId, "next.value.alias"));
        Assert.Throws<CStructLayoutException>(() => metadata.Import("record", new MetadataImportOptions { PointerSize = pointerSize, MaxTypes = 2, }));
    }
}
