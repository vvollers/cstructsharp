namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Constraint labels, the offset base and collected violations in generated code: the member-by-member reader, the fixed
///     reader's constraint pass and a view's <c>Validate</c> report exactly what the runtime layout reports for the same bytes.
/// </summary>
[TestClass]
public class ConstraintReportingParityTests
{
    private const string Header = "struct hdr { uint16 magic @expect(magic == 0xEF53, \\\"hdr.magic\\\"); uint8 version @supports(version <= 2, \\\"hdr.version\\\") @expect(version != 0); uint8 n @expect(n <= 3); uint8 vals[n]; uint8 tail @expect(tail != 0, \\\"hdr.tail\\\"); };";

    private const string Fixed = "struct fx { uint32 a @expect(a != 0, \\\"fx.a\\\"); uint32 b @expect(b > a) @supports(b < 100, \\\"fx.b\\\"); uint32 c; }; struct outer { fx first; fx second; };";

    private static readonly string RuntimeHeader = Header.Replace("\\\"", "\"", StringComparison.Ordinal);

    private static readonly string RuntimeFixed = Fixed.Replace("\\\"", "\"", StringComparison.Ordinal);

    private static readonly string Consumer = """"
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("@HEADER@", Root = "hdr", Surface = GeneratedSurface.Full)]
        public static partial class FullHdr { }

        [CStructLayout("@HEADER@", Root = "hdr", Surface = GeneratedSurface.Parser)]
        public static partial class ParserHdr { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader)]
        public static partial class FixedFx { }

        [CStructLayout("@FIXED@", Root = "outer", Surface = GeneratedSurface.Parser)]
        public static partial class OuterFx { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Views)]
        public static partial class ViewFx { }

        public static class Probe
        {
            public static string[] Describe(IEnumerable<CStructConstraintException> violations)
                => violations.Select(v => v.Member + "|" + v.Label + "|" + v.Offset + "|" + v.Kind + "|" + v.Path + "|" + v.Constraint).ToArray();

            public static string[] FullHeader(byte[] bytes, long offsetBase)
            {
                var found = new List<CStructConstraintException>();
                FullHdr.Parse(bytes, null, new ReadOptions { ConstraintViolations = found, OffsetBase = offsetBase });
                return Describe(found);
            }

            public static string[] ParserHeader(byte[] bytes, long offsetBase)
            {
                var found = new List<CStructConstraintException>();
                ParserHdr.Parse(bytes, null, new ReadOptions { ConstraintViolations = found, OffsetBase = offsetBase });
                return Describe(found);
            }

            public static string[] FixedRoot(byte[] bytes, long offsetBase)
            {
                var found = new List<CStructConstraintException>();
                FixedFx.Parse(bytes, null, new ReadOptions { ConstraintViolations = found, OffsetBase = offsetBase });
                return Describe(found);
            }

            public static string[] Outer(byte[] bytes, long offsetBase)
            {
                var found = new List<CStructConstraintException>();
                OuterFx.Parse(bytes, null, new ReadOptions { ConstraintViolations = found, OffsetBase = offsetBase });
                return Describe(found);
            }

            public static string[] ViewValidate(byte[] bytes, long offsetBase)
            {
                var found = new List<CStructConstraintException>();
                new ViewFx.FxView(bytes, new ReadOptions { OffsetBase = offsetBase }).Validate(found);
                return Describe(found);
            }

            public static CStructException? Thrown(Action action)
            {
                try
                {
                    action();
                    return null;
                }
                catch (CStructException exception)
                {
                    return exception;
                }
            }

            public static CStructException? ThrownByOptions(byte[] bytes, long offsetBase) => Thrown(() => ParserHdr.Parse(bytes, null, new ReadOptions { OffsetBase = offsetBase }));

            public static CStructException? ThrownByLimits(byte[] bytes, long offsetBase) => Thrown(() => ParserHdr.Parse(bytes, new ReadLimits { OffsetBase = offsetBase }));

            public static CStructException? ThrownByFixed(byte[] bytes, long offsetBase) => Thrown(() => FixedFx.Parse(bytes, new ReadLimits { OffsetBase = offsetBase }));

            public static CStructException? ThrownByView(byte[] bytes, long offsetBase) => Thrown(() => new ViewFx.FxView(bytes, new ReadOptions { OffsetBase = offsetBase }).Validate());

            public static CStructException? ThrownByShortView(byte[] bytes, long offsetBase) => Thrown(() => _ = new ViewFx.FxView(bytes, new ReadOptions { OffsetBase = offsetBase }).C);
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates the layouts once.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        GeneratorResult result = GeneratorRunner.Run(Consumer.Replace("@HEADER@", Header, StringComparison.Ordinal).Replace("@FIXED@", Fixed, StringComparison.Ordinal)).AssertClean();
        assembly = result.Load();
    }

    /// <summary>Calls a probe method by name.</summary>
    /// <param name="method">The method.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The result.</returns>
    private static object? Call(string method, params object[] arguments)
        => assembly.GetType("Demo.Probe")!.GetMethod(method)!.Invoke(null, arguments);

    /// <summary>What the runtime layout collects for the same bytes, in the probe's format.</summary>
    /// <param name="layout">The layout text.</param>
    /// <param name="root">The root.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="offsetBase">The offset base.</param>
    /// <returns>The description of every violation.</returns>
    private static string[] RuntimeCollect(string layout, string root, byte[] bytes, long offsetBase)
    {
        var found = new List<CStructConstraintException>();
        new CStruct(layout).Parse(bytes, root, null, new ReadOptions { ConstraintViolations = found, OffsetBase = offsetBase });
        return found.Select(v => v.Member + "|" + v.Label + "|" + v.Offset + "|" + v.Kind + "|" + v.Path + "|" + v.Constraint).ToArray();
    }

    /// <summary>The member-by-member reader collects the runtime's violations, in order, with labels, offsets and the offset base.</summary>
    [TestMethod]
    public void MemberReader_CollectsLikeTheRuntime()
    {
        byte[] bytes = [1, 2, 3, 2, 7, 8, 0];
        foreach (long offsetBase in new long[] { 0, 5000 })
        {
            string[] expected = RuntimeCollect(RuntimeHeader, "hdr", bytes, offsetBase);
            Assert.AreEqual(3, expected.Length);
            CollectionAssert.AreEqual(expected, (string[])Call("FullHeader", bytes, offsetBase)!, "full " + offsetBase);
            CollectionAssert.AreEqual(expected, (string[])Call("ParserHeader", bytes, offsetBase)!, "parser " + offsetBase);
        }
    }

    /// <summary>The fixed reader's constraint pass collects the runtime's violations without moving the cursor, so the rest of the read is unaffected.</summary>
    [TestMethod]
    public void FixedPass_CollectsLikeTheRuntime()
    {
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 7, 0, 0, 0];
        string[] expected = RuntimeCollect(RuntimeFixed, "fx", bytes, 64);
        Assert.AreEqual(1, expected.Length);
        CollectionAssert.AreEqual(expected, (string[])Call("FixedRoot", bytes, 64L)!);

        // A struct that holds two constrained structs is read member by member and still collects both.
        byte[] outer = [0, 0, 0, 0, 1, 0, 0, 0, 7, 0, 0, 0, 5, 0, 0, 0, 3, 0, 0, 0, 9, 0, 0, 0];
        string[] expectedOuter = RuntimeCollect(RuntimeFixed, "outer", outer, 0);
        Assert.AreEqual(2, expectedOuter.Length);
        CollectionAssert.AreEqual(expectedOuter, (string[])Call("Outer", outer, 0L)!);
    }

    /// <summary>A view's <c>Validate</c> collects the failures and reports the offsets the reader reports, moved by the offset base.</summary>
    [TestMethod]
    public void ViewValidate_CollectsLikeTheRuntime()
    {
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 7, 0, 0, 0];
        CollectionAssert.AreEqual(RuntimeCollect(RuntimeFixed, "fx", bytes, 0), (string[])Call("ViewValidate", bytes, 0L)!);
        CollectionAssert.AreEqual(RuntimeCollect(RuntimeFixed, "fx", bytes, 900), (string[])Call("ViewValidate", bytes, 900L)!);

        byte[] twice = [5, 0, 0, 0, 3, 0, 0, 0, 1, 0, 0, 0];
        string[] expected = RuntimeCollect(RuntimeFixed, "fx", twice, 0);
        Assert.AreEqual(1, expected.Length);
        CollectionAssert.AreEqual(expected, (string[])Call("ViewValidate", twice, 0L)!);
    }

    /// <summary>A thrown constraint failure carries the label and the base offset on every generated path, equal to the runtime's.</summary>
    [TestMethod]
    public void ThrownFailures_CarryLabelAndBase()
    {
        byte[] bytes = [1, 2, 1, 0, 1];
        CStructConstraintException runtime = Assert.Throws<CStructConstraintException>(() => new CStruct(RuntimeHeader).Parse(bytes, "hdr", null, new ReadOptions { OffsetBase = 300 }));
        foreach (string method in new[] { "ThrownByOptions", "ThrownByLimits" })
        {
            var actual = (CStructConstraintException)Call(method, bytes, 300L)!;
            Assert.AreEqual(runtime.Label, actual.Label, method);
            Assert.AreEqual("hdr.magic", actual.Label, method);
            Assert.AreEqual(runtime.Offset, actual.Offset, method);
            Assert.AreEqual(runtime.Message, actual.Message, method);
        }

        byte[] fixedBytes = new byte[12];
        CStructConstraintException fixedRuntime = Assert.Throws<CStructConstraintException>(() => new CStruct(RuntimeFixed).Parse(fixedBytes, "fx", null, new ReadOptions { OffsetBase = 300 }));
        var fixedActual = (CStructConstraintException)Call("ThrownByFixed", fixedBytes, 300L)!;
        Assert.AreEqual(fixedRuntime.Offset, fixedActual.Offset);
        Assert.AreEqual(fixedRuntime.Message, fixedActual.Message);
        var viewActual = (CStructConstraintException)Call("ThrownByView", fixedBytes, 300L)!;
        Assert.AreEqual(fixedRuntime.Offset, viewActual.Offset);
        Assert.AreEqual("fx.a", viewActual.Label);
    }

    /// <summary>A short read carries the base too: in a reader and in a view.</summary>
    [TestMethod]
    public void ShortReads_CarryTheBase()
    {
        byte[] shortBytes = [0x53, 0xEF, 1];
        CStructReadException runtime = Assert.Throws<CStructReadException>(() => new CStruct(RuntimeHeader).Parse(shortBytes, "hdr", null, new ReadOptions { OffsetBase = 77 }));
        var actual = (CStructReadException)Call("ThrownByOptions", shortBytes, 77L)!;
        Assert.AreEqual(runtime.Offset, actual.Offset);
        var limits = (CStructReadException)Call("ThrownByLimits", shortBytes, 77L)!;
        Assert.AreEqual(runtime.Offset, limits.Offset);

        var view = (CStructReadException)Call("ThrownByShortView", new byte[4], 77L)!;
        var local = (CStructReadException)Call("ThrownByShortView", new byte[4], 0L)!;
        Assert.AreEqual(local.Offset + 77, view.Offset);
    }
}
