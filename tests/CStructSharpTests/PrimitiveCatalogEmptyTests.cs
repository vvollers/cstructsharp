namespace CStructSharp.Tests;

using CStructSharp.Codecs;

/// <summary>Checks that an empty custom-codec registration reuses the already-built primitive metadata.</summary>
[TestClass]
[DoNotParallelize]
public class PrimitiveCatalogEmptyTests
{
    /// <summary>Layouts without custom codecs do not allocate replacement catalog dictionaries.</summary>
    [TestMethod]
    public void EmptyRegistration_DoesNotAllocateCatalogCopies()
    {
        PrimitiveCatalog catalog = PrimitiveCatalog.For(true, 64);
        CustomCodecDescriptor[] empty = Array.Empty<CustomCodecDescriptor>();
        for (int index = 0; index < 128; index++)
        {
            _ = catalog.WithCustomCodecs(empty);
        }

        // Both the catalog and empty input are prepared outside the measurement; only registration is measured.
        int totalCodecs = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 256; index++)
        {
            totalCodecs += catalog.WithCustomCodecs(empty).CodecCount;
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(catalog.CodecCount * 256, totalCodecs);
        Assert.AreEqual(0L, allocated, "An empty registration must not copy the cached primitive metadata.");
        Assert.AreEqual(catalog.CodecIdOf("uint8"), catalog.WithCustomCodecs(empty).CodecIdOf("uint8"));
    }
}
