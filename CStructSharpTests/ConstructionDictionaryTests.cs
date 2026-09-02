namespace CStructSharp.Tests;

using System.Collections;

/// <summary>Verifies the one-way construction-to-publication state transition used by compiled layouts.</summary>
[TestClass]
public class ConstructionDictionaryTests
{
    /// <summary>Discards mutable storage, retains its comparer and entries, and rejects every later write path.</summary>
    [TestMethod]
    public void Freeze_PublishesCompleteSnapshotAndRejectsLaterMutation()
    {
        var table = new ConstructionDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        table.Add("one", 1);
        table["two"] = 2;

        Assert.IsFalse(table.IsFrozen);
        Assert.Throws<InvalidOperationException>(() => _ = table.Snapshot);
        Assert.AreEqual(1, table["ONE"]);
        Assert.IsTrue(table.ContainsKey("TWO"));

        table.Freeze();

        Assert.IsTrue(table.IsFrozen);
        Assert.AreEqual(2, table.Count);
        Assert.AreEqual(1, table["ONE"]);
        Assert.AreEqual(2, table.Snapshot["TWO"]);
        Assert.IsTrue(table.TryGetValue("two", out int value));
        Assert.AreEqual(2, value);
        CollectionAssert.AreEquivalent(new[] { "one", "two", }, table.Keys.ToArray());
        CollectionAssert.AreEquivalent(new[] { 1, 2, }, table.Values.ToArray());
        Assert.HasCount(2, table.ToArray());
        Assert.HasCount(2, ((IEnumerable)table).Cast<object>());

        Assert.Throws<InvalidOperationException>(() => table["three"] = 3);
        Assert.Throws<InvalidOperationException>(() => table.Add("three", 3));
        Assert.Throws<InvalidOperationException>(
            () => table.ReplaceWith(new Dictionary<string, int> { ["three"] = 3, }));
        Assert.Throws<InvalidOperationException>(() => table.Freeze());
    }

    /// <summary>Replaces rather than merges builder entries before the irreversible publication step.</summary>
    [TestMethod]
    public void ReplaceWith_UsesConfiguredComparerAndRemovesOldEntries()
    {
        var table = new ConstructionDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        table.Add("old", 1);

        table.ReplaceWith(
            new Dictionary<string, int>
            {
                ["first"] = 2,
                ["second"] = 3,
            });
        table.Freeze();

        Assert.IsFalse(table.ContainsKey("old"));
        Assert.AreEqual(2, table["FIRST"]);
        Assert.AreEqual(3, table["SECOND"]);
    }
}
