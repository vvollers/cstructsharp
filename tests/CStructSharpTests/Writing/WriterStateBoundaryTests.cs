namespace CStructSharp.Tests;

using System.Numerics;
using System.Reflection;
using CStructSharp.Compilation;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using CStructSharp.Writing;

/// <summary>
///     Checks operation-owned writer state before the public writer's normalization can mask boundary errors, and that
///     a nested field under a qualified parent reuses the existing prefix.
/// </summary>
[TestClass]
public class WriterStateBoundaryTests
{
    /// <summary>Starting at the exact nesting limit is valid, but one more or a negative depth is rejected with context.</summary>
    [TestMethod]
    public void InitialDepth_AcceptsExactLimitAndExplainsInvalidValues()
    {
        using var stream = new MemoryStream();
        var options = new WriteOptions { MaxNestingDepth = 2, };
        var state = new CStructElementWriterState(stream, [], false, options, initialStructureDepth: 2);
        Assert.AreEqual(2, state.StructureDepth);
        foreach (int depth in new[] { -1, 3, })
        {
            // The constructor validates inherited depth before this state can be used by a nested writer.
            ArgumentOutOfRangeException failure = Assert.Throws<ArgumentOutOfRangeException>(() => new CStructElementWriterState(stream, [], false, options, depth));
            Assert.AreEqual("initialStructureDepth", failure.ParamName);
            StringAssert.StartsWith(failure.Message, "The initial structure depth is outside the configured write limit.");
        }
    }

    /// <summary>A cancelled write cannot construct usable state or touch the caller's output.</summary>
    [TestMethod]
    public void Constructor_ObservesCancellationBeforeOutput()
    {
        using var stream = new MemoryStream();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Direct construction isolates the state-level check from the public operation's own early cancellation.
        OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() => new CStructElementWriterState(stream, [], false, new WriteOptions { CancellationToken = cancellation.Token, }));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.AreEqual(0L, stream.Length);
    }

    /// <summary>Zero array/string/output budgets are valid; negative budgets and zero nesting have distinct explanations.</summary>
    [TestMethod]
    public void Limits_AcceptZeroByteCountsAndDescribeInvalidSettings()
    {
        CStructElementWriterState.ValidateWriteOptions(new WriteOptions { MaxArrayElements = 0, MaxStringBytes = 0, MaxTotalBytesWritten = 0, });

        // Array counts, byte counts and nesting depth have different units and therefore different diagnostics.
        StringAssert.StartsWith(Assert.Throws<ArgumentOutOfRangeException>(() => CStructElementWriterState.ValidateWriteOptions(new WriteOptions { MaxArrayElements = -1, })).Message, "Maximum array elements cannot be negative.");
        StringAssert.StartsWith(Assert.Throws<ArgumentOutOfRangeException>(() => CStructElementWriterState.ValidateWriteOptions(new WriteOptions { MaxStringBytes = -1, })).Message, "Write byte limits cannot be negative.");
        StringAssert.StartsWith(Assert.Throws<ArgumentOutOfRangeException>(() => CStructElementWriterState.ValidateWriteOptions(new WriteOptions { MaxNestingDepth = 0, })).Message, "Maximum nesting depth must be greater than zero.");
    }

    /// <summary>Removing a captured value clears its qualified alias, and leaving the scope releases prefix state.</summary>
    [TestMethod]
    public void QualifiedScope_RemovesStaleAliasesAndPrefixStorage()
    {
        using var stream = new MemoryStream();
        var state = new CStructElementWriterState(stream, [], false, new WriteOptions());
        state.QualifiedPrefix = "nested.";
        state.Variables["count"] = new Literal(3);
        state.PublishQualified("count");
        Assert.AreSame(state.Variables["count"], state.Variables["nested.count"]);
        state.Variables.Remove("count");
        state.PublishQualified("count");
        Assert.IsFalse(state.Variables.ContainsKey("nested.count"));
        state.QualifiedPrefix = null;
        Assert.IsFalse(state.HasQualifiedPrefix);
        Assert.IsNull(state.QualifiedPrefix);
        Assert.AreEqual(0, state.Variables.Count);
    }

    /// <summary>The nested-field wrapper needs only the existing prefix restoration, not an extra prefix installation.</summary>
    [TestMethod]
    [DoNotParallelize]
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
            .CreateDelegate<Action<CompiledCompositeType, object, CStructElementWriterState, bool>>(layout);
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
            writeStruct(field.Composite!, data, controlState, false);
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
