namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     The non-throwing forms of constraint checking: <c>TryParse</c> of a fixed struct, a view's <c>TryValidate</c> and the
///     static <c>Matches</c> report a value that breaks a condition without any exception being thrown (counted with the
///     runtime's first-chance exception event), and report what the throwing forms and the runtime layout report.
/// </summary>
[TestClass]
public class NonThrowingConstraintTests
{
    private const string Fixed = "struct fx { uint32 a @expect(a != 0, \\\"fx.a\\\"); uint32 b @expect(b > a) @supports(b < 100, \\\"fx.b\\\"); uint32 c; };";

    private const string Probe = "enum kind : uint8 { Ok = 1, Bad = 2 }; #define LIMIT 10\\nstruct probe { uint16 magic @expect(magic == 0xEF53, \\\"probe.magic\\\"); uint8 version @supports(version <= 2) @expect(version != 0); kind k @expect(k != kind.Bad); uint8 lo:4 @expect(lo != 15); uint8 hi:4 @expect(hi < LIMIT); uint8 n; uint8 data[n]; uint8 after @expect(after == 7); };";

    private static readonly string RuntimeFixed = Fixed.Replace("\\\"", "\"", StringComparison.Ordinal);

    private static readonly string RuntimeProbe = Probe.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\n", "\n", StringComparison.Ordinal);

    private static readonly string Consumer = """"
        using System;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader)]
        public static partial class FixedReader { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Full)]
        public static partial class FixedFull { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader, Shape = GeneratedShape.Struct)]
        public static partial class FixedValue { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader, RuntimeByteOrder = true)]
        public static partial class FixedOrder { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Views)]
        public static partial class FixedViews { }

        [CStructLayout("@PROBE@", Root = "probe", Surface = GeneratedSurface.Parser, Views = false)]
        public static partial class ProbeParser { }

        [CStructLayout("@PROBE@", Root = "probe", Surface = GeneratedSurface.Full)]
        public static partial class ProbeFull { }

        [CStructLayout("@PROBE@", Root = "probe", Surface = GeneratedSurface.Reader, RuntimeByteOrder = true)]
        public static partial class ProbeOrder { }

        public static class Calls
        {
            public static (bool Ok, CStructException? Failure) FixedSpan(byte[] bytes)
            {
                bool ok = FixedReader.TryParse(bytes, out FixedReader.Fx? value, out CStructException? failure);
                return (ok, failure);
            }

            public static (bool Ok, CStructException? Failure) FixedLimits(byte[] bytes)
            {
                bool ok = FixedReader.TryParse(new ReadOnlySpan<byte>(bytes), out FixedReader.Fx? value, out CStructException? failure, new ReadLimits { OffsetBase = 40 });
                return (ok, failure);
            }

            public static (bool Ok, CStructException? Failure) FixedMemory(byte[] bytes)
            {
                bool ok = FixedReader.TryParse(new ReadOnlyMemory<byte>(bytes), out FixedReader.Fx? value, out CStructException? failure);
                return (ok, failure);
            }

            public static (bool Ok, CStructException? Failure) FixedArray(byte[] bytes)
            {
                bool ok = FixedReader.TryParse(bytes, out FixedReader.Fx? value, out CStructException? failure);
                return (ok, failure);
            }

            public static (bool Ok, CStructException? Failure) FixedFullSpan(byte[] bytes)
            {
                bool ok = FixedFull.TryParse(bytes, out FixedFull.Fx? value, out CStructException? failure);
                return (ok, failure);
            }

            public static (bool Ok, CStructException? Failure) FixedStruct(byte[] bytes)
            {
                bool ok = FixedValue.TryParse(bytes, out FixedValue.Fx value, out CStructException? failure);
                return (ok, failure);
            }

            public static (bool Ok, CStructException? Failure) FixedByOrder(byte[] bytes, bool littleEndian)
            {
                bool ok = FixedOrder.TryParse(bytes, littleEndian, out FixedOrder.Fx? value, out CStructException? failure);
                return (ok, failure);
            }

            public static (bool Ok, uint A) FixedGood(byte[] bytes)
            {
                bool ok = FixedReader.TryParse(bytes, out FixedReader.Fx? value, out CStructException? failure);
                return (ok, value?.A ?? 0);
            }

            public static (bool Ok, CStructException? Failure) ViewTry(byte[] bytes)
            {
                bool ok = new FixedViews.FxView(bytes).TryValidate(out CStructException? failure);
                return (ok, failure);
            }

            public static bool MatchesParser(byte[] bytes) => ProbeParser.Matches(bytes);

            public static bool MatchesFull(byte[] bytes) => ProbeFull.Matches(bytes);

            public static bool MatchesOrder(byte[] bytes, bool littleEndian) => ProbeOrder.Matches(bytes, littleEndian);
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates the layouts once.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        GeneratorResult result = GeneratorRunner.Run(Consumer.Replace("@FIXED@", Fixed, StringComparison.Ordinal).Replace("@PROBE@", Probe, StringComparison.Ordinal)).AssertClean();
        assembly = result.Load();
    }

    /// <summary>Calls a method of the consumer's <c>Calls</c> class and counts the exceptions raised on this thread while it runs.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="method">The method name.</param>
    /// <param name="exceptions">The number of exceptions thrown during the call, caught or not.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The result.</returns>
    private static T Call<T>(string method, out int exceptions, params object[] arguments)
    {
        MethodInfo target = assembly.GetType("Demo.Calls")!.GetMethod(method)!;
        int thrown = 0;
        int thread = Environment.CurrentManagedThreadId;

        // Counts the exceptions raised on the test thread, caught or not.
        void Count(object? sender, System.Runtime.ExceptionServices.FirstChanceExceptionEventArgs e)
        {
            if (Environment.CurrentManagedThreadId == thread)
            {
                thrown++;
            }
        }

        // Warm the method so a first call's own work (type loading, JIT) cannot throw into the count.
        target.Invoke(null, arguments);
        AppDomain.CurrentDomain.FirstChanceException += Count;
        try
        {
            var result = (T)target.Invoke(null, arguments)!;
            exceptions = thrown;
            return result;
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Count;
        }
    }

    /// <summary>The runtime layout's failure for the same bytes, the model of every non-throwing report.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <param name="offsetBase">The offset base.</param>
    /// <returns>The failure, or <see langword="null"/> when the read succeeds.</returns>
    private static CStructException? RuntimeFailure(byte[] bytes, long offsetBase = 0)
    {
        try
        {
            new CStruct(RuntimeFixed).Parse(bytes, "fx", null, new ReadOptions { OffsetBase = offsetBase });
            return null;
        }
        catch (CStructException exception)
        {
            return exception;
        }
    }

    /// <summary>A constraint that is not satisfied is returned by every non-throwing form with no exception raised, equal to the runtime's failure.</summary>
    [TestMethod]
    public void TryParse_ReturnsAConstraintFailureWithoutThrowing()
    {
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 7, 0, 0, 0];
        CStructException expected = RuntimeFailure(bytes)!;
        foreach (string method in new[] { "FixedSpan", "FixedMemory", "FixedArray", "FixedFullSpan", "FixedStruct" })
        {
            (bool ok, CStructException? failure) = Call<(bool, CStructException?)>(method, out int exceptions, bytes);
            Assert.IsFalse(ok, method);
            Assert.AreEqual(0, exceptions, method + " threw");
            var constraint = (CStructConstraintException)failure!;
            Assert.AreEqual(expected.Message, constraint.Message, method);
            Assert.AreEqual(expected.Offset, constraint.Offset, method);
            Assert.AreEqual("fx.a", constraint.Label, method);
        }

        (bool limitsOk, CStructException? limitsFailure) = Call<(bool, CStructException?)>("FixedLimits", out int limitsExceptions, bytes);
        Assert.IsFalse(limitsOk);
        Assert.AreEqual(0, limitsExceptions);
        Assert.AreEqual(RuntimeFailure(bytes, 40)!.Offset, limitsFailure!.Offset);
    }

    /// <summary>A value that satisfies the conditions parses as before, and a second failing field is the one reported when the first is fine.</summary>
    [TestMethod]
    public void TryParse_SucceedsAndReportsTheFirstFailingCondition()
    {
        (bool ok, uint a) = Call<(bool, uint)>("FixedGood", out int exceptions, new byte[] { 3, 0, 0, 0, 9, 0, 0, 0, 1, 0, 0, 0 });
        Assert.IsTrue(ok);
        Assert.AreEqual(3u, a);
        Assert.AreEqual(0, exceptions);

        byte[] unsupported = [3, 0, 0, 0, 200, 0, 0, 0, 1, 0, 0, 0];
        (bool unsupportedOk, CStructException? failure) = Call<(bool, CStructException?)>("FixedSpan", out int thrown, unsupported);
        Assert.IsFalse(unsupportedOk);
        Assert.AreEqual(0, thrown);
        Assert.AreEqual(ConstraintKind.Unsupported, ((CStructConstraintException)failure!).Kind);
    }

    /// <summary>A source the fixed reader cannot take still fails with the runtime's failure (through the member reader, which throws and is caught).</summary>
    [TestMethod]
    public void TryParse_ReportsAShortSourceLikeTheRuntime()
    {
        byte[] shortBytes = [1, 0, 0, 0, 2, 0];
        CStructException expected = RuntimeFailure(shortBytes)!;
        (bool ok, CStructException? failure) = Call<(bool, CStructException?)>("FixedSpan", out _, shortBytes);
        Assert.IsFalse(ok);
        Assert.AreEqual(expected.GetType(), failure!.GetType());
        Assert.AreEqual(expected.Message, failure.Message);
    }

    /// <summary>With a run-time byte order the non-throwing form decodes in the order it is given.</summary>
    [TestMethod]
    public void TryParse_HonoursARuntimeByteOrder()
    {
        // Little-endian a = 1 and b = 2 satisfy every condition; big-endian a = 16777216 and b = 33554432 break b < 100.
        byte[] bytes = [1, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0, 0];
        (bool littleOk, CStructException? littleFailure) = Call<(bool, CStructException?)>("FixedByOrder", out int littleExceptions, bytes, true);
        Assert.IsTrue(littleOk);
        Assert.IsNull(littleFailure);
        Assert.AreEqual(0, littleExceptions);

        (bool bigOk, CStructException? bigFailure) = Call<(bool, CStructException?)>("FixedByOrder", out int bigExceptions, bytes, false);
        Assert.IsFalse(bigOk);
        Assert.AreEqual(0, bigExceptions);
        var constraint = (CStructConstraintException)bigFailure!;
        Assert.AreEqual(ConstraintKind.Unsupported, constraint.Kind);
        Assert.AreEqual("fx.b", constraint.Label);
    }

    /// <summary>A view's <c>TryValidate</c> returns the failure <c>Validate</c> throws, without throwing.</summary>
    [TestMethod]
    public void TryValidate_ReturnsTheFailureWithoutThrowing()
    {
        byte[] bytes = [5, 0, 0, 0, 3, 0, 0, 0, 1, 0, 0, 0];
        CStructException expected = RuntimeFailure(bytes)!;
        (bool ok, CStructException? failure) = Call<(bool, CStructException?)>("ViewTry", out int exceptions, bytes);
        Assert.IsFalse(ok);
        Assert.AreEqual(0, exceptions);
        Assert.AreEqual(expected.Message, failure!.Message);
        Assert.AreEqual(expected.Offset, failure.Offset);

        (bool good, CStructException? none) = Call<(bool, CStructException?)>("ViewTry", out int goodExceptions, new byte[] { 3, 0, 0, 0, 9, 0, 0, 0, 1, 0, 0, 0 });
        Assert.IsTrue(good);
        Assert.IsNull(none);
        Assert.AreEqual(0, goodExceptions);
    }

    /// <summary>
    ///     <c>Matches</c> is true exactly when the source holds the bytes the evaluated conditions read and none of them is
    ///     broken: equal to the runtime layout collecting the <c>@expect</c> violations of the members at build-time offsets,
    ///     for every single-byte mutation of a good input and every prefix of it.
    /// </summary>
    [TestMethod]
    public void Matches_AgreesWithTheRuntimeOnMutatedInputs()
    {
        // magic, version, k, (lo, hi), n = 2, data, after = 7.
        byte[] good = [0x53, 0xEF, 2, 1, 0x34, 2, 8, 9, 7];
        var runtime = new CStruct(RuntimeProbe);
        string[] evaluated = ["magic", "version", "k", "lo", "hi"];
        int needed = 5;
        var checks = new List<(string Name, byte[] Bytes)> { ("good", good) };
        for (int length = 0; length < good.Length; length++)
        {
            checks.Add(("prefix " + length, good[..length]));
        }

        for (int index = 0; index < good.Length; index++)
        {
            foreach (byte replacement in new byte[] { 0, 1, 2, 3, 7, 10, 15, 0x0F, 0x1F, 0xA1, 0xFF })
            {
                byte[] mutated = (byte[])good.Clone();
                mutated[index] = replacement;
                checks.Add(("byte " + index + " = " + replacement, mutated));
            }
        }

        foreach ((string name, byte[] bytes) in checks)
        {
            bool expected = bytes.Length >= needed && CollectedMalformed(runtime, bytes).All(member => !evaluated.Contains(member, StringComparer.Ordinal));
            Assert.AreEqual(expected, (bool)assembly.GetType("Demo.Calls")!.GetMethod("MatchesParser")!.Invoke(null, [bytes])!, name + " (parser)");
            Assert.AreEqual(expected, (bool)assembly.GetType("Demo.Calls")!.GetMethod("MatchesFull")!.Invoke(null, [bytes])!, name + " (full)");
        }

        Call<bool>("MatchesParser", out int exceptions, good[..3]);
        Assert.AreEqual(0, exceptions, "a short source does not throw");
        Assert.IsFalse(Call<bool>("MatchesParser", out _, new byte[] { 0x53, 0xEF, 2, 2, 0x34, 2, 8, 9, 7 }), "kind Bad");
    }

    /// <summary>With a run-time byte order <c>Matches</c> reads the neutral values in the order it is given.</summary>
    [TestMethod]
    public void Matches_HonoursARuntimeByteOrder()
    {
        byte[] littleEndian = [0x53, 0xEF, 1, 1, 0x34, 0, 0, 7];
        byte[] bigEndian = [0xEF, 0x53, 1, 1, 0x34, 0, 0, 7];
        MethodInfo matches = assembly.GetType("Demo.Calls")!.GetMethod("MatchesOrder")!;
        Assert.IsTrue((bool)matches.Invoke(null, [littleEndian, true])!);
        Assert.IsFalse((bool)matches.Invoke(null, [littleEndian, false])!);
        Assert.IsTrue((bool)matches.Invoke(null, [bigEndian, false])!);
        Assert.IsFalse((bool)matches.Invoke(null, [bigEndian, true])!);
    }

    /// <summary>The members that break an <c>@expect</c> condition when the runtime reads the bytes and collects the failures; empty when the read cannot start.</summary>
    /// <param name="layout">The runtime layout.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The members, in field order.</returns>
    private static string[] CollectedMalformed(CStruct layout, byte[] bytes)
    {
        var found = new List<CStructConstraintException>();
        try
        {
            layout.Parse(bytes, "probe", null, new ReadOptions { ConstraintViolations = found });
        }
        catch (CStructReadException)
        {
            // A source that ends inside the struct: the violations before the failure still count.
        }

        return found.Where(violation => violation.Kind == ConstraintKind.Malformed).Select(violation => violation.Member!).ToArray();
    }
}
