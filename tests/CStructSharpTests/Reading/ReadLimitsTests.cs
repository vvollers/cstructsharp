namespace CStructSharp.Tests;

using System.Runtime.CompilerServices;
using System.Threading;
using CStructSharp;

/// <summary>
///     <see cref="ReadLimits"/>: the limits of a read as a value. Its zeroed value has the limits of a new
///     <see cref="ReadOptions"/>, every limit round-trips, it converts to and from <see cref="ReadOptions"/>, and it stays
///     small enough to pass by value.
/// </summary>
[TestClass]
public class ReadLimitsTests
{
    /// <summary>A value with nothing set, including <see langword="default"/>, has the defaults of a new <see cref="ReadOptions"/>.</summary>
    [TestMethod]
    public void Default_HasTheLimitsOfNewReadOptions()
    {
        var options = new ReadOptions();
        foreach (ReadLimits limits in new[] { default, new ReadLimits { TrimFixedText = false } })
        {
            Assert.AreEqual(options.MaxArrayElements, limits.MaxArrayElements);
            Assert.AreEqual(options.MaxStringBytes, limits.MaxStringBytes);
            Assert.AreEqual(options.MaxTotalBytesRead, limits.MaxTotalBytesRead);
            Assert.AreEqual(options.MaxNestingDepth, limits.MaxNestingDepth);
            Assert.AreEqual(options.TrimFixedText, limits.TrimFixedText);
            Assert.AreEqual(options.InvalidText, limits.InvalidText);
            Assert.AreEqual(options.CancellationToken, limits.CancellationToken);
        }
    }

    /// <summary>Every limit stores and returns its value, including the extremes and the values below the default.</summary>
    [TestMethod]
    public void EveryLimit_RoundTrips()
    {
        using var source = new CancellationTokenSource();
        var limits = new ReadLimits
        {
            MaxArrayElements = 0,
            MaxStringBytes = long.MaxValue,
            MaxTotalBytesRead = 1,
            MaxNestingDepth = int.MaxValue,
            TrimFixedText = true,
            InvalidText = InvalidTextHandling.Replace,
            CancellationToken = source.Token,
        };
        Assert.AreEqual(0, limits.MaxArrayElements);
        Assert.AreEqual(long.MaxValue, limits.MaxStringBytes);
        Assert.AreEqual(1L, limits.MaxTotalBytesRead);
        Assert.AreEqual(int.MaxValue, limits.MaxNestingDepth);
        Assert.IsTrue(limits.TrimFixedText);
        Assert.AreEqual(InvalidTextHandling.Replace, limits.InvalidText);
        Assert.AreEqual(source.Token, limits.CancellationToken);
        var negative = new ReadLimits { MaxStringBytes = long.MinValue, MaxArrayElements = int.MinValue };
        Assert.AreEqual(long.MinValue, negative.MaxStringBytes);
        Assert.AreEqual(int.MinValue, negative.MaxArrayElements);
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ReadLimits { InvalidText = (InvalidTextHandling)7 });
    }

    /// <summary>Limits compare by value and convert to and from <see cref="ReadOptions"/> without losing a limit.</summary>
    [TestMethod]
    public void ConvertsToAndFromReadOptions()
    {
        var options = new ReadOptions { MaxArrayElements = 7, MaxStringBytes = 99, MaxTotalBytesRead = 1234, MaxNestingDepth = 3, TrimFixedText = true, InvalidText = InvalidTextHandling.Replace };
        ReadLimits limits = ReadLimits.From(options);
        Assert.AreEqual(new ReadLimits { MaxArrayElements = 7, MaxStringBytes = 99, MaxTotalBytesRead = 1234, MaxNestingDepth = 3, TrimFixedText = true, InvalidText = InvalidTextHandling.Replace }, limits);
        ReadOptions back = limits.ToOptions();
        Assert.AreEqual(options, back);
        Assert.AreEqual(default, ReadLimits.From(null));
        Assert.AreNotEqual(default, limits);
    }

    /// <summary>The value is passed by value to every read, so it stays small.</summary>
    [TestMethod]
    public void IsSmall()
    {
        Assert.IsLessThanOrEqualTo(40, Unsafe.SizeOf<ReadLimits>());
    }
}
