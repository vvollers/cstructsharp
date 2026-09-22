namespace CStructSharp.Tests;

using System.Collections.Concurrent;
using System.Reflection;
using CStructSharp.Addressing;

/// <summary>Checks that the path parsing optimization retains a bounded number of successful results.</summary>
[TestClass]
[DoNotParallelize]
public class PathCacheBoundaryTests
{
    /// <summary>The cache fills to its declared capacity, then parses new paths without retaining more entries.</summary>
    [TestMethod]
    public void Retention_StopsAtTheDeclaredCapacity()
    {
        // Inspect only the retained-entry count, not cached result identity. Reflection keeps this resource
        // invariant test from requiring a production API solely for test access to private cache storage.
        FieldInfo? cacheField = typeof(CStructPathResolver).GetField("Cache", BindingFlags.NonPublic | BindingFlags.Static);
        FieldInfo? capacityField = typeof(CStructPathResolver).GetField("CacheCapacity", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(cacheField);
        Assert.IsNotNull(capacityField);
        var cache = (ConcurrentDictionary<string, PathSegment[]>)cacheField.GetValue(null)!;
        int capacity = (int)capacityField.GetRawConstantValue()!;
        KeyValuePair<string, PathSegment[]>[] previous = cache.ToArray();
        cache.Clear();

        try
        {
            for (int index = 0; index < capacity * 2; index++)
            {
                string member = "member" + index;
                IReadOnlyList<PathSegment> parsed = CStructPathResolver.Parse("root." + member);
                Assert.AreEqual(2, parsed.Count);
                Assert.AreEqual("root", parsed[0].Name);
                Assert.AreEqual(member, parsed[1].Name);
                Assert.AreEqual(Math.Min(index + 1, capacity), cache.Count, "Retained paths must respect the sequential capacity boundary.");
            }
        }
        finally
        {
            // Restore the exact prior cache entries, even when a mutant causes an assertion to fail.
            cache.Clear();
            foreach (KeyValuePair<string, PathSegment[]> entry in previous)
            {
                cache.TryAdd(entry.Key, entry.Value);
            }
        }
    }
}
