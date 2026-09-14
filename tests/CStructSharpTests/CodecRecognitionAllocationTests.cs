namespace CStructSharpTests;

using CStructSharp;

/// <summary>Codec classification must not allocate for ordinary numeric values.</summary>
[TestClass]
public class CodecRecognitionAllocationTests
{
    /// <summary>Endian suffix recognition remains exact and avoids per-value temporary arrays.</summary>
    [TestMethod]
    public void FixedPointRecognition_DoesNotAllocate()
    {
        string[] names = ["uint32", "uint16>", "fixed16_16", "fixed16_16<", "fixed16_16>", "ufixed16_16", "ufixed16_16<", "ufixed16_16>", "fixed2_30", "fixed2_30<", "fixed2_30>", "ufixed8_8", "ufixed8_8<", "ufixed8_8>", "fixed16_16>>"];
        int recognized = 0;
        for (int repeat = 0; repeat < 1000; repeat++)
        {
            foreach (string name in names)
            {
                if (FixedPointCodec.IsType(name))
                {
                    recognized++;
                }
            }
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        foreach (string name in names)
        {
            if (FixedPointCodec.IsType(name))
            {
                recognized++;
            }
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(12 * 1001, recognized);
        Assert.AreEqual(0L, allocated);
    }
}
