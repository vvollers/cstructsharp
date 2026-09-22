namespace CStructSharp.Tests;

using System.Reflection;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>Checks that a nested read does not reinstall an unchanged qualified-variable prefix.</summary>
[TestClass]
[DoNotParallelize]
public class ReaderQualifiedPrefixAllocationTests
{
    /// <summary>An unqualified nested field needs only the existing prefix restoration after reading its child.</summary>
    [TestMethod]
    public void UnqualifiedNestedField_AvoidsRedundantPrefixAllocation()
    {
        var layout = new CStruct("struct child { uint8 value; }; struct root { child nested; };");
        var root = (CompiledCompositeType)layout.CompiledModel.Symbols["root"].Symbol.Definition!;
        CompiledField field = root.Fields[0];
        Assert.IsFalse(field.HasQualifiedPrefix);
        Assert.IsNotNull(field.Composite);
        var readField = typeof(CStruct).GetMethod("HandleCStructElement", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<CStructElement, StructValue, CStructOperationContext, DebugPath?, long, bool, CompiledField?, CompositeFieldPlacementCursor?, bool>>(layout);
        using var fieldInput = new MemoryStream(new byte[] { 7, });
        using var controlInput = new MemoryStream(new byte[] { 7, });
        var settings = ReadOperationSettings.SnapshotReadOptions(null);
        var fieldState = new CStructOperationContext(fieldInput, new LayoutVariables(), false, settings) { QualifiedPrefix = "outer.", };
        var controlState = new CStructOperationContext(controlInput, new LayoutVariables(), false, settings);
        var prefixState = new CStructOperationContext(Stream.Null, new LayoutVariables(), false, settings);
        var destination = new StructValue(root.Shape);
        var controlValue = new StructValue(root.Shape);

        // Bind the same field dispatcher used under an existing qualified parent, without reflection during measurement.
        Action wrapped = () =>
        {
            fieldInput.Position = 0;
            readField(field.EffectiveField, destination, fieldState, null, -1, false, field, null, false);
        };

        // Use the same dispatcher without a prefix, then account for exactly one required prefix restoration.
        // No field is captured, so qualified-variable publishing cannot add unrelated work to either sample.
        Action control = () =>
        {
            controlInput.Position = 0;
            readField(field.EffectiveField, controlValue, controlState, null, -1, false, field, null, false);
            prefixState.QualifiedPrefix = "outer.";
        };
        for (int index = 0; index < 100; index++)
        {
            wrapped();
            control();
        }

        long wrappedBytes = long.MaxValue;
        long controlBytes = long.MaxValue;
        for (int sample = 0; sample < 3; sample++)
        {
            wrappedBytes = Math.Min(wrappedBytes, Measure(wrapped));
            controlBytes = Math.Min(controlBytes, Measure(control));
        }

        Assert.AreEqual("outer.", fieldState.QualifiedPrefix);
        Assert.AreEqual((byte)7, destination.Get<StructValue>("nested").Get<byte>("value"));
        Assert.AreEqual((byte)7, controlValue.Get<StructValue>("nested").Get<byte>("value"));
        Assert.IsTrue(wrappedBytes <= controlBytes, $"Prefixed dispatcher allocated {wrappedBytes} bytes; plain dispatcher and one restoration allocated {controlBytes}.");
    }

    /// <summary>Measures two hundred warmed synchronous reads on the current thread.</summary>
    /// <param name="operation">The bound read operation with reusable input and state.</param>
    /// <returns>The managed bytes allocated during the repeated reads.</returns>
    private static long Measure(Action operation)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 200; index++)
        {
            operation();
        }

        return GC.GetAllocatedBytesForCurrentThread() - before;
    }
}
