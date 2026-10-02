namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;
using CStructSharp.Values;

/// <summary>Independent ISF fixtures cover supported descriptors and explicit rejection of unknown representations.</summary>
[TestClass]
public class MemoryIsfCoverageTests
{
    private const string Profile = """
        {"metadata":{"format":"6.2.0"},
         "base_types":{
           "u16":{"kind":"int","size":2,"signed":false,"endian":"big"},
           "u32":{"kind":"int","size":4,"signed":false,"endian":"little"},
           "u64":{"kind":"int","size":8,"signed":false,"endian":"little"},
           "boolean":{"kind":"bool","size":1,"signed":false,"endian":"little"},
           "real":{"kind":"float","size":4,"signed":true,"endian":"little"},
           "nothing":{"kind":"void","size":0}
         },
         "user_types":{
           "root":{"kind":"class","size":48,"fields":{
             "word":{"offset":0,"type":{"kind":"base","name":"u16"}},
             "flag":{"offset":2,"type":{"kind":"base","name":"boolean"}},
             "real":{"offset":4,"type":{"kind":"base","name":"real"}},
             "choice":{"offset":8,"type":{"kind":"enum","name":"E"}},
             "items":{"offset":12,"type":{"kind":"array","count":2,"subtype":{"kind":"base","name":"u32"}}},
             "opaque":{"offset":24,"type":{"kind":"pointer","base":"u64","subtype":{"kind":"base","name":"nothing"}}},
             "callable":{"offset":32,"type":{"kind":"pointer","subtype":{"kind":"function"}}},
             "overlay":{"offset":40,"anonymous":true,"type":{"kind":"union","name":"U"}},
             "choiceAgain":{"offset":44,"type":{"kind":"enum","name":"E"}}
           }},
           "U":{"kind":"union","size":4,"fields":{"number":{"offset":0,"type":{"kind":"base","name":"u32"}}}}
         },
         "enums":{"E":{"size":4,"base":"u32","constants":{"ZERO_0":0,"ONE":1}}},"symbols":{}}
        """;

    /// <summary>Imports the independently authored profile using bounded defaults.</summary>
    private static MetadataImportResult Import(string profile = Profile) => new IsfMetadata(Encoding.UTF8.GetBytes(profile)).Import("root");

    /// <summary>Metadata-defined byte order, enum values, arrays, and promoted unions agree across read and creation.</summary>
    [TestMethod]
    public void Isf_ExercisesValueKindsAndProvenance()
    {
        MetadataImportResult imported = Import();
        Assert.AreEqual("isf:user:root", imported.RootTypeId);
        StringAssert.Contains(imported.Diagnostics.Single(), "address-only");
        Assert.AreEqual("isf:base:u16", imported.Schema.GetType("isf:base:u16").Provenance);
        var session = new MemorySession(imported.Schema);
        byte[] bytes = session.Serialize(imported.RootTypeId, new Dictionary<string, object?>
        {
            ["word"] = (ushort)0x1234,
            ["flag"] = true,
            ["real"] = 1.5F,
            ["choice"] = 1U,
            ["items"] = new uint[] { 7, 9, },
            ["opaque"] = new StoredPointer(ulong.MaxValue),
            ["callable"] = new StoredPointer(0),
            ["overlay"] = UnionValue.FromMember("U", "number", 42U),
            ["choiceAgain"] = 0U,
        });
        CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 1, 0, 0, 0, 0xc0, 0x3f, }, bytes[..8]);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
        Assert.AreEqual((ushort)0x1234, session.Read(region, imported.RootTypeId, "word"));
        Assert.AreEqual(true, session.Read(region, imported.RootTypeId, "flag"));
        Assert.AreEqual(1.5F, session.Read(region, imported.RootTypeId, "real"));
        Assert.AreEqual(9U, session.Read(region, imported.RootTypeId, "items[1]"));
        Assert.AreEqual(42U, session.Read(region, imported.RootTypeId, "number"));
        Assert.AreEqual("ONE", ((EnumValueResult)session.Read(region, imported.RootTypeId, "choice")!).Name);
        Assert.AreEqual("ZERO_0", ((EnumValueResult)session.Read(region, imported.RootTypeId, "choiceAgain")!).Name);
        Assert.AreEqual(new StoredPointer(ulong.MaxValue), session.Read(region, imported.RootTypeId, "opaque"));
        Assert.Throws<CStructPathException>(() => session.Resolve(region, imported.RootTypeId, "opaque.value"));
        var values = (StructValue)session.Read(region, imported.RootTypeId)!;
        Assert.AreEqual(42U, values["number"]);
    }

    /// <summary>Wrong versions, unsupported kinds, invalid references, and mismatched target settings fail explicitly.</summary>
    [TestMethod]
    public void Isf_RejectsMalformedRepresentations()
    {
        foreach ((string oldValue, string replacement) in new[]
        {
            ("6.2.0", "5.0.0"),
            ("\"endian\":\"big\"", "\"endian\":\"unknown\""),
            ("\"kind\":\"class\"", "\"kind\":\"unknown\""),
            ("\"kind\":\"bool\"", "\"kind\":\"unknown\""),
            ("\"kind\":\"array\"", "\"kind\":\"unknown\""),
            ("\"base\":\"u64\"", "\"base\":\"u32\""),
            ("\"count\":2", "\"count\":-1"),
            ("\"ZERO_0\"", "\"bad-name\""),
        })
        {
            CStructLayoutException error = Assert.Throws<CStructLayoutException>(() => Import(Profile.Replace(oldValue, replacement, StringComparison.Ordinal)));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message));
        }

        Assert.Throws<CStructLayoutException>(() => Import(Profile.Replace("\"name\":\"u16\"", "\"name\":\"absent\"", StringComparison.Ordinal)));
        Assert.Throws<CStructLayoutException>(() => new IsfMetadata(Encoding.UTF8.GetBytes(Profile), maxBytes: 16));
        Assert.Throws<OperationCanceledException>(() => new IsfMetadata(Encoding.UTF8.GetBytes(Profile), cancellationToken: new CancellationToken(true)));
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(Profile));
        Assert.Throws<CStructLayoutException>(() => metadata.Import("root", new MetadataImportOptions { MaxTypes = 1, }));
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Import("root", new MetadataImportOptions { MaxTypes = 0, }));
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.Import("root", new MetadataImportOptions { PointerSize = 3, }));
        Assert.Throws<OperationCanceledException>(() => metadata.Import("root", cancellationToken: new CancellationToken(true)));
    }

    /// <summary>
    ///     A pointer chain of several hundred user types imports without deep recursion: each type points
    ///     to the next, and the walk crosses every link.
    /// </summary>
    [TestMethod]
    public void Isf_ImportsAChainDeeperThanTheCallStackLimit()
    {
        const int Depth = 2000;
        var types = new StringBuilder();
        for (int index = 0; index < Depth; index++)
        {
            // The last type points back to the first, closing the chain into a cycle the walk must also end.
            string next = "t" + ((index + 1) % Depth).ToString(System.Globalization.CultureInfo.InvariantCulture);
            types.Append(index == 0 ? string.Empty : ",")
                 .Append("\"t").Append(index).Append("\":{\"kind\":\"struct\",\"size\":8,\"fields\":{\"next\":{\"offset\":0,")
                 .Append("\"type\":{\"kind\":\"pointer\",\"subtype\":{\"kind\":\"struct\",\"name\":\"").Append(next).Append("\"}}}}}");
        }

        string profile = "{\"metadata\":{\"format\":\"6.2.0\"},\"base_types\":{},\"user_types\":{" + types + "},\"enums\":{},\"symbols\":{}}";
        MetadataImportResult imported = new IsfMetadata(Encoding.UTF8.GetBytes(profile)).Import("t0");

        // Every user type and one generated pointer per type.
        Assert.AreEqual(Depth * 2, imported.Schema.Types.Count);
        Assert.AreEqual(MemoryTypeKind.Struct, imported.Schema.GetType("isf:user:t" + (Depth - 1)).Kind);
    }

    /// <summary>One parsed document serves several roots, and <see cref="MetadataImportOptions.BestEffort"/> demotes a broken type instead of failing.</summary>
    [TestMethod]
    public void Isf_ReusesOneDocumentAndHonorsBestEffort()
    {
        const string Broken = """
            {"metadata":{"format":"6.2.0"},
             "base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},
             "user_types":{
               "inner":{"kind":"struct","size":4,"fields":{"x":{"offset":100,"type":{"kind":"base","name":"u32"}}}},
               "outer":{"kind":"struct","size":4,"fields":{"inner":{"offset":0,"type":{"kind":"struct","name":"inner"}}}},
               "plain":{"kind":"struct","size":4,"fields":{"x":{"offset":0,"type":{"kind":"base","name":"u32"}}}}
             },
             "enums":{},"symbols":{}}
            """;
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(Broken));

        Assert.AreEqual(MemoryTypeKind.Struct, metadata.Import("plain").Schema.GetType("isf:user:plain").Kind);
        Assert.Throws<CStructLayoutException>(() => metadata.Import("outer"));
        MetadataImportResult tolerant = metadata.Import("outer", new MetadataImportOptions { BestEffort = true, });
        Assert.AreEqual(MemoryTypeKind.RawBytes, tolerant.Schema.GetType("isf:user:inner").Kind);
        StringAssert.Contains(string.Join('\n', tolerant.Diagnostics), "isf:user:inner");
    }

    /// <summary>Importing a small ISF profile stays within a per-import allocation budget (#57).</summary>
    /// <remarks>
    /// The profile is the one <c>MemoryAnalysisBenchmarks.ImportIsf</c> measures: a <c>u32</c> base type and an
    /// eight-byte <c>record</c> struct with one member at offset 4. An import compiles one small core layout per scalar
    /// and validates the recorded placement directly, which allocates roughly 21 KB. The 40,000-byte bound leaves room
    /// for runtime differences between target frameworks, yet fails if the schema again compiles a throwaway layout
    /// for every struct (the removed placement-check views cost about 45 KB per import on their own).
    /// </remarks>
    [TestMethod]
    public void Isf_SmallImportStaysWithinItsAllocationBudget()
    {
        const string Record = """
            {"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},
            "user_types":{"record":{"kind":"struct","size":8,"fields":{"value":{"offset":4,"type":{"kind":"base","name":"u32"}}}}},"enums":{},"symbols":{}}
            """;
        const int Imports = 50;
        const long BudgetPerImport = 40_000;
        byte[] bytes = Encoding.UTF8.GetBytes(Record);

        // Warm up so one-time JIT and static caches are not charged to the measured imports.
        for (int i = 0; i < 5; i++)
        {
            Assert.AreEqual("isf:user:record", new IsfMetadata(bytes).Import("record").RootTypeId);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < Imports; i++)
        {
            _ = new IsfMetadata(bytes).Import("record");
        }

        long perImport = (GC.GetAllocatedBytesForCurrentThread() - before) / Imports;
        Assert.IsTrue(perImport < BudgetPerImport, $"An ISF import allocated {perImport} bytes; the budget is {BudgetPerImport}.");
    }
}
