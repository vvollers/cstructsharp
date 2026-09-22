namespace CStructSharp.Tests;

using System.Reflection;
using CStructSharp.Diagnostics;
using CStructSharp.Streams;

/// <summary>Checks cumulative physical-output arithmetic at the signed 64-bit limit without enormous I/O.</summary>
[TestClass]
public class WriteBudgetArithmeticBoundaryTests
{
    /// <summary>The last representable charged byte is affordable, but the next byte must not wrap into a negative charge.</summary>
    [TestMethod]
    public void PhysicalOutput_RejectsOverflowAfterTheExactLimit()
    {
        using var inner = new MemoryStream(new byte[2]);
        using var stream = new WriteBudgetStream(inner, new WriteOptions { MaxTotalBytesWritten = long.MaxValue, });
        SetPreviouslyWrittenBytes(stream, long.MaxValue - 1);
        Assert.IsTrue(stream.CanAffordBlock(1, 1));
        stream.WriteByte(17);
        Assert.AreEqual(1L, inner.Position);
        Assert.IsTrue(stream.CanAffordBlock(0, 0));
        Assert.IsFalse(stream.CanAffordBlock(1, 1));

        // Overflow is rejected by accounting before the inner stream accepts another byte.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => stream.WriteByte(29));
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
        StringAssert.StartsWith(failure.Message, "Write output accounting overflowed the supported stream range");
        Assert.AreEqual(1L, inner.Position);
        CollectionAssert.AreEqual(new byte[] { 17, 0, }, inner.ToArray());
    }

    /// <summary>Sets a valid accumulated history directly so the boundary needs no exabytes of repeated writes.</summary>
    /// <param name="stream">The budget wrapper whose existing output accounting is initialized.</param>
    /// <param name="bytes">A nonnegative count within the configured budget.</param>
    /// <remarks>
    ///     Rewriting existing storage can accumulate this count independently of the file extent. Reflection is
    ///     limited to this test setup; the assertions exercise the normal preflight and write methods afterwards.
    /// </remarks>
    private static void SetPreviouslyWrittenBytes(WriteBudgetStream stream, long bytes)
    {
        FieldInfo? field = typeof(WriteBudgetStream).GetField("bytesWritten", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.IsNotNull(field, "Update the boundary fixture if the internal accounting representation changes.");
        field.SetValue(stream, bytes);
    }
}
