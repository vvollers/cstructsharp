namespace CStructSharpTests;

using CStructSharp;

/// <summary>Verifies UUID network order and Windows GUID field order independently of layout endianness.</summary>
[TestClass]
public class IdentifierTests
{
    /// <summary>Identifiers cannot act as integer counts through a stale caller value or writer string.</summary>
    [TestMethod]
    public void Identifiers_RemoveShadowedNumericVariables()
    {
        foreach (string type in new[] { "uuid", "guid" })
        {
            var parser = new CStruct($"struct root {{ {type} id; uint8 bytes[id]; }};", aligned: false);
            var variables = new Dictionary<string, int> { ["id"] = 1 };
            Assert.Throws<CStructLayoutException>(() => parser.ParseStream(new MemoryStream(new byte[17]), "root", variables: variables));
            Assert.Throws<CStructLayoutException>(() => parser.ResolveAddress(new MemoryStream(new byte[17]), "root.bytes[0]", variables: variables));
            Assert.Throws<CStructLayoutException>(() => parser.Serialize("root", new { id = Guid.Empty, bytes = new byte[] { 0 } }, variables: variables));
            Assert.Throws<CStructLayoutException>(() => parser.Serialize("root", new { id = Guid.Empty.ToString("D"), bytes = new byte[] { 0 } }, variables: variables));
        }
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
                byte[] bytes = parser.Serialize("root", new { prefix = 1, id = text, tail = 99 });
                byte[] fullExpected = [1, .. expected, 99];
                CollectionAssert.AreEqual(fullExpected, bytes);
                Assert.AreEqual(18, parser.GetStructSizeInBytes("root"));
                using var stream = new MemoryStream(bytes);
                Assert.AreEqual(Guid.Parse(text), parser.ReadValue<Guid>(stream, "root.id"));
                stream.Position = 0;
                (List<DebugData> debug, dynamic parsed) = parser.ParseStreamWithDebug(stream, "root");
                CollectionAssert.AreEqual(bytes, parser.Serialize("root", parsed.root));
                DebugData entry = debug.Single(item => item.DebugStackString == "root.id");
                Assert.AreEqual(1L, entry.CurPos);
                Assert.AreEqual(17L, entry.EndPos);
                stream.Position = 0;
                Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.id", "invalid"));
                CollectionAssert.AreEqual(bytes, stream.ToArray());
                stream.Position = 0;
                parser.UpdateStream(stream, "root.id", Guid.Empty);
                stream.Position = 0;
                Assert.AreEqual(Guid.Empty, parser.ReadValue<Guid>(stream, "root.id"));
            }
        }
    }
}
