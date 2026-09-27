namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>Verifies UUID network order and Windows GUID field order independently of layout endianness.</summary>
[TestClass]
public class UuidPrimitiveTests
{
    /// <summary>A UUID is never an integer count: naming one in a count fails layout construction.</summary>
    /// <param name="type">The UUID spelling.</param>
    [TestMethod]
    [DataRow("uuid")]
    [DataRow("guid")]
    public void UuidCount_FailsConstruction(string type)
    {
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct($"struct root {{ {type} id; uint8 bytes[id]; }};", aligned: false));
        StringAssert.Contains(failure.Message, "Field 'id' is a UUID");
    }

    /// <summary>Non-symmetric identifiers retain their bits, byte ranges, and typed managed value.</summary>
    [TestMethod]
    public void IdentifierByteOrderAndAlignment_AreExplicit()
    {
        const string text = "00112233-4455-6677-8899-aabbccddeeff";
        foreach (string type in new[] { "uuid", "guid" })
        {
            byte[] expected = Convert.FromHexString(type == "uuid"
                                                       ? "00112233445566778899aabbccddeeff"
                                                       : "33221100554477668899aabbccddeeff");
            foreach (bool littleEndian in new[] { false, true })
            {
                var parser = new CStruct($"struct root {{ uint8 prefix; {type} id; uint8 tail; }};", aligned: true, isLittleEndian: littleEndian);
                byte[] bytes = parser.Serialize("root", new Dictionary<string, object?> { ["prefix"] = 1, ["id"] = text, ["tail"] = 99 });
                byte[] fullExpected = [1, .. expected, 99];
                CollectionAssert.AreEqual(fullExpected, bytes);
                Assert.AreEqual(18, parser.GetStructSizeInBytes("root"));
                using var stream = new MemoryStream(bytes);
                Assert.AreEqual(Guid.Parse(text), parser.ReadValue<Guid>(stream, "root.id"));
                stream.Position = 0;
                (dynamic parsed, IReadOnlyList<DebugData> debug) = parser.ParseWithDebug(stream, "root");
                CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed));
                DebugData entry = debug.Single(item => item.Path == "root.id");
                Assert.AreEqual(1L, entry.Start);
                Assert.AreEqual(17L, entry.End);
                stream.Position = 0;
                Assert.Throws<CStructWriteException>(() => parser.Update(stream, "root.id", "invalid"));
                CollectionAssert.AreEqual(bytes, stream.ToArray());
                stream.Position = 0;
                parser.Update(stream, "root.id", Guid.Empty);
                stream.Position = 0;
                Assert.AreEqual(Guid.Empty, parser.ReadValue<Guid>(stream, "root.id"));
            }
        }
    }
}
