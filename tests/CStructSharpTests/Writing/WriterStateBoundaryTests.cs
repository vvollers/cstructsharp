namespace CStructSharp.Tests;

using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Writing;

/// <summary>
///     Checks the operation-owned write state at its boundaries - the nesting limit, cancellation, the option limits, the
///     qualified names a nested struct publishes - and that a nested field under a qualified parent costs no more than the
///     flat equivalent.
/// </summary>
[TestClass]
public class WriterStateBoundaryTests
{
    /// <summary>A value exactly as deep as the nesting limit is written; one level more is rejected with the limit's text.</summary>
    [TestMethod]
    public void NestingDepth_AcceptsExactLimitAndExplainsTheNextLevel()
    {
        var layout = new CStruct("struct leaf { uint8 v; }; struct middle { leaf inner; uint8 n; uint8 items[n]; }; struct root { middle m; };");
        var value = new Dictionary<string, object?>
        {
            ["m"] = new Dictionary<string, object?> { ["inner"] = new Dictionary<string, object?> { ["v"] = (byte)7, }, ["n"] = (byte)0, ["items"] = Array.Empty<byte>(), },
        };
        CollectionAssert.AreEqual(new byte[] { 7, 0, }, layout.Serialize("root", value, options: new WriteOptions { MaxNestingDepth = 3, }));

        CStructWriteLimitException failure = Assert.Throws<CStructWriteLimitException>(() => layout.Serialize("root", value, options: new WriteOptions { MaxNestingDepth = 2, }));
        StringAssert.StartsWith(failure.Message, WriteFailures.NestingLimit.TrimEnd('.'));
    }

    /// <summary>A cancelled write fails before it touches the caller's output.</summary>
    [TestMethod]
    public void CancelledWrite_FailsBeforeOutput()
    {
        var layout = new CStruct("struct rec { uint8 n; uint8 items[n]; };");
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        OperationCanceledException failure = Assert.Throws<OperationCanceledException>(
            () => layout.Write(stream, "rec", new Dictionary<string, object?> { ["n"] = (byte)0, ["items"] = Array.Empty<byte>(), }, options: new WriteOptions { CancellationToken = cancellation.Token, }));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.AreEqual(0L, stream.Length);
    }

    /// <summary>Zero array/string/output budgets are valid; negative budgets and zero nesting have distinct explanations.</summary>
    [TestMethod]
    public void Limits_AcceptZeroByteCountsAndDescribeInvalidSettings()
    {
        WriteOptionSnapshots.ValidateWriteOptions(new WriteOptions { MaxArrayElements = 0, MaxStringBytes = 0, MaxTotalBytesWritten = 0, });

        // Array counts, byte counts and nesting depth have different units and therefore different diagnostics.
        StringAssert.StartsWith(Assert.Throws<ArgumentOutOfRangeException>(() => WriteOptionSnapshots.ValidateWriteOptions(new WriteOptions { MaxArrayElements = -1, })).Message, "Maximum array elements cannot be negative.");
        StringAssert.StartsWith(Assert.Throws<ArgumentOutOfRangeException>(() => WriteOptionSnapshots.ValidateWriteOptions(new WriteOptions { MaxStringBytes = -1, })).Message, "Write byte limits cannot be negative.");
        StringAssert.StartsWith(Assert.Throws<ArgumentOutOfRangeException>(() => WriteOptionSnapshots.ValidateWriteOptions(new WriteOptions { MaxNestingDepth = 0, })).Message, "Maximum nesting depth must be greater than zero.");
    }

    /// <summary>
    ///     While a prefix is active a captured value is published under its qualified name; a removed value removes the
    ///     qualified one, so no stale alias survives; without a prefix nothing is published.
    /// </summary>
    [TestMethod]
    public void QualifiedScope_RemovesStaleAliasesAndStopsWithoutAPrefix()
    {
        var layout = new CStruct("struct child { uint8 count; }; struct rec { child nested; uint8 items[nested.count]; };");
        QualifiedTarget[] targets = layout.Compilation.SlotTable.ReadPrograms.GetQualifiedTargets("count");
        Assert.HasCount(1, targets);
        int slot = targets[0].Slot;
        VariableSlots slots = VariableSlots.Create(layout.Compilation.SlotTable, LayoutVariableInput.FromIntegers(null));
        var state = new WriteEngineState(layout, slots, new WriteOptions());
        try
        {
            state.QualifiedPrefix = "nested.";
            state.PublishQualified(targets, SlotValue.FromLiteral(3));
            Assert.AreEqual((Int128)3, slots.Get(slot).Value);
            state.PublishQualified(targets, SlotValue.Undefined);
            Assert.AreEqual(SlotState.Undefined, slots.Get(slot).State);
            state.QualifiedPrefix = null;
            state.PublishQualified(targets, SlotValue.FromLiteral(5));
            Assert.AreEqual(SlotState.Undefined, slots.Get(slot).State, "nothing is published without a prefix");
        }
        finally
        {
            state.Release();
            slots.Dispose();
        }
    }

    /// <summary>
    ///     Writing a field through an unqualified nested struct allocates nothing beyond the flat equivalent's write: the
    ///     nested struct publishes no prefix of its own, so only the result array is allocated either way.
    /// </summary>
    [TestMethod]
    [DoNotParallelize]
    [TestCategory(TestCategories.Allocation)]
    public void UnqualifiedNestedField_AllocatesNoMoreThanTheFlatWrite()
    {
        var nested = new CStruct("struct child { uint8 value; uint8 n; uint8 items[n]; }; struct root { child nested; };");
        var flat = new CStruct("struct root { uint8 value; uint8 n; uint8 items[n]; };");
        var member = new Dictionary<string, object?> { ["value"] = (byte)7, ["n"] = (byte)0, ["items"] = Array.Empty<byte>(), };
        var nestedValue = new Dictionary<string, object?> { ["nested"] = member, };
        WriteOptions options = ExecutionPaths.NoFastPathsWrite();
        byte[] nestedBytes = nested.Serialize("root", nestedValue, options: options);
        CollectionAssert.AreEqual(flat.Serialize("root", member, options: options), nestedBytes);

        long nestedAllocated = long.MaxValue;
        long flatAllocated = long.MaxValue;
        for (int sample = 0; sample < 3; sample++)
        {
            nestedAllocated = Math.Min(nestedAllocated, Measure(() => nested.Serialize("root", nestedValue, options: options)));
            flatAllocated = Math.Min(flatAllocated, Measure(() => flat.Serialize("root", member, options: options)));
        }

        Assert.IsTrue(nestedAllocated <= flatAllocated, $"The nested write allocated {nestedAllocated} bytes; the flat write {flatAllocated}.");
    }

    /// <summary>Measures repeated synchronous writes after the layouts and pools have warmed up.</summary>
    /// <param name="operation">The write.</param>
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
