namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     <c>Shape = Scalar</c>: a one-member layout generates a static <c>Parse</c> that returns the value and no type, with the
///     failures, limits and constraints of the struct's reader (equal to the runtime layout's), and any other layout is refused
///     with CSG015 and the reason.
/// </summary>
[TestClass]
public class ScalarShapeTests
{
    private const string Header = "using CStructSharp;\n\nnamespace Demo;\n\n";

    private const string Consumer = Header + """"
        using System;
        using CStructSharp.Diagnostics;

        [CStructLayout("struct word { uint32 value; };", Root = "word", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Reader)]
        public static partial class WordLayout { }

        [CStructLayout("struct word { uint32 value; };", Root = "word", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Parser)]
        public static partial class ParserWord { }

        [CStructLayout("enum kind : uint8 { Ok = 1, Bad = 2 }; struct holder { kind k; };", Root = "holder", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Reader)]
        public static partial class KindLayout { }

        [CStructLayout("struct id { uuid value; };", Root = "id", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Reader)]
        public static partial class IdLayout { }

        [CStructLayout("struct word { uint16 value; };", Root = "word", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Reader, RuntimeByteOrder = true)]
        public static partial class OrderLayout { }

        [CStructLayout("struct magic { uint32 value @expect(value == 0xCAFEF00D, \"magic\") @supports(value != 7); };", Root = "magic", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Reader)]
        public static partial class MagicLayout { }

        [CStructLayout("struct word { uint32 value; };", Root = "word", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Reader, LittleEndian = false)]
        public static partial class BigLayout { }

        [CStructLayout("struct word { uint32 value; };", Root = "word", Shape = GeneratedShape.Class, Surface = GeneratedSurface.Parser, Views = false)]
        public static partial class ClassWord { }

        public static class Calls
        {
            public static uint Word(byte[] bytes) => WordLayout.Parse(bytes);

            public static uint WordLimits(byte[] bytes, long offsetBase) => WordLayout.Parse(bytes, new ReadLimits { OffsetBase = offsetBase, MaxTotalBytesRead = 2 });

            public static uint WordOptions(byte[] bytes, long offsetBase) => WordLayout.Parse(bytes, new ReadOptions { OffsetBase = offsetBase });

            public static uint ClassWordLimits(byte[] bytes, long offsetBase) => ClassWord.Parse(bytes, new ReadLimits { OffsetBase = offsetBase, MaxTotalBytesRead = 2 }).Value;

            public static uint ParserWordValue(byte[] bytes) => ParserWord.Parse(bytes);

            public static uint Big(byte[] bytes) => BigLayout.Parse(bytes);

            public static string Kind(byte[] bytes) => KindLayout.Parse(bytes).ToString();

            public static Type KindType() => typeof(KindLayout).GetMethod("Parse", new[] { typeof(ReadOnlySpan<byte>), typeof(ReadOptions) })!.ReturnType;

            public static Guid Id(byte[] bytes) => IdLayout.Parse(bytes);

            public static ushort Order(byte[] bytes, bool little) => OrderLayout.Parse(bytes, little);

            public static uint Magic(byte[] bytes) => MagicLayout.Parse(bytes);

            public static (bool Ok, uint Value, CStructException? Failure) TryMagic(byte[] bytes)
            {
                bool ok = MagicLayout.TryParse(bytes, out uint value, out CStructException? failure);
                return (ok, value, failure);
            }

            public static (bool Ok, uint Value, CStructException? Failure) TryWord(byte[] bytes)
            {
                bool ok = WordLayout.TryParse(bytes, out uint value, out CStructException? failure);
                return (ok, value, failure);
            }

            public static int Collect(byte[] bytes)
            {
                var found = new System.Collections.Generic.List<CStructConstraintException>();
                MagicLayout.Parse(bytes, new ReadOptions { ConstraintViolations = found });
                return found.Count;
            }

            public static void Cancelled(byte[] bytes)
            {
                using var source = new System.Threading.CancellationTokenSource();
                source.Cancel();
                WordLayout.Parse(bytes, new ReadOptions { CancellationToken = source.Token });
            }

            public static int Sizing() => WordLayout.Sizes.Word;
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates the layouts once.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        assembly = GeneratorRunner.Run(Consumer).AssertClean().Load();
    }

    /// <summary>Calls a method of the consumer's <c>Calls</c> class and unwraps a failure it throws.</summary>
    /// <param name="method">The method.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The result.</returns>
    private static object? Call(string method, params object[] arguments)
    {
        try
        {
            return assembly.GetType("Demo.Calls")!.GetMethod(method)!.Invoke(null, arguments);
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    /// <summary>The value the runtime layout reads for the same bytes, or the failure it raises.</summary>
    /// <param name="layout">The layout text.</param>
    /// <param name="root">The root.</param>
    /// <param name="member">The member.</param>
    /// <param name="bytes">The bytes.</param>
    /// <param name="options">The options.</param>
    /// <returns>The value or the exception.</returns>
    private static object Runtime(string layout, string root, string member, byte[] bytes, ReadOptions? options = null)
    {
        try
        {
            return new CStruct(layout).Parse(bytes, root, null, options)[member]!;
        }
        catch (CStructException exception)
        {
            return exception;
        }
    }

    /// <summary>The generated source is a short static class: no struct type, no fixed reader family, and a snapshot records it.</summary>
    [TestMethod]
    public void GeneratedSource_HasNoTypeAndIsRecorded()
    {
        string source = GeneratorRunner.Run(Header + "[CStructLayout(\"struct word { uint32 value; };\", Root = \"word\", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Parser)]\npublic static partial class Sample { }").AssertClean().Source;
        Snapshot.Match("Shape.Scalar", source);
        StringAssert.DoesNotMatch(source, new System.Text.RegularExpressions.Regex("class Word\\b"));
        StringAssert.DoesNotMatch(source, new System.Text.RegularExpressions.Regex("ReadWordFixed|FillWordFixed|ParseWord"));
        string full = GeneratorRunner.Run(Header + "[CStructLayout(\"struct word { uint32 value; };\", Root = \"word\", Shape = GeneratedShape.Class, Surface = GeneratedSurface.Parser, Views = false)]\npublic static partial class Sample { }").AssertClean().Source;
        Assert.IsLessThan(full.Length * 7 / 10, source.Length, "the scalar form is much smaller than the class form");
    }

    /// <summary>The method returns the value itself, for a number, an enum and an identifier.</summary>
    [TestMethod]
    public void Parse_ReturnsTheValue()
    {
        Assert.AreEqual(0x04030201u, Call("Word", new byte[] { 1, 2, 3, 4 }));
        Assert.AreEqual(0x01020304u, Call("Big", new byte[] { 1, 2, 3, 4 }));
        Assert.AreEqual("Bad", Call("Kind", new byte[] { 2 }));
        Assert.AreEqual("Demo.KindLayout+Kind", ((Type)Call("KindType")!).FullName);
        byte[] id = Enumerable.Range(1, 16).Select(value => (byte)value).ToArray();
        Assert.AreEqual(new Guid("01020304-0506-0708-090a-0b0c0d0e0f10"), Call("Id", id));
        Assert.AreEqual(0x0102u, Convert.ToUInt32(Call("Order", new byte[] { 2, 1 }, true)));
        Assert.AreEqual(0x0201u, Convert.ToUInt32(Call("Order", new byte[] { 2, 1 }, false)));
        Assert.AreEqual(4, Call("Sizing"));
        Assert.AreEqual(0x04030201u, Call("ParserWordValue", new byte[] { 1, 2, 3, 4, 9 }));
    }

    /// <summary>Short, limit and base-offset failures are the runtime layout's, message and offset included.</summary>
    [TestMethod]
    public void Failures_AreTheRuntimes()
    {
        const string Layout = "struct word { uint32 value; };";
        byte[] shortBytes = [1, 2];
        var expected = (CStructException)Runtime(Layout, "word", "value", shortBytes);
        var actual = Assert.Throws<CStructReadException>(() => Call("Word", shortBytes));
        Assert.AreEqual(expected.Message, actual.Message);
        Assert.AreEqual(expected.Offset, actual.Offset);

        // A limit failure is the one the generated reader of the same layout as a class reports.
        var limited = Assert.Throws<CStructReadLimitException>(() => Call("ClassWordLimits", new byte[4], 50L));
        var limitedActual = Assert.Throws<CStructReadLimitException>(() => Call("WordLimits", new byte[4], 50L));
        Assert.AreEqual(limited.Message, limitedActual.Message);
        Assert.AreEqual(limited.Offset, limitedActual.Offset);

        var shortBased = (CStructException)Runtime(Layout, "word", "value", shortBytes, new ReadOptions { OffsetBase = 99 });
        var shortActual = Assert.Throws<CStructReadException>(() => Call("WordOptions", shortBytes, 99L));
        Assert.AreEqual(shortBased.Offset, shortActual.Offset);
        Assert.Throws<OperationCanceledException>(() => Call("Cancelled", new byte[4]));
    }

    /// <summary>Constraints are checked by the fast path and the member path: the failure is the runtime's, with its label, and a non-throwing read returns it.</summary>
    [TestMethod]
    public void Constraints_AreChecked_AndTryParseDoesNotThrow()
    {
        const string Layout = "struct magic { uint32 value @expect(value == 0xCAFEF00D, \"magic\") @supports(value != 7); };";
        byte[] good = [0x0D, 0xF0, 0xFE, 0xCA];
        Assert.AreEqual(0xCAFEF00Du, Call("Magic", good));

        byte[] bad = [1, 0, 0, 0];
        var expected = (CStructConstraintException)Runtime(Layout, "magic", "value", bad);
        var actual = Assert.Throws<CStructConstraintException>(() => Call("Magic", bad));
        Assert.AreEqual(expected.Message, actual.Message);
        Assert.AreEqual(expected.Offset, actual.Offset);
        Assert.AreEqual("magic", actual.Label);

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

        Call("TryMagic", bad);
        AppDomain.CurrentDomain.FirstChanceException += Count;
        object? result;
        try
        {
            result = Call("TryMagic", bad);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= Count;
        }

        Assert.AreEqual(0, thrown);
        var tuple = ((bool Ok, uint Value, CStructException? Failure))result!;
        Assert.IsFalse(tuple.Ok);
        Assert.AreEqual(expected.Message, tuple.Failure!.Message);
        Assert.AreEqual(expected.Offset, tuple.Failure.Offset);

        var okTuple = ((bool Ok, uint Value, CStructException? Failure))Call("TryMagic", good)!;
        Assert.IsTrue(okTuple.Ok);
        Assert.AreEqual(0xCAFEF00Du, okTuple.Value);

        var shortTuple = ((bool Ok, uint Value, CStructException? Failure))Call("TryWord", new byte[] { 1 })!;
        Assert.IsFalse(shortTuple.Ok);
        Assert.IsInstanceOfType<CStructReadException>(shortTuple.Failure);
        Assert.AreEqual(1, Call("Collect", bad));
    }

    /// <summary>A layout a scalar cannot be is refused with CSG015 and the reason.</summary>
    /// <param name="attribute">The attribute arguments.</param>
    /// <param name="reason">A word the message must contain.</param>
    [TestMethod]
    [DataRow("\"struct a { uint32 x; };\", Shape = GeneratedShape.Scalar", "Reader or Parser surface")]
    [DataRow("\"struct a { uint32 x; uint32 y; };\", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Parser", "exactly one named member")]
    [DataRow("\"struct a { uint8 x[4]; };\", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Parser", "not an array")]
    [DataRow("\"struct a { uint8 x:3; };\", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Parser", "bitfield")]
    [DataRow("\"struct b { uint8 x; }; struct a { b y; };\", Root = \"a\", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Parser", "exactly one struct")]
    [DataRow("\"struct a { cstring name; };\", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Parser", "fixed size")]
    [DataRow("\"struct a { uint16 x @align(8); };\", Shape = GeneratedShape.Scalar, Surface = GeneratedSurface.Parser, Aligned = true", "fill the struct")]
    public void Unsupported_IsRefusedWithCSG015(string attribute, string reason)
    {
        GeneratorResult result = GeneratorRunner.Run(Header + "[CStructLayout(" + attribute + ")]\npublic static partial class Sample { }");
        Assert.AreEqual(1, result.DiagnosticsWithId("CSG015").Count, string.Join("; ", result.GeneratorDiagnostics.Select(diagnostic => diagnostic.GetMessage())));
        StringAssert.Contains(result.DiagnosticsWithId("CSG015")[0].GetMessage(), reason);
    }
}
