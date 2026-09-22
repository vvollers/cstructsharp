namespace CStructSharp.Tests;

using System.Numerics;
using System.Reflection;
using CStructSharp.Compilation;
using CStructSharp.Syntax;
using CStructSharp.Writing;

/// <summary>Checks that an unqualified nested field does not allocate a redundant copy of its existing outer prefix.</summary>
[TestClass]
[DoNotParallelize]
public class WriterQualifiedPrefixAllocationTests
{
    /// <summary>The nested-field wrapper needs only the existing prefix restoration, not an extra prefix installation.</summary>
    [TestMethod]
    public void UnqualifiedNestedField_AvoidsRedundantPrefixAllocation()
    {
        var layout = new CStruct("struct child { uint8 value; }; struct root { child nested; };");
        var root = (CompiledCompositeType)layout.CompiledModel.Symbols["root"].Symbol.Definition!;
        CompiledField field = root.Fields[0];
        Assert.IsFalse(field.HasQualifiedPrefix);
        Assert.IsNotNull(field.Composite);
        var data = new Dictionary<string, object?> { ["value"] = (byte)7, };
        var writeField = typeof(CStruct).GetMethod("WriteSingleFieldValue", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Func<CompiledField, object, CStructElementWriterState, BigInteger?>>(layout);
        var writeStruct = typeof(CStruct).GetMethod("WriteStruct", BindingFlags.Instance | BindingFlags.NonPublic)!
            .CreateDelegate<Action<CompiledCompositeType, object, CStructElementWriterState>>(layout);
        using var fieldOutput = new MemoryStream(new byte[1]);
        using var controlOutput = new MemoryStream(new byte[1]);
        var fieldState = new CStructElementWriterState(fieldOutput, new Dictionary<string, Expr>(), false, new WriteOptions()) { QualifiedPrefix = "outer.", };
        var controlState = new CStructElementWriterState(controlOutput, new Dictionary<string, Expr>(), false, new WriteOptions()) { QualifiedPrefix = "outer.", };

        // Exercise the same wrapper used for a scalar nested member under an already-qualified parent.
        Action wrapped = () =>
        {
            fieldOutput.Position = 0;
            _ = writeField(field, data, fieldState);
        };

        // Match the actual nested write and its required restoration without installing an unchanged prefix first.
        Action control = () =>
        {
            controlOutput.Position = 0;
            writeStruct(field.Composite!, data, controlState);
            controlState.QualifiedPrefix = "outer.";
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
        CollectionAssert.AreEqual(new byte[] { 7, }, fieldOutput.ToArray());
        CollectionAssert.AreEqual(fieldOutput.ToArray(), controlOutput.ToArray());
        Assert.IsTrue(wrappedBytes <= controlBytes, $"Nested wrapper allocated {wrappedBytes} bytes; direct write and restoration allocated {controlBytes}.");
    }

    /// <summary>Measures repeated synchronous writes after the delegates and output buffers have warmed up.</summary>
    /// <param name="operation">The already-bound write operation.</param>
    /// <returns>The managed bytes allocated on the current thread during two hundred writes.</returns>
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
