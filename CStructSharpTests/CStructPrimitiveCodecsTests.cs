namespace CStructSharp.Tests;

/// <summary>
///     Regression coverage for the architecture-review optimization (docs/architecture-improvement-plan.md, AP-0.1)
///     that hoisted the direction-suffixed primitive reader/writer/alignment tables into process-wide static
///     fields shared by every <see cref="CStruct"/> instance. These tests exist specifically to prove that
///     sharing doesn't leak endianness/behavior across instances - each <see cref="CStruct"/> still resolves its
///     own unsuffixed/aliased primitive names against its own <c>IsLittleEndian</c> choice, independent of any
///     other instance built before or after it.
/// </summary>
[TestClass]
public class CStructPrimitiveCodecsTests
{
    private const string Layout = "struct root { int32 value; uint16 pair; };";

    /// <summary>
    ///     Two instances built with opposite endianness, from the same process (so both necessarily share the
    ///     static base tables), must each read/write using their own byte order - not whichever instance
    ///     happened to populate the shared static tables first.
    /// </summary>
    [TestMethod]
    public void OppositeEndiannessInstances_ReadAndWriteIndependently()
    {
        var little = new CStruct(Layout, pointerSize: 1, isLittleEndian: true);
        var big = new CStruct(Layout, pointerSize: 1, isLittleEndian: false);

        byte[] bytes = little.Serialize("root", new { value = 0x11223344, pair = (ushort)0xAABB, });

        using var littleStream = new MemoryStream(bytes);
        dynamic parsedLittle = little.ParseStream(littleStream, "root");
        Assert.AreEqual(0x11223344, parsedLittle.value);
        Assert.AreEqual((ushort)0xAABB, parsedLittle.pair);

        // The same bytes decoded with the opposite-endianness instance must NOT agree - proving `big` truly uses
        // its own byte order rather than one baked into a shared, first-instance-wins static table.
        using var bigStream = new MemoryStream(bytes);
        dynamic parsedBig = big.ParseStream(bigStream, "root");
        Assert.AreNotEqual(0x11223344, parsedBig.value);
        Assert.AreNotEqual((ushort)0xAABB, parsedBig.pair);

        byte[] bigBytes = big.Serialize("root", new { value = 0x11223344, pair = (ushort)0xAABB, });
        CollectionAssert.AreNotEqual(bytes, bigBytes);
    }

    /// <summary>
    ///     Constructing many instances in immediate succession (interleaving both endiannesses) must not corrupt
    ///     any instance's own alignment/handler resolution - a stress-shaped proof that the shared static base
    ///     tables are read-only from every instance's perspective.
    /// </summary>
    [TestMethod]
    public void InterleavedConstruction_EachInstanceKeepsItsOwnEndianness()
    {
        for (int i = 0; i < 8; i++)
        {
            bool isLittleEndian = i % 2 == 0;
            var cstruct = new CStruct(Layout, pointerSize: 1, isLittleEndian: isLittleEndian);

            byte[] bytes = cstruct.Serialize("root", new { value = 1, pair = (ushort)2, });
            using var stream = new MemoryStream(bytes);
            dynamic parsed = cstruct.ParseStream(stream, "root");

            Assert.AreEqual(1, parsed.value);
            Assert.AreEqual((ushort)2, parsed.pair);
            Assert.AreEqual(4, cstruct.GetStructSizeInBytes("root") - 2, "int32 alignment/width must stay 4 bytes regardless of construction order.");
        }
    }
}
