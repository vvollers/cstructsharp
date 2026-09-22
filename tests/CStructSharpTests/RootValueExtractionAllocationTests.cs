namespace CStructSharp.Tests;

using System.Reflection;
using CStructSharp.Compilation;
using CStructSharp.Values;

/// <summary>Checks that extracting a known root member avoids the fallback collection enumeration.</summary>
[TestClass]
[DoNotParallelize]
public class RootValueExtractionAllocationTests
{
    /// <summary>A named root lookup allocates less than finding the same value by enumeration.</summary>
    [TestMethod]
    public void NamedRootValue_AvoidsEnumerationStorage()
    {
        var value = new StructValue(new StructShape(["value"]));
        value["value"] = (byte)7;
        MethodInfo method = typeof(CStruct).GetMethod("ExtractOnlyValue", BindingFlags.NonPublic | BindingFlags.Static)!;
        var extract = method.CreateDelegate<Func<StructValue, string, object?>>();

        // Bind the same private extraction path used after root and selected-field decoding.
        Func<object?> named = () => extract(value, "value");

        // Keep enumeration as the explicit alternative whose temporary storage the fast path avoids.
        Func<object?> enumerated = () => value.Values.Single();
        for (int index = 0; index < 100; index++)
        {
            Assert.AreEqual((byte)7, named());
            Assert.AreEqual((byte)7, enumerated());
        }

        long namedBytes = long.MaxValue;
        long enumeratedBytes = long.MaxValue;
        for (int sample = 0; sample < 3; sample++)
        {
            namedBytes = Math.Min(namedBytes, Measure(named));
            enumeratedBytes = Math.Min(enumeratedBytes, Measure(enumerated));
        }

        Assert.IsTrue(namedBytes < enumeratedBytes, $"Named lookup allocated {namedBytes} bytes; enumeration allocated {enumeratedBytes}.");
    }

    /// <summary>Measures managed allocations on the current thread after delegate setup and warmup.</summary>
    /// <param name="operation">The extraction operation to repeat.</param>
    /// <returns>The bytes allocated during two hundred extractions.</returns>
    private static long Measure(Func<object?> operation)
    {
        object? last = null;
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 200; index++)
        {
            last = operation();
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        GC.KeepAlive(last);
        return allocated;
    }
}
