namespace CStructSharp.Tests;

using System.Collections;
using CStructSharp.Compilation;

/// <summary>Verifies the one-way construction-to-publication state transition used by compiled layouts.</summary>
[TestClass]
public class ConstructionDictionaryTests
{
    /// <summary>Local entries shadow a shared baseline in lookup, count, enumeration and collection copies.</summary>
    [TestMethod]
    public void Baseline_ShadowingPreservesEveryVisibleEntryExactlyOnce()
    {
        var baseline = new Dictionary<string, int> { ["base"] = 1, ["shared"] = 2, };
        var table = new ConstructionDictionary<string, int>(StringComparer.Ordinal, baseline);
        table.Add("local", 3);
        table.Add("shared", 4);
        Assert.AreEqual(3, table.Count);
        Assert.AreEqual(1, table["base"]);
        Assert.AreEqual(4, table["shared"]);
        CollectionAssert.AreEquivalent(new[] { "base", "local", "shared", }, table.Keys.ToArray());
        CollectionAssert.AreEquivalent(new[] { 1, 3, 4, }, table.Values.ToArray());
        var collection = (ICollection<KeyValuePair<string, int>>)table;
        Assert.IsTrue(collection.Contains(new KeyValuePair<string, int>("base", 1)));
        Assert.IsTrue(collection.Contains(new KeyValuePair<string, int>("shared", 4)));
        Assert.IsFalse(collection.Contains(new KeyValuePair<string, int>("shared", 2)));
        Assert.IsFalse(collection.Contains(new KeyValuePair<string, int>("missing", 0)));
        var copied = new KeyValuePair<string, int>[5];
        collection.CopyTo(copied, 1);
        CollectionAssert.AreEquivalent(table.ToArray(), copied[1..4]);
        Assert.AreEqual(default, copied[0]);
        Assert.AreEqual(default, copied[4]);
        Assert.AreEqual(2, baseline["shared"], "Shadowing must not mutate the shared table.");
    }

    /// <summary>The collection facade rejects every mutator before and after publication with an actionable reason.</summary>
    /// <param name="freeze">Whether the construction phase has ended.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CollectionView_IsReadOnlyThroughoutConstruction(bool freeze)
    {
        var table = new ConstructionDictionary<string, int>();
        table.Add("value", 7);
        if (freeze)
        {
            table.Freeze();
        }

        var view = (ICollection<KeyValuePair<string, int>>)table;
        Assert.IsTrue(view.IsReadOnly);
        var pair = new KeyValuePair<string, int>("value", 7);

        // Only the explicit builder methods may mutate this lookup; ICollection is always a read-only facade.
        Assert.AreEqual("The construction dictionary is read-only through its collection view.", Assert.Throws<NotSupportedException>(() => view.Add(pair)).Message);
        Assert.AreEqual("The construction dictionary is read-only through its collection view.", Assert.Throws<NotSupportedException>(() => view.Clear()).Message);
        Assert.AreEqual("The construction dictionary is read-only through its collection view.", Assert.Throws<NotSupportedException>(() => view.Remove(pair)).Message);
        Assert.AreEqual(1, table.Count);
        Assert.AreEqual(7, table["value"]);
    }

    /// <summary>Snapshot, frozen-builder and missing-key failures identify the specific invalid operation.</summary>
    [TestMethod]
    public void Diagnostics_DistinguishPublicationAndLookupFailures()
    {
        var table = new ConstructionDictionary<string, int>();

        // An unpublished view is a state error, while a missing entry is a lookup error.
        Assert.AreEqual("The construction dictionary has not been frozen.", Assert.Throws<InvalidOperationException>(() => _ = table.Snapshot).Message);
        Assert.AreEqual("The key 'missing' was not present in the dictionary.", Assert.Throws<KeyNotFoundException>(() => _ = table["missing"]).Message);
        table.Freeze();

        // A second publication must not reopen or silently reuse a mutable builder.
        Assert.AreEqual("The construction dictionary is already frozen.", Assert.Throws<InvalidOperationException>(() => table.Freeze()).Message);
    }

    /// <summary>
    ///     The builder starts with case-insensitive entries one = 1 and two = 2.
    /// </summary>
    /// <remarks>
    ///     Freezing must retain both entries and the comparer while rejecting all later mutation and a second freeze.
    ///     This internal helper supports publishing stable layout metadata after construction.
    /// </remarks>
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

    /// <summary>
    ///     Before freezing, ReplaceWith must discard old and install first = 2 and second = 3.
    /// </summary>
    /// <remarks>
    ///     After freezing, uppercase lookups must still work because the configured comparer is case-insensitive.
    ///     Replacement must not accidentally merge stale construction entries into the final snapshot.
    /// </remarks>
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
