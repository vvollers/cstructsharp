namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Memory.Metadata;

/// <summary>Preserves exact BTF name lookup outcomes across repeated calls, split tables, and concurrent readers.</summary>
[TestClass]
public class MemoryBtfNameCacheTests
{
    /// <summary>Name matching preserves case, whitespace, and Unicode spelling without normalization.</summary>
    [TestMethod]
    public void FindType_MatchesNamesExactly()
    {
        string[] names = ["value", "Value", "é", "e\u0301", " value ",];
        BtfMetadata metadata = Integers(names);
        for (int iteration = 0; iteration < 3; iteration++)
        {
            for (int index = 0; index < names.Length; index++)
            {
                // A new equal string must find the same declaration as the original name instance.
                Assert.AreEqual((uint)index + 1, metadata.FindType(new string(names[index].ToCharArray())));
            }

            AssertFailure(metadata, "VALUE", "BTF has no type named 'VALUE'.");
            AssertFailure(metadata, "value ", "BTF has no type named 'value '.");
            AssertFailure(metadata, "É", "BTF has no type named 'É'.");
        }
    }

    /// <summary>A typedef name identifies its own declaration rather than the underlying storage type.</summary>
    [TestMethod]
    public void FindType_PreservesTypedefIdentity()
    {
        var metadata = new BtfMetadata(MemoryBtfCoverageTests.Blob(
            [1, 1U << 24, 1, 8, 6, 8U << 24, 1,], "\0word\0alias\0"));
        for (int iteration = 0; iteration < 3; iteration++)
        {
            Assert.AreEqual(1U, metadata.FindType("word"));
            Assert.AreEqual(2U, metadata.FindType("alias"));
        }

        var duplicate = new BtfMetadata(MemoryBtfCoverageTests.Blob(
            [1, 1U << 24, 1, 8, 1, 8U << 24, 1,], "\0word\0"));
        AssertFailure(duplicate, "word", "BTF name 'word' is ambiguous.");
    }

    /// <summary>Missing and duplicate names keep exact diagnostics and create independent exception objects on every call.</summary>
    [TestMethod]
    public void FindType_RepeatedFailuresRemainIndependent()
    {
        BtfMetadata metadata = Integers("unique", "duplicate", "duplicate");
        for (int iteration = 0; iteration < 3; iteration++)
        {
            Assert.AreEqual(1U, metadata.FindType("unique"));
            AssertFailure(metadata, "duplicate", "BTF name 'duplicate' is ambiguous.");
            AssertFailure(metadata, "missing", "BTF has no type named 'missing'.");
        }
    }

    /// <summary>Empty names follow ordinary unique or ambiguous matching, while null always retains the missing-name error.</summary>
    [TestMethod]
    public void FindType_DistinguishesNullFromEmptyNames()
    {
        foreach (int count in new[] { 0, 1, 2, })
        {
            var names = new string[count];
            Array.Fill(names, string.Empty);
            BtfMetadata metadata = Integers(names);
            for (int iteration = 0; iteration < 3; iteration++)
            {
                AssertFailure(metadata, null, "BTF has no type named ''.");
                if (count == 1)
                {
                    Assert.AreEqual(1U, metadata.FindType(string.Empty));
                }
                else
                {
                    AssertFailure(metadata, string.Empty, count == 0 ? "BTF has no type named ''." : "BTF name '' is ambiguous.");
                }

                AssertFailure(metadata, null, "BTF has no type named ''.");
            }
        }
    }

    /// <summary>A split table includes inherited names and new collisions without changing any outcome in its base.</summary>
    [TestMethod]
    public void FindType_SplitTablesKeepTheirOwnNameOutcomes()
    {
        var basis = new BtfMetadata(MemoryBtfCoverageTests.Blob(
            [1, 1U << 24, 1, 8, 10, 1U << 24, 1, 8,], "\0baseOnly\0shared\0"));
        Assert.AreEqual(1U, basis.FindType("baseOnly"));
        Assert.AreEqual(2U, basis.FindType("shared"));
        AssertFailure(basis, "alias", "BTF has no type named 'alias'.");

        // The base string section has 17 bytes; the last typedef reuses its 'shared' name at offset 10.
        var split = new BtfMetadata(
            MemoryBtfCoverageTests.Blob([28, 8U << 24, 1, 17, 1U << 24, 1, 8, 10, 8U << 24, 2,], "moduleOnly\0alias\0"),
            basis);
        var inherited = new BtfMetadata(MemoryBtfCoverageTests.Blob([34, 8U << 24, 1,], "third\0"), split);
        for (int iteration = 0; iteration < 3; iteration++)
        {
            foreach (BtfMetadata metadata in new[] { split, inherited, })
            {
                Assert.AreEqual(1U, metadata.FindType("baseOnly"));
                Assert.AreEqual(3U, metadata.FindType("alias"));
                Assert.AreEqual(4U, metadata.FindType("moduleOnly"));
                AssertFailure(metadata, "shared", "BTF name 'shared' is ambiguous.");
            }

            Assert.AreEqual(6U, inherited.FindType("third"));
            AssertFailure(split, "third", "BTF has no type named 'third'.");
            Assert.AreEqual(2U, basis.FindType("shared"));
            AssertFailure(basis, "alias", "BTF has no type named 'alias'.");
        }
    }

    /// <summary>Separate metadata objects may assign the same name different IDs and different failure categories.</summary>
    [TestMethod]
    public void FindType_IndependentTablesDoNotShareOutcomes()
    {
        BtfMetadata first = Integers("shared", "firstOnly", "duplicate", "duplicate");
        BtfMetadata second = Integers("secondOnly", "shared", "duplicate");
        for (int iteration = 0; iteration < 3; iteration++)
        {
            Assert.AreEqual(1U, first.FindType("shared"));
            Assert.AreEqual(2U, second.FindType("shared"));
            Assert.AreEqual(2U, first.FindType("firstOnly"));
            AssertFailure(second, "firstOnly", "BTF has no type named 'firstOnly'.");
            AssertFailure(first, "secondOnly", "BTF has no type named 'secondOnly'.");
            Assert.AreEqual(1U, second.FindType("secondOnly"));
            AssertFailure(first, "duplicate", "BTF name 'duplicate' is ambiguous.");
            Assert.AreEqual(3U, second.FindType("duplicate"));
        }
    }

    /// <summary>Looking up hundreds of distinct successful and failed names preserves both early and late outcomes.</summary>
    [TestMethod]
    public void FindType_LargeNameWorkingSetPreservesOutcomes()
    {
        const int Count = 300;
        var names = new string[Count + 2];
        for (int index = 0; index < Count; index++)
        {
            names[index] = "type" + index;
        }

        names[Count] = "duplicate";
        names[Count + 1] = "duplicate";
        BtfMetadata metadata = Integers(names);
        AssertFailure(metadata, "duplicate", "BTF name 'duplicate' is ambiguous.");
        for (int index = 0; index < Count; index++)
        {
            Assert.AreEqual((uint)index + 1, metadata.FindType(names[index]));
            string missing = "missing" + index;
            AssertFailure(metadata, missing, $"BTF has no type named '{missing}'.");
        }

        for (int index = Count - 1; index >= 0; index--)
        {
            Assert.AreEqual((uint)index + 1, metadata.FindType(names[index]));
        }

        AssertFailure(metadata, "duplicate", "BTF name 'duplicate' is ambiguous.");
        AssertFailure(metadata, "missing0", "BTF has no type named 'missing0'.");
        AssertFailure(metadata, "missing299", "BTF has no type named 'missing299'.");
    }

    /// <summary>Concurrent first and repeated lookups agree on unique, missing, and ambiguous names.</summary>
    [TestMethod]
    public void FindType_ConcurrentReadersKeepIndependentFailures()
    {
        var names = new string[66];
        for (int index = 0; index < 64; index++)
        {
            names[index] = "type" + index;
        }

        names[64] = "duplicate";
        names[65] = "duplicate";
        BtfMetadata metadata = Integers(names);

        // Workers share the metadata while checking overlapping failed lookups and their own successful name.
        Parallel.For(0, 64, index =>
        {
            for (int iteration = 0; iteration < 4; iteration++)
            {
                Assert.AreEqual((uint)index + 1, metadata.FindType(names[index]));
                AssertFailure(metadata, "duplicate", "BTF name 'duplicate' is ambiguous.");
                AssertFailure(metadata, "missing", "BTF has no type named 'missing'.");
            }
        });
    }

    /// <summary>Asserts the exact repeated failure contract and verifies exception data stays private to each call.</summary>
    /// <param name="metadata">Table whose lookup is expected to fail.</param>
    /// <param name="name">Requested name, including null to exercise its existing missing-name behavior.</param>
    /// <param name="message">Exact expected diagnostic, including punctuation.</param>
    private static void AssertFailure(BtfMetadata metadata, string? name, string message)
    {
        // Capture two calls separately so reusing a cached exception instance cannot satisfy the assertions.
        CStructLayoutException first = Assert.Throws<CStructLayoutException>(() => metadata.FindType(name!));
        first.Data["caller"] = "first";

        // A repeated failure must construct its own exception without inheriting caller annotations.
        CStructLayoutException second = Assert.Throws<CStructLayoutException>(() => metadata.FindType(name!));
        Assert.AreNotSame(first, second);
        Assert.AreEqual(message, first.Message);
        Assert.AreEqual(message, second.Message);
        Assert.AreEqual(CStructErrorCode.InvalidLayout, first.Code);
        Assert.AreEqual(CStructErrorCode.InvalidLayout, second.Code);
        Assert.IsNull(first.Path);
        Assert.IsNull(second.Path);
        Assert.IsNull(first.InnerException);
        Assert.IsNull(second.InnerException);
        Assert.IsFalse(second.Data.Contains("caller"));
    }

    /// <summary>Creates one-byte integer records with the supplied names, preserving duplicates and empty names.</summary>
    /// <param name="names">Names in declaration order; each position becomes the next one-based BTF ID.</param>
    /// <returns>An independent parsed BTF table built with UTF-8 byte offsets.</returns>
    private static BtfMetadata Integers(params string[] names)
    {
        var strings = new StringBuilder("\0");
        var words = new uint[names.Length * 4];
        uint offset = 1;
        for (int index = 0; index < names.Length; index++)
        {
            words[index * 4] = offset;
            words[(index * 4) + 1] = 1U << 24;
            words[(index * 4) + 2] = 1;
            words[(index * 4) + 3] = 8;
            strings.Append(names[index]).Append('\0');
            offset += (uint)Encoding.UTF8.GetByteCount(names[index]) + 1;
        }

        return new BtfMetadata(MemoryBtfCoverageTests.Blob(words, strings.ToString()));
    }
}
