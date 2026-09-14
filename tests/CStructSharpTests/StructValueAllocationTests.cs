namespace CStructSharpTests;

using CStructSharp;

/// <summary>Pins the per-parse allocation of the shared-shape struct result model (E2.2).</summary>
[TestClass]
public class StructValueAllocationTests
{
    private const string Layout = """
        struct leaf { uint8 kind; uint32 value; };
        struct mid { leaf first; leaf second; };
        struct pair { mid left; mid right; uint16 tag; };
        struct root { pair items[256]; };
        """;

    /// <summary>
    ///     1,280 nested struct values (256 pairs × 5 composites) plus 1,536 boxed scalars: the shape is shared, so
    ///     each struct costs one slot array and one object header, not a growing dictionary and DLR class chain.
    /// </summary>
    [TestMethod]
    public void NestedParse_AllocatesOneSlotArrayPerStruct()
    {
        var cstruct = new CStruct(Layout);
        byte[] bytes = new byte[256 * ((2 * 10) + 2)];
        new Random(7).NextBytes(bytes);
        for (int repeat = 0; repeat < 4; repeat++)
        {
            GC.KeepAlive(cstruct.Parse(bytes, "root"));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        object parsed = cstruct.Parse(bytes, "root");
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(parsed);

        // The ExpandoObject model cost ≈ 770 KB for this input; the slot model measured ≈ 511 KB, and skipping the
        // layout-variable capture of unreferenced scalars (E2.6) ≈ 425 KB on both TFMs. The remainder is the
        // per-struct placement cursor and the boxed scalars. The budget leaves room for runtime differences while
        // rejecting a return to either earlier state.
        long budget = 480_000;
        Assert.IsTrue(allocated <= budget, $"Parse allocated {allocated} bytes; budget is {budget}.");
    }
}
