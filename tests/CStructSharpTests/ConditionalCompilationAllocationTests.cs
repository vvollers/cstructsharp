namespace CStructSharpTests;

using System.Text;
using CStructSharp;

/// <summary>Protects scope compilation from recreating temporary collections for each primitive field.</summary>
[TestClass]
public class ConditionalCompilationAllocationTests
{
    /// <summary>Both a wide conditional arm and a mostly-unconditional record need bounded compiler allocation.</summary>
    [TestMethod]
    [DataRow(128, true)]
    [DataRow(1024, false)]
    public void PrimitiveScopeMetadata_HasBoundedAllocation(int count, bool grouped)
    {
        var source = new StringBuilder("struct root { uint8 tag; ");
        if (grouped)
        {
            source.Append("if (tag == 1) { ");
        }

        for (int index = 0; index < count; index++)
        {
            source.Append("uint32 f").Append(index).Append(";");
        }

        source.Append(grouped ? "} };" : "if (tag == 1) { uint8 present; } };");
        string definition = source.ToString();
        for (int repeat = 0; repeat < 8; repeat++)
        {
            GC.KeepAlive(new CStruct(definition));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        var layout = new CStruct(definition);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(layout);
        // Allow runtime/tiered-JIT differences while rejecting the previous per-primitive collection growth.
        long budget = grouped ? 400_000 : 2_400_000;
        Assert.IsTrue(allocated <= budget, $"Compiler allocated {allocated} bytes; budget is {budget}.");
    }
}
