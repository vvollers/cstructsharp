namespace CStructSharp.Tests;

using System.Collections;
using System.Collections.ObjectModel;
using CStructSharp.Diagnostics;
using CStructSharp.Writing;

/// <summary>Preserves eager nested-array snapshots, collection callbacks and failures while preparing write values.</summary>
[TestClass]
public class NestedArrayNormalizationTests
{
    /// <summary>The normalization retains the original collection interaction order and snapshots earlier rows before later callbacks.</summary>
    /// <param name="scenario">The valid, invalid or adversarial nested collection to compare.</param>
    [TestMethod]
    [DataRow("matrix")]
    [DataRow("cube")]
    [DataRow("short")]
    [DataRow("long")]
    [DataRow("invalid")]
    [DataRow("empty")]
    [DataRow("enormous")]
    public void Flattening_PreservesOriginalCallbacksAndOutcome(string scenario)
    {
        (object[]? expected, string[] expectedEvents, string? expectedFailure) = Observe(scenario, original: true);
        (object[]? actual, string[] actualEvents, string? actualFailure) = Observe(scenario, original: false);

        CollectionAssert.AreEqual(expectedEvents, actualEvents);
        Assert.AreEqual(expectedFailure, actualFailure);
        if (expected is not null)
        {
            CollectionAssert.AreEqual(expected, actual);
            if (scenario is "matrix" or "cube")
            {
                CollectionAssert.AreEqual(new object[] { 1, 2, 3, 4, }, actual);
            }
        }
    }

    /// <summary>A lying count and huge declared dimension fail without reserving the entire claimed matrix.</summary>
    [TestMethod]
    public void EnormousDeclaredShape_DoesNotReserveItsProduct()
    {
        // Warm both the valid normalization and failure construction before observing the adversarial allocation.
        _ = Observe("enormous", original: false);
        long before = GC.GetAllocatedBytesForCurrentThread();
        (_, _, string? failure) = Observe("enormous", original: false);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        StringAssert.Contains(failure!, "Expected an array or list for field value.");
        Assert.IsLessThan(2_000_000L, allocated, "An invalid sparse source must not allocate storage for its claimed billions of elements.");
    }

    /// <summary>Dimension errors still precede leaf conversion and leave only an already-written prefix in a stream.</summary>
    [TestMethod]
    public void InvalidLaterRow_PrecedesConversionAndLeavesThePrefix()
    {
        var layout = new CStruct("struct root { uint8 prefix; uint16 grid[2][2]; };");
        var data = new Dictionary<string, object?>
        {
            ["prefix"] = 9,
            ["grid"] = new object[] { new object[] { "invalid number", 2, }, new object[] { 3, }, },
        };
        using var destination = new MemoryStream();

        // Full dimension validation must finish before attempting to convert the invalid first leaf.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Write(destination, "root", data));
        Assert.AreEqual("Array length mismatch for grid: expected 2, got 1 (field 'grid' (uint16), in 'root', offset 1).", failure.Message);
        Assert.AreEqual(1L, destination.Position);
        CollectionAssert.AreEqual(new byte[] { 9, }, destination.ToArray());
    }

    /// <summary>Normalized matrices retain scalar write charging and exactly the same stream prefix on a budget failure.</summary>
    /// <param name="budget">The physical byte budget, including the prefix and tail.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(1)]
    [DataRow(3)]
    [DataRow(8)]
    [DataRow(10)]
    public void StreamWrite_PreservesScalarBudgetBoundaries(int budget)
    {
        var layout = new CStruct("struct root { uint8 prefix; uint16 grid[2][2]; uint8 tail; };");
        var data = new Dictionary<string, object?>
        {
            ["prefix"] = 9,
            ["grid"] = new ushort[][] { new ushort[] { 1, 2, }, new ushort[] { 3, 4, }, },
            ["tail"] = 8,
        };
        byte[] complete = [9, 1, 0, 2, 0, 3, 0, 4, 0, 8,];
        using var destination = new MemoryStream();
        var options = new WriteOptions { MaxTotalBytesWritten = budget, };
        if (budget < complete.Length)
        {
            // Every uint16 remains a single two-byte write even though normalization reserves its references together.
            _ = Assert.Throws<CStructWriteLimitException>(() => layout.Write(destination, "root", data, options: options));
        }
        else
        {
            layout.Write(destination, "root", data, options: options);
        }

        int written = budget == 0 ? 0 : budget >= complete.Length ? complete.Length : 1 + (((budget - 1) / 2) * 2);
        Assert.AreEqual((long)written, destination.Position);
        CollectionAssert.AreEqual(complete[..written], destination.ToArray());
    }

    /// <summary>Runs one fresh source through the original or candidate normalization and records every observable callback.</summary>
    /// <param name="scenario">The source shape and failure mode.</param>
    /// <param name="original">Whether to use the preserved pre-optimization normalization.</param>
    /// <returns>The complete leaves, callback log and failure type/message.</returns>
    private static (object[]? Values, string[] Events, string? Failure) Observe(string scenario, bool original)
    {
        var events = new List<string>();
        var first = new ObservedList("first", events, 1, 2);
        var second = new ObservedList("second", events, scenario == "short" ? new object[] { 3, } : scenario == "long" ? new object[] { 3, 4, 5, } : new object[] { 3, 4, });

        // The second row mutates earlier source storage; its earlier copied values must already be independent.
        second.OnCopy = () => first[0] = 99;
        object source = new ObservedList("root", events, first, scenario == "invalid" ? 42 : second);
        int[] dimensions = [2, 2,];
        if (scenario == "cube")
        {
            source = new ObservedList("outer", events, source);
            dimensions = [1, 2, 2,];
        }
        else if (scenario == "empty")
        {
            source = new ObservedList("root", events, Array.Empty<object>(), Array.Empty<object>());
            dimensions = [2, 0,];
        }
        else if (scenario == "enormous")
        {
            source = new ObservedList("root", events, new object[] { 1, }, 42) { ReportedCount = int.MaxValue, };
            dimensions = [int.MaxValue, 1,];
        }

        try
        {
            List<object> leaves = original
                ? OriginalFlatten(source, dimensions, "grid")
                : WriteValueRules.FlattenNestedArrayValues(source, dimensions, "grid");
            return (leaves.ToArray(), events.ToArray(), null);
        }
        catch (CStructWriteException failure)
        {
            return (null, events.ToArray(), failure.GetType().FullName + ": " + failure.Message);
        }
    }

    /// <summary>Retains the original normalization as an oracle for callback order, snapshots and diagnostic text.</summary>
    /// <param name="value">The nested source.</param>
    /// <param name="dimensions">The required remaining dimensions.</param>
    /// <param name="field">The field named by diagnostics.</param>
    /// <returns>The original row-major snapshot of leaf references.</returns>
    /// <exception cref="CStructWriteException">A level is invalid or has a different element count.</exception>
    private static List<object> OriginalFlatten(object value, IReadOnlyList<int> dimensions, string field)
    {
        IList<object> level = WriteValueMaterialization.ConvertToObjectList(value, dimensions[0], field);
        if (level.Count != dimensions[0])
        {
            throw new CStructWriteException(WriteFailures.ArrayLengthMismatch(field, dimensions[0], level.Count));
        }

        if (dimensions.Count == 1)
        {
            return level as List<object> ?? level.ToList();
        }

        int[] remaining = [.. dimensions.Skip(1),];
        var flattened = new List<object>();
        foreach (object item in level)
        {
            flattened.AddRange(OriginalFlatten(item, remaining, field));
        }

        return flattened;
    }

    /// <summary>Records collection access and permits adversarial counts and mutations during copying.</summary>
    private sealed class ObservedList : Collection<object>, IList<object>
    {
        private readonly string name;
        private readonly List<string> events;

        /// <summary>Creates a collection with observable access to its supplied items.</summary>
        /// <param name="name">The log prefix.</param>
        /// <param name="events">The shared ordered callback log.</param>
        /// <param name="items">The initial items.</param>
        public ObservedList(string name, List<string> events, params object[] items)
            : base(items.ToList())
        {
            this.name = name;
            this.events = events;
        }

        /// <summary>Gets or sets a deliberately false count, or null to report the real count.</summary>
        public int? ReportedCount { get; set; }

        /// <summary>Gets or sets the mutation to perform immediately before copying the elements.</summary>
        public Action? OnCopy { get; set; }

        /// <summary>Gets the observable count used by normalization and collection copying.</summary>
        int ICollection<object>.Count
        {
            get
            {
                this.events.Add(this.name + ":count");
                return this.ReportedCount ?? this.Count;
            }
        }

        /// <summary>Copies the current elements after recording the callback and applying its mutation.</summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The first destination index.</param>
        void ICollection<object>.CopyTo(object[] array, int arrayIndex)
        {
            this.events.Add(this.name + ":copy");
            this.OnCopy?.Invoke();
            this.CopyTo(array, arrayIndex);
        }

        /// <summary>Enumerates the items with observable start, item and disposal events.</summary>
        /// <returns>The observed enumerator.</returns>
        IEnumerator<object> IEnumerable<object>.GetEnumerator() => this.Trace().GetEnumerator();

        /// <summary>Enumerates the same observed items through the non-generic collection interface.</summary>
        /// <returns>The observed enumerator.</returns>
        IEnumerator IEnumerable.GetEnumerator() => this.Trace().GetEnumerator();

        /// <summary>Records the progress and disposal of a single traversal.</summary>
        /// <returns>Every item in its current order.</returns>
        private IEnumerable<object> Trace()
        {
            this.events.Add(this.name + ":start");
            try
            {
                foreach (object item in this.Items)
                {
                    this.events.Add(this.name + ":item");
                    yield return item;
                }
            }
            finally
            {
                this.events.Add(this.name + ":dispose");
            }
        }
    }
}
