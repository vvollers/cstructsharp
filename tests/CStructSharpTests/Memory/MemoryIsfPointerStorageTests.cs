namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Explicit ISF pointer storage survives native/WOW64 records independently of the schema's default pointer width.</summary>
[TestClass]
public class MemoryIsfPointerStorageTests
{
    private const string Mixed = """
        {"metadata":{"format":"6.2.0"},"base_types":{
        "byte":{"kind":"int","size":1,"signed":false,"endian":"little"},
        "ptr32":{"kind":"int","size":4,"signed":false,"endian":"little"},
        "ptr64":{"kind":"int","size":8,"signed":false,"endian":"little"}},
        "user_types":{"record":{"kind":"struct","size":12,"fields":{
        "narrow":{"offset":0,"type":{"kind":"pointer","base":"ptr32","subtype":{"kind":"base","name":"byte"}}},
        "wide":{"offset":4,"type":{"kind":"pointer","base":"ptr64","subtype":{"kind":"base","name":"byte"}}}}}},
        "enums":{},"symbols":{}}
        """;

    /// <summary>Explicit stored widths decode, round-trip and update without widening or truncating a neighboring pointer.</summary>
    /// <param name="defaultWidth">Default target width for pointers without explicit storage.</param>
    [TestMethod]
    [DataRow(4)]
    [DataRow(8)]
    public void ExplicitPointerWidths_KeepStoredBitsAndNeighboringBytes(int defaultWidth)
    {
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(Mixed));
        MetadataImportResult imported = metadata.Import("record", new MetadataImportOptions { PointerSize = defaultWidth });
        var session = new MemorySession(imported.Schema);
        byte[] bytes = [0x78, 0x56, 0x34, 0x12, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01,];
        var source = new ByteArrayMemorySource("mixed-native-pointers", bytes);
        var region = new MemoryRegion(source, 0, bytes.Length);
        Assert.AreEqual(new StoredPointer(0x12345678, 4), session.Read(region, imported.RootTypeId, "narrow"));
        Assert.AreEqual(new StoredPointer(0x0123456789ABCDEF, 8), session.Read(region, imported.RootTypeId, "wide"));
        CollectionAssert.AreEqual(bytes, session.Serialize(imported.RootTypeId, session.Read(region, imported.RootTypeId)));
        session.PlanUpdate(region, imported.RootTypeId, "narrow", new StoredPointer(0xAABBCCDD, 4)).Commit();
        CollectionAssert.AreEqual(new byte[] { 0xDD, 0xCC, 0xBB, 0xAA, 0xEF, 0xCD, 0xAB, 0x89, 0x67, 0x45, 0x23, 0x01, }, source.ToArray());
    }

    /// <summary>Field-directed inspection retains each pointer's explicit representation rather than replacing it with the target default.</summary>
    [TestMethod]
    public void ProjectedPointerWidths_KeepBothRepresentations()
    {
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(Mixed));
        MetadataImportResult imported = metadata.ImportProjection("record", ["narrow", "wide"]);
        var session = new MemorySession(imported.Schema);
        var region = new MemoryRegion(new ByteArrayMemorySource("mixed", [0xFF, 0xFF, 0xFF, 0xFF, 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,]), 0, 12);
        Assert.AreEqual(new StoredPointer(uint.MaxValue, 4), session.Read(region, imported.RootTypeId, "narrow"));
        Assert.AreEqual(new StoredPointer(ulong.MaxValue - 1, 8), session.Read(region, imported.RootTypeId, "wide"));
    }

    /// <summary>An explicit storage base supplies the pointer's byte order as well as its width, overriding the schema default.</summary>
    /// <param name="baseEndian">Recorded byte order of the pointer's storage base.</param>
    /// <param name="schemaLittleEndian">Default byte order passed to the importer.</param>
    [TestMethod]
    [DataRow("big", true)]
    [DataRow("little", false)]
    public void ExplicitPointerStorage_KeepsItsByteOrder(string baseEndian, bool schemaLittleEndian)
    {
        string profile = Mixed.Replace("\"ptr32\":{\"kind\":\"int\",\"size\":4,\"signed\":false,\"endian\":\"little\"}", $"\"ptr32\":{{\"kind\":\"int\",\"size\":4,\"signed\":false,\"endian\":\"{baseEndian}\"}}", StringComparison.Ordinal);
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(profile), isLittleEndian: schemaLittleEndian);
        MetadataImportResult imported = metadata.Import("record");
        var session = new MemorySession(imported.Schema);
        byte[] bytes = [0x12, 0x34, 0x56, 0x78, 0, 0, 0, 0, 0, 0, 0, 0,];
        var region = new MemoryRegion(new ByteArrayMemorySource("ordered-pointer", bytes), 0, bytes.Length);
        ulong expected = baseEndian == "big" ? 0x12345678UL : 0x78563412UL;

        Assert.AreEqual(new StoredPointer(expected, 4), session.Read(region, imported.RootTypeId, "narrow"));
        CollectionAssert.AreEqual(bytes, session.Serialize(imported.RootTypeId, session.Read(region, imported.RootTypeId)));
    }

    /// <summary>A pointer cannot borrow floating-point, boolean or unsupported-width storage from a malformed profile.</summary>
    /// <param name="kind">Recorded base category.</param>
    /// <param name="size">Recorded storage width in bytes.</param>
    [TestMethod]
    [DataRow("float", 4)]
    [DataRow("bool", 4)]
    [DataRow("int", 3)]
    [DataRow("int", 16)]
    public void InvalidPointerStorage_RejectsNonintegralOrUnsupportedRepresentations(string kind, int size)
    {
        string profile = Mixed.Replace("\"ptr32\":{\"kind\":\"int\",\"size\":4", $"\"ptr32\":{{\"kind\":\"{kind}\",\"size\":{size}", StringComparison.Ordinal);
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(profile));
        Assert.Throws<CStructLayoutException>(() => metadata.Import("record"));
    }
}
