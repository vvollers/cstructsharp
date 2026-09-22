namespace CStructSharp.Tests;

using CStructSharp.Expressions;
using CStructSharp.Syntax;
using CStructSharp.Writing;

/// <summary>Checks operation-owned writer state before the public writer's normalization can mask boundary errors.</summary>
[TestClass]
public class WriterStateBoundaryTests
{
    /// <summary>Unknown caller expressions require full capture while resolved variable dictionaries may use selective capture.</summary>
    [TestMethod]
    public void CapturePolicy_DistinguishesResolvedAndUnknownVariables()
    {
        using var stream = new MemoryStream();
        var selective = new CStructElementWriterState(stream, new LayoutVariables(), false, new WriteOptions());
        var full = new CStructElementWriterState(stream, new LayoutVariables { CaptureAll = true, }, false, new WriteOptions());
        var unknown = new CStructElementWriterState(stream, new Dictionary<string, Expr>(), false, new WriteOptions());
        Assert.IsFalse(selective.CaptureAllLayoutVariables);
        Assert.IsTrue(full.CaptureAllLayoutVariables);
        Assert.IsTrue(unknown.CaptureAllLayoutVariables);
    }

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
}
