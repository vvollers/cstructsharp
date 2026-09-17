namespace CStructSharp.Tests;

using System.Text;
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
    private static MetadataImportResult Import(string profile = Profile) => IsfMetadata.Import(Encoding.UTF8.GetBytes(profile), "root");

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
            ["overlay"] = new MemoryUnionSelection("number", 42U),
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
        Assert.Throws<ArgumentException>(() => session.Resolve(region, imported.RootTypeId, "opaque.value"));
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
            ArgumentException error = Assert.Throws<ArgumentException>(() => Import(Profile.Replace(oldValue, replacement, StringComparison.Ordinal)));
            Assert.IsFalse(string.IsNullOrWhiteSpace(error.Message));
        }

        Assert.Throws<KeyNotFoundException>(() => Import(Profile.Replace("\"name\":\"u16\"", "\"name\":\"absent\"", StringComparison.Ordinal)));
        Assert.Throws<ArgumentException>(() => IsfMetadata.Import(Encoding.UTF8.GetBytes(Profile), "root", maxBytes: 16));
        Assert.Throws<ArgumentException>(() => IsfMetadata.Import(Encoding.UTF8.GetBytes(Profile), "root", maxTypes: 1));
        Assert.Throws<ArgumentException>(() => IsfMetadata.Import(Encoding.UTF8.GetBytes(Profile), "root", maxTypes: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => IsfMetadata.Import(Encoding.UTF8.GetBytes(Profile), "root", pointerSize: 3));
        Assert.Throws<OperationCanceledException>(() => IsfMetadata.Import(Encoding.UTF8.GetBytes(Profile), "root", cancellationToken: new CancellationToken(true)));
    }
}
