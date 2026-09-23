namespace CStructSharp.Tests;

using CStructSharp.Reading;

/// <summary>Checks that fixed record plans avoid the general reader's repeated per-record allocation.</summary>
[TestClass]
public class StaticReadAllocationBoundaryTests
{
    /// <summary>Both a fixed root and a runtime-count array retain the allocation benefit of fixed record decoding.</summary>
    /// <param name="runtimeCount">Whether the array count comes from an input field rather than the declaration.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void FixedRecords_AllocateLessThanGeneralDecoding(bool runtimeCount)
    {
        string root = runtimeCount
            ? "struct root { uint16 count; record items[count]; uint8 tail; };"
            : "struct root { record items[256]; uint8 tail; };";
        var layout = new CStruct("struct record { uint8 first; uint8 second; };" + root, isLittleEndian: true);
        byte[] bytes = new byte[(256 * 2) + 1 + (runtimeCount ? 2 : 0)];
        if (runtimeCount)
        {
            bytes[1] = 1;
        }

        bytes[^1] = 99;
        dynamic parsed = layout.Parse(bytes.AsSpan(), "root");
        Assert.AreEqual(256, ((IEnumerable<object?>)parsed.items).Count());
        Assert.AreEqual((byte)99, (byte)parsed.tail);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));

        long planned = Measure(layout, bytes, false);
        long general = Measure(layout, bytes, true);
        Assert.IsTrue(planned < general, $"Fixed record plans allocated {planned} bytes; general decoding allocated {general} bytes.");

        // A valid count or depth exactly at its limit should retain the same fixed-record allocation savings.
        var generous = new ReadOptions { MaxNestingDepth = 3, MaxArrayElements = 257, };
        var boundary = new ReadOptions { MaxNestingDepth = 2, MaxArrayElements = 256, };
        Assert.AreEqual(Measure(layout, bytes, false, generous), Measure(layout, bytes, false, boundary));
    }

    /// <summary>Warms one decoding mode, then measures eight parses while restoring the caller's per-thread switch.</summary>
    /// <param name="layout">The prepared record layout, excluded from the measured allocation.</param>
    /// <param name="bytes">The complete input, shared unchanged by both decoding modes.</param>
    /// <param name="disabled">Whether to route fixed composites through the general reader.</param>
    /// <param name="options">Optional read limits; construction is outside the measurement.</param>
    /// <returns>Current-thread bytes allocated by eight completed parses.</returns>
    private static long Measure(CStruct layout, byte[] bytes, bool disabled, ReadOptions? options = null)
    {
        bool previous = StaticReadPlan.DisabledForTesting;
        StaticReadPlan.DisabledForTesting = disabled;
        try
        {
            for (int repeat = 0; repeat < 4; repeat++)
            {
                GC.KeepAlive(layout.Parse(bytes.AsSpan(), "root", options: options));
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int repeat = 0; repeat < 8; repeat++)
            {
                GC.KeepAlive(layout.Parse(bytes.AsSpan(), "root", options: options));
            }

            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
        finally
        {
            StaticReadPlan.DisabledForTesting = previous;
        }
    }
}
