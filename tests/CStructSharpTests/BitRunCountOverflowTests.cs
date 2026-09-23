namespace CStructSharp.Tests;

using System.Reflection;
using System.Runtime.InteropServices;
using CStructSharp.Compilation;

/// <summary>Checks that the compiler cannot wrap a long bitfield run into a negative metadata count.</summary>
[TestClass]
[DoNotParallelize]
public class BitRunCountOverflowTests
{
    /// <summary>Repeated valid unnamed 64-bit fields cross the Int32 bit-count boundary without huge source text.</summary>
    [TestMethod]
    public void BitRun_RejectsMoreThanInt32Bits()
    {
        var layout = new CStruct("struct root { uint64 _:64; };");
        var composite = (CompiledCompositeType)layout.CompiledModel.Composites[layout.GetStruct("root")].Definition!;
        CompiledField field = composite.Fields[0];

        // Reuse identical padding metadata: measurement reads each entry, so independent copies add no evidence.
        // The reference array is 256 MiB; no binary input, giant layout source or per-field objects are allocated.
        var fields = new CompiledField[(int)((long)int.MaxValue / 64) + 1];
        Array.Fill(fields, field);
        var run = ImmutableCollectionsMarshal.AsImmutableArray(fields);
        MethodInfo measure = typeof(LayoutCompilation).GetMethod("MeasureBitfieldRuns", BindingFlags.NonPublic | BindingFlags.Static)!;

        // The checked conversion fails before any measured count is assigned back to the field metadata.
        TargetInvocationException error = Assert.Throws<TargetInvocationException>(() => measure.Invoke(null, new object?[] { run, }));
        Assert.IsInstanceOfType<OverflowException>(error.InnerException);
        Assert.AreEqual(64, field.BitRunBits);
    }
}
