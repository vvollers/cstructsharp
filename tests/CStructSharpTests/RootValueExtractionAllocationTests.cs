namespace CStructSharp.Tests;

using System.Reflection;
using CStructSharp.Compilation;
using CStructSharp.Expressions;
using CStructSharp.Values;

/// <summary>Checks that ordinary reads and parses avoid unnecessary wrappers, enumeration and debug records.</summary>
[TestClass]
[DoNotParallelize]
public class RootValueExtractionAllocationTests
{
    /// <summary>The first array element reuses the known field start without constructing a stride descriptor.</summary>
    [TestMethod]
    public void FirstArrayElement_AvoidsStrideDescriptorAllocation()
    {
        var layout = new CStruct("struct root { uint8 values[2]; };");
        using var source = new MemoryStream(new byte[] { 7, 7, });

        // Index zero already has the field's resolved start.
        Func<object?> first = () =>
        {
            source.Position = 0;
            return layout.ReadValue(source, "root.values[0]");
        };

        // The next element must derive its stride from the selected element shape.
        Func<object?> second = () =>
        {
            source.Position = 0;
            return layout.ReadValue(source, "root.values[1]");
        };
        for (int index = 0; index < 100; index++)
        {
            Assert.AreEqual((byte)7, first());
            Assert.AreEqual((byte)7, second());
        }

        long firstBytes = long.MaxValue;
        long secondBytes = long.MaxValue;
        for (int sample = 0; sample < 3; sample++)
        {
            firstBytes = Math.Min(firstBytes, Measure(first));
            secondBytes = Math.Min(secondBytes, Measure(second));
        }

        Assert.IsTrue(firstBytes < secondBytes, $"First element allocated {firstBytes} bytes; second allocated {secondBytes}.");
    }

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

    /// <summary>Selected composite readers share their traversal without extra wrappers or unused debug paths.</summary>
    /// <param name="arrayElement">Whether the selected composite is an indexed array element.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void SelectedComposite_AvoidsAnExtraResultWrapper(bool arrayElement)
    {
        var layout = new CStruct(arrayElement
            ? "struct child { uint8 value; }; struct root { uint8 prefix; child nested[1]; };"
            : "struct child { uint8 value; }; struct root { uint8 prefix; child nested; };");
        string path = arrayElement ? "root.nested[0]" : "root.nested";
        using var valueInput = new MemoryStream(new byte[] { 0, 7, });
        using var parseInput = new MemoryStream(new byte[] { 0, 7, });

        // Rewind the same input so every sample decodes the same selected composite.
        Func<object?> readValue = () =>
        {
            valueInput.Position = 0;
            return layout.ReadValue(valueInput, path);
        };

        // Parse requests the same natural composite, providing the corresponding allocation baseline.
        Func<object?> parse = () =>
        {
            parseInput.Position = 0;
            return layout.Parse(parseInput, path);
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

        Assert.AreEqual(valueBytes, parseBytes, $"ReadValue allocated {valueBytes} bytes; Parse allocated {parseBytes}.");
    }

    /// <summary>Ordinary parsing avoids the extra records allocated only when debug byte ranges are requested.</summary>
    /// <param name="path">A root or nested composite path.</param>
    [TestMethod]
    [DataRow("root")]
    [DataRow("root.nested")]
    public void OrdinaryParse_AvoidsDebugRecordAllocation(string path)
    {
        var layout = new CStruct("struct child { uint8 value; }; struct root { child nested; };");
        using var ordinaryInput = new MemoryStream(new byte[] { 7, });
        using var debugInput = new MemoryStream(new byte[] { 7, });
        LayoutVariableInput variables = LayoutVariableInput.FromIntegers(null);

        // Compare the two shared cores without including the public debug-result wrapper's allocation.
        Func<object?> ordinary = () =>
        {
            ordinaryInput.Position = 0;
            return layout.ParseStreamCore(ordinaryInput, path, variables, null);
        };

        // Return only the value so neither delegate boxes the debug tuple during measurement.
        Func<object?> debug = () =>
        {
            debugInput.Position = 0;
            return layout.ParseStreamWithDebugCore(debugInput, path, variables, null).Result;
        };
        for (int index = 0; index < 100; index++)
        {
            Assert.IsInstanceOfType<StructValue>(ordinary());
            Assert.IsInstanceOfType<StructValue>(debug());
        }

        long ordinaryBytes = long.MaxValue;
        long debugBytes = long.MaxValue;
        for (int sample = 0; sample < 3; sample++)
        {
            ordinaryBytes = Math.Min(ordinaryBytes, Measure(ordinary));
            debugBytes = Math.Min(debugBytes, Measure(debug));
        }

        Assert.IsTrue(ordinaryBytes < debugBytes, $"Ordinary parsing allocated {ordinaryBytes} bytes; debug parsing allocated {debugBytes}.");
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
