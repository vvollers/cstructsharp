namespace CStructSharp.Generators.Tests;

using System;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     The generated <c>Parse</c> overloads that take <see cref="ReadLimits"/>: the same value and the same failures as the
///     overloads that take <see cref="ReadOptions"/>, on every surface that has a reader, and a read governed by limits
///     allocates no options object.
/// </summary>
[TestClass]
public class ReadLimitsParseTests
{
    private const string Layout = "struct hdr { uint8 n; uint16 values[n]; utf8 name[3]; }; struct root { hdr first; uint32 tail; };";

    private const string Consumer = """"
        using System;
        using CStructSharp;

        namespace Demo;

        [CStructLayout("struct hdr { uint8 n; uint16 values[n]; utf8 name[3]; }; struct root { hdr first; uint32 tail; };", Root = "root", Surface = GeneratedSurface.Reader)]
        public static partial class Reader { }

        [CStructLayout("struct hdr { uint8 n; uint16 values[n]; utf8 name[3]; }; struct root { hdr first; uint32 tail; };", Root = "root", Surface = GeneratedSurface.Parser)]
        public static partial class Parser { }

        public static class Probe
        {
            public static object Limited(byte[] bytes, int maxArray, bool trim)
                => Demo.Reader.Parse(bytes, new ReadLimits { MaxArrayElements = maxArray, TrimFixedText = trim });

            public static object Optioned(byte[] bytes, int maxArray, bool trim)
                => Demo.Reader.Parse(bytes, new ReadOptions { MaxArrayElements = maxArray, TrimFixedText = trim });

            public static object NamedLimited(byte[] bytes)
                => Demo.Reader.ParseHdr(bytes, default);

            public static object LimitedDepth(byte[] bytes, int depth)
                => Demo.Reader.Parse(bytes, new ReadLimits { MaxNestingDepth = depth });

            public static object OptionedDepth(byte[] bytes, int depth)
                => Demo.Reader.Parse(bytes, new ReadOptions { MaxNestingDepth = depth });

            public static long AllocatedWithLimits(byte[] bytes, int rounds)
            {
                var limits = new ReadLimits { MaxArrayElements = 100 };
                for (int i = 0; i < 200; i++) { _ = Demo.Parser.Parse(bytes, limits); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < rounds; i++) { _ = Demo.Parser.Parse(bytes, limits); }
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }

            public static long AllocatedWithOptions(byte[] bytes, int rounds)
            {
                for (int i = 0; i < 200; i++) { _ = Demo.Parser.Parse(bytes, new ReadOptions { MaxArrayElements = 100 }); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < rounds; i++) { _ = Demo.Parser.Parse(bytes, new ReadOptions { MaxArrayElements = 100 }); }
                return GC.GetAllocatedBytesForCurrentThread() - before;
            }
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates the layout on the reader and the parser surfaces and compiles the probe.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        assembly = GeneratorRunner.Run(Consumer).AssertClean().Load();
    }

    /// <summary>Calls a probe method and unwraps a failure it throws.</summary>
    /// <param name="method">The probe method.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The result.</returns>
    private static object Call(string method, params object[] arguments)
    {
        try
        {
            return assembly.GetType("Demo.Probe")!.GetMethod(method)!.Invoke(null, arguments)!;
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    /// <summary>Parsing under limits gives the value parsing under equivalent options gives, trimming text the same way.</summary>
    [TestMethod]
    public void ParseWithLimits_EqualsParseWithOptions()
    {
        byte[] bytes = [2, 1, 0, 2, 0, (byte)'a', (byte)'b', 0, 9, 0, 0, 0];
        foreach (bool trim in new[] { false, true })
        {
            object limited = Call("Limited", bytes, 10, trim);
            object optioned = Call("Optioned", bytes, 10, trim);
            ParityComparer.AssertSame(new CStruct(Layout.Replace("\\n", "\n", StringComparison.Ordinal)).Parse(bytes, "root", options: new ReadOptions { TrimFixedText = trim }), limited, "root", strict: true);
            ParityComparer.AssertSame(new CStruct(Layout).Parse(bytes, "root", options: new ReadOptions { TrimFixedText = trim }), optioned, "root", strict: true);
        }
    }

    /// <summary>A limit that the input exceeds fails with the same exception and message as the equivalent options.</summary>
    [TestMethod]
    public void ExceededLimit_FailsLikeOptions()
    {
        byte[] bytes = [3, 1, 0, 2, 0, 3, 0, (byte)'a', (byte)'b', (byte)'c', 9, 0, 0, 0];
        CStructReadLimitException limited = Assert.Throws<CStructReadLimitException>(() => Call("Limited", bytes, 2, false));
        CStructReadLimitException optioned = Assert.Throws<CStructReadLimitException>(() => Call("Optioned", bytes, 2, false));
        Assert.AreEqual(optioned.Message, limited.Message);
        StringAssert.Contains(limited.Message, "MaxArrayElements");
    }

    /// <summary>A nesting limit of zero fails the read at the root, with the exception equal options raise, and the named reader accepts the default limits.</summary>
    [TestMethod]
    public void ZeroNestingLimit_FailsLikeOptions()
    {
        byte[] bytes = [0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Exception optioned = Assert.Throws<CStructReadLimitException>(() => Call("OptionedDepth", bytes, 0));
        Exception limited = Assert.Throws<CStructReadLimitException>(() => Call("LimitedDepth", bytes, 0));
        Assert.AreEqual(optioned.Message, limited.Message);
        Assert.IsNotNull(Call("NamedLimited", new object[] { bytes }), "default limits read the value");
        Assert.Throws<ArgumentOutOfRangeException>(() => _ = new ReadLimits { InvalidText = (InvalidTextHandling)5 });
    }

    /// <summary>A read governed by limits allocates less than one that builds its own options object each call (88 bytes).</summary>
    [TestMethod]
    public void ReadWithLimits_AllocatesNoOptions()
    {
        byte[] bytes = [1, 1, 0, (byte)'a', (byte)'b', (byte)'c', 9, 0, 0, 0];
        const int Rounds = 2000;
        long withLimits = (long)Call("AllocatedWithLimits", bytes, Rounds);
        long withOptions = (long)Call("AllocatedWithOptions", bytes, Rounds);
        Assert.IsGreaterThanOrEqualTo(Rounds * 80L, withOptions - withLimits, $"limits {withLimits} bytes, options {withOptions} bytes for {Rounds} reads");
    }
}
