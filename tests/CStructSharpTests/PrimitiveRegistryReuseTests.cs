namespace CStructSharpTests;

using CStructSharp;

/// <summary>Shared primitive metadata must not share user symbols, byte order or pointer placement.</summary>
[TestClass]
public class PrimitiveRegistryReuseTests
{
    [TestMethod]
    public void IndependentLayouts_KeepAliasesAndByteOrdersIsolated()
    {
        Parallel.For(0, 64, index =>
        {
            bool littleEndian = index % 2 == 0;
            byte pointerSize = (byte)(1 << (index % 4));
            string primitive = index % 3 == 0 ? "uint32" : "uint16";
            var layout = new CStruct($"typedef {primitive} value; struct root {{ value number; uint8 *ptr; }};", pointerSize, isLittleEndian: littleEndian);
            var data = new Dictionary<string, object> { ["number"] = 0x1234, ["ptr"] = 0 };
            byte[] bytes = layout.Serialize("root", data);
            int width = primitive == "uint32" ? 4 : 2;
            Assert.AreEqual(width + pointerSize, bytes.Length);
            Assert.AreEqual((byte)0x34, bytes[littleEndian ? 0 : width - 1]);
            Assert.AreEqual((byte)0x12, bytes[littleEndian ? 1 : width - 2]);
            Assert.AreEqual(0x1234, layout.ReadValue<int>(new MemoryStream(bytes), "root.number"));
        });
    }
}
