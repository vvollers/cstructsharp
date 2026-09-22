namespace CStructSharp.Tests;

using System.Reflection;
using CStructSharp.Compilation;
using CStructSharp.Values;

/// <summary>Checks that value reads avoid unnecessary result wrappers and fallback enumeration.</summary>
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

    /// <summary>A selected composite does not need more allocation through ReadValue than through Parse.</summary>
    [TestMethod]
    public void SelectedComposite_AvoidsAnExtraResultWrapper()
    {
        var layout = new CStruct("struct child { uint8 value; }; struct root { uint8 prefix; child nested; };");
        using var valueInput = new MemoryStream(new byte[] { 0, 7, });
        using var parseInput = new MemoryStream(new byte[] { 0, 7, });

        // Rewind the same input so every sample decodes the same selected composite.
        Func<object?> readValue = () =>
        {
            valueInput.Position = 0;
            return layout.ReadValue(valueInput, "root.nested");
        };

        // Parse requests the same natural composite, providing the corresponding allocation baseline.
        Func<object?> parse = () =>
        {
            parseInput.Position = 0;
            return layout.Parse(parseInput, "root.nested");
        };
        for (int index = 0; index < 100; index++)
        {
            Assert.AreEqual((byte)7, ((StructValue)readValue()!)["value"]);
            Assert.AreEqual((byte)7, ((StructValue)parse()!)["value"]);
        }

        long valueBytes = long.MaxValue;
        long parseBytes = long.MaxValue;
        for (int sample = 0; sample < 3; sample++)
        {
            valueBytes = Math.Min(valueBytes, Measure(readValue));
            parseBytes = Math.Min(parseBytes, Measure(parse));
        }

        Assert.IsTrue(valueBytes <= parseBytes, $"ReadValue allocated {valueBytes} bytes; Parse allocated {parseBytes}.");
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
