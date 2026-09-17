namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     <see cref="CStruct.GetOrCompile"/> must return the same compiled instance for identical inputs, miss on any
///     differing constructor input, never cache failures, and stay bounded (E3.2 / E1.1).
/// </summary>
[TestClass]
public class CStructLayoutCacheTests
{
    private const string Layout = "struct root { uint16 kind; uint32 length; };";

    /// <summary>Identical source and options yield the same shared, still-usable compiled instance; null and default options are one key.</summary>
    [TestMethod]
    public void GetOrCompile_ReturnsSameInstance_ForIdenticalInputs()
    {
        var cache = new CStructLayoutCache();
        CStruct first = cache.GetOrCompile(Layout, 8, false, true, null);
        CStruct second = cache.GetOrCompile(Layout, 8, false, true, null);
        CStruct third = cache.GetOrCompile(Layout, 8, false, true, new CStructCompilationOptions());

        Assert.AreSame(first, second);
        Assert.AreSame(first, third, "default options and null options describe the same layout");
        Assert.AreEqual(1, cache.Count);
        dynamic parsed = first.Parse(new byte[] { 2, 0, 6, 0, 0, 0 }, "root");
        Assert.AreEqual((ushort)2, (ushort)parsed.kind);
    }

    /// <summary>Every constructor input is part of the key: pointer size, alignment, byte order, any compilation limit, or the source text forces a fresh compile.</summary>
    [TestMethod]
    public void GetOrCompile_Misses_WhenAnyConstructorInputDiffers()
    {
        var cache = new CStructLayoutCache();
        CStruct baseline = cache.GetOrCompile(Layout, 8, false, true, null);

        Assert.AreNotSame(baseline, cache.GetOrCompile(Layout, 4, false, true, null), "pointer size");
        Assert.AreNotSame(baseline, cache.GetOrCompile(Layout, 8, true, true, null), "alignment");
        Assert.AreNotSame(baseline, cache.GetOrCompile(Layout, 8, false, false, null), "byte order");
        Assert.AreNotSame(
            baseline,
            cache.GetOrCompile(Layout, 8, false, true, new CStructCompilationOptions { MaxExpressionTokens = 10 }),
            "compilation limit");
        Assert.AreNotSame(baseline, cache.GetOrCompile(Layout + " ", 8, false, true, null), "source text");
        Assert.AreEqual(6, cache.Count);

        CStruct bigEndian = cache.GetOrCompile(Layout, 8, false, false, null);
        dynamic parsed = bigEndian.Parse(new byte[] { 0, 2, 0, 0, 0, 6 }, "root");
        Assert.AreEqual(6u, (uint)parsed.length);
    }

    /// <summary>
    ///     Every <see cref="CStructCompilationOptions"/> property is a member of the cache key, so adding an option
    ///     without extending the key (two layouts compiled with different options sharing one cached instance) fails
    ///     here rather than in a caller.
    /// </summary>
    [TestMethod]
    public void CacheKey_CoversEveryCompilationOption()
    {
        Type keyType = typeof(CStructLayoutCache).GetNestedType("Key", System.Reflection.BindingFlags.NonPublic) ??
                       throw new AssertFailedException("CStructLayoutCache must declare its Key record.");
        string[] keyMembers = keyType.GetProperties().Select(property => property.Name).ToArray();
        foreach (System.Reflection.PropertyInfo option in typeof(CStructCompilationOptions).GetProperties())
        {
            CollectionAssert.Contains(keyMembers, option.Name, $"CStructCompilationOptions.{option.Name} is not part of CStructLayoutCache.Key.");
        }

        // The key members that mirror an option must also differ when the option does.
        var cache = new CStructLayoutCache();
        CStruct baseline = cache.GetOrCompile(Layout, 8, false, true, null);
        Assert.AreNotSame(baseline, cache.GetOrCompile(Layout, 8, false, true, new CStructCompilationOptions { CLongWidth = 32, }), "CLongWidth");
        Assert.AreNotSame(baseline, cache.GetOrCompile(Layout, 8, false, true, new CStructCompilationOptions { DefaultEnumStorage = "uint8", }), "DefaultEnumStorage");
        Assert.AreNotSame(baseline, cache.GetOrCompile(Layout, 8, false, true, new CStructCompilationOptions { Defined = new HashSet<string> { "X", }, }), "Defined");
        Assert.AreNotSame(baseline, cache.GetOrCompile(Layout, 8, false, true, new CStructCompilationOptions { Prelude = "#define P 1", }), "Prelude");
        Assert.AreNotSame(baseline, cache.GetOrCompile(Layout, 8, false, true, new CStructCompilationOptions { BitfieldAllocation = BitfieldAllocation.HighBitFirst, }), "BitfieldAllocation");
    }

    /// <summary>Invalid layouts and unsupported pointer sizes throw exactly as the constructor does and leave nothing behind in the cache.</summary>
    [TestMethod]
    public void GetOrCompile_DoesNotCacheFailures()
    {
        var cache = new CStructLayoutCache();
        Assert.ThrowsExactly<CStructLayoutException>(() => cache.GetOrCompile("struct root { uint16 kind", 8, false, true, null));
        Assert.ThrowsExactly<CStructLayoutException>(() => cache.GetOrCompile("struct root { uint16 kind", 8, false, true, null));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => cache.GetOrCompile(Layout, 3, false, true, null));
        Assert.AreEqual(0, cache.Count);
    }

    /// <summary>Above capacity the least recently used layout is evicted; touching an entry keeps it alive.</summary>
    [TestMethod]
    public void GetOrCompile_EvictsLeastRecentlyUsed_WhenOverCapacity()
    {
        var cache = new CStructLayoutCache(capacity: 3);
        CStruct a = cache.GetOrCompile("struct a { uint8 v; };", 8, false, true, null);
        CStruct b = cache.GetOrCompile("struct b { uint8 v; };", 8, false, true, null);
        CStruct c = cache.GetOrCompile("struct c { uint8 v; };", 8, false, true, null);
        Assert.AreSame(a, cache.GetOrCompile("struct a { uint8 v; };", 8, false, true, null), "touch a so b is the oldest");

        CStruct d = cache.GetOrCompile("struct d { uint8 v; };", 8, false, true, null);
        Assert.AreEqual(3, cache.Count);
        Assert.AreSame(a, cache.GetOrCompile("struct a { uint8 v; };", 8, false, true, null));
        Assert.AreSame(c, cache.GetOrCompile("struct c { uint8 v; };", 8, false, true, null));
        Assert.AreSame(d, cache.GetOrCompile("struct d { uint8 v; };", 8, false, true, null));
        Assert.AreNotSame(b, cache.GetOrCompile("struct b { uint8 v; };", 8, false, true, null), "b was evicted");
    }

    /// <summary>Sources larger than the retained-character budget are compiled but never retained, so one huge definition cannot evict the working set.</summary>
    [TestMethod]
    public void GetOrCompile_DoesNotRetainOversizedSources()
    {
        var cache = new CStructLayoutCache(capacity: 8, maximumSourceChars: 64);
        string large = "struct root { uint8 a; uint8 b; uint8 c; uint8 d; uint8 e; uint8 f; uint8 g; uint8 h; };";
        Assert.IsTrue(large.Length > 64);
        CStruct first = cache.GetOrCompile(large, 8, false, true, null);
        CStruct second = cache.GetOrCompile(large, 8, false, true, null);
        Assert.AreNotSame(first, second);
        Assert.AreEqual(0, cache.Count);

        CStruct small = cache.GetOrCompile("struct s { uint8 v; };", 8, false, true, null);
        Assert.AreSame(small, cache.GetOrCompile("struct s { uint8 v; };", 8, false, true, null));
        Assert.AreEqual(1, cache.Count);
    }

    /// <summary>Concurrent requests for overlapping layouts always receive correct compiled instances and never exceed capacity.</summary>
    [TestMethod]
    public void GetOrCompile_IsSafeUnderConcurrentUse()
    {
        var cache = new CStructLayoutCache(capacity: 4);
        string[] layouts = Enumerable.Range(0, 8).Select(index => $"struct s{index} {{ uint8 v; uint16 w; }};").ToArray();
        var results = new CStruct[64 * layouts.Length];
        Parallel.For(0, results.Length, index =>
        {
            results[index] = cache.GetOrCompile(layouts[index % layouts.Length], 8, false, true, null);
        });

        Assert.IsTrue(cache.Count <= 4);
        for (int index = 0; index < results.Length; index++)
        {
            Assert.AreEqual(3, results[index].GetStructSizeInBytes($"s{index % layouts.Length}"));
        }
    }

    /// <summary>The public CStruct.GetOrCompile shares instances until ClearCompiledCache, after which earlier instances remain valid.</summary>
    [TestMethod]
    public void SharedFactory_ReturnsSameInstance_UntilCleared()
    {
        const string layout = "struct shared_factory_probe { uint8 v; };";
        CStruct first = CStruct.GetOrCompile(layout);
        Assert.AreSame(first, CStruct.GetOrCompile(layout));
        CStruct.ClearCompiledCache();
        Assert.AreNotSame(first, CStruct.GetOrCompile(layout));
        Assert.AreEqual(1, first.GetStructSizeInBytes("shared_factory_probe"), "instances handed out before Clear stay usable");
    }
}
