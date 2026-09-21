namespace CStructSharp.Generators.Tests;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Record sequences: <c>Records&lt;Name&gt;</c> over memory, a segmented sequence, and a stream and
///     <c>Records&lt;Name&gt;Async</c> read the records the runtime's <c>ParseMany</c> reads, for a fixed-size and a
///     runtime-sized composite; trailing bytes fail on the step that meets them with the runtime's text and the
///     record's index; the view enumerator walks fixed-size records without allocating; the derived names are
///     reserved (CSG003).
/// </summary>
[TestClass]
public class SequenceTests
{
    private const string Layout = """"
        using System;
        using System.Collections.Generic;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("""
            struct hdr { uint16 length; uint8 tag; uint8 flags; };
            struct root { uint8 n; uint8 data[n]; uint16 *link; uint16 blob; };
            """, Root = "root", LittleEndian = false, Aligned = false, PointerSize = 2)]
        public static partial class Packet { }

        public static class Probe
        {
            public static object[] Enumerate(byte[] bytes)
            {
                var lengths = new List<int>(8);
                long before = GC.GetAllocatedBytesForCurrentThread();
                foreach (Demo.Packet.HdrView view in Demo.Packet.HdrView.Enumerate(bytes))
                {
                    lengths.Add(view.Length);
                }

                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                string failure = "no failure";
                int seen = 0;
                try
                {
                    foreach (Demo.Packet.HdrView view in Demo.Packet.HdrView.Enumerate(bytes.AsSpan(0, bytes.Length - 1)))
                    {
                        seen++;
                    }
                }
                catch (CStructReadException exception)
                {
                    failure = exception.Message;
                }

                int empty = 0;
                foreach (Demo.Packet.HdrView view in Demo.Packet.HdrView.Enumerate(ReadOnlySpan<byte>.Empty))
                {
                    empty++;
                }

                return new object[] { lengths.ToArray(), allocated, failure, seen, empty };
            }
        }
        """";

    // Three 4-byte headers; two runtime-sized roots (n = 1 with link -> 4, n = 2 with link -> 5: each record's own blob).
    private static readonly byte[] Headers = [0, 1, 7, 1, 0, 2, 7, 2, 0, 3, 7, 3,];
    private static readonly byte[] Roots = [1, 0xAA, 0, 4, 0xBB, 0xCC, 2, 0xAA, 0xAB, 0, 5, 0xDD, 0xEE,];

    [TestMethod]
    public void Records_ReadWhatParseManyReads_FromEveryInput()
    {
        GeneratorResult result = GeneratorRunner.Run(Layout).AssertClean();
        Snapshot.Match("Sequences.Packet", result.Source);
        Assembly assembly = result.Load();
        Type packet = assembly.GetType("Demo.Packet")!;
        var runtime = (CStruct)packet.GetProperty("Layout")!.GetValue(null)!;

        foreach ((string name, byte[] bytes, int count) in new[] { ("Hdr", Headers, 3), ("Root", Roots, 2), })
        {
            string layoutName = name.ToLowerInvariant();
            List<StructValue> expected = runtime.ParseMany(bytes, layoutName).ToList();
            Assert.HasCount(count, expected);
            foreach ((string kind, object input) in Inputs(bytes))
            {
                MethodInfo records = packet.GetMethods().Single(method => method.Name == "Records" + name && method.GetParameters()[0].ParameterType.IsInstanceOfType(input) && method.GetParameters()[0].ParameterType != typeof(object));
                List<object> actual = ((IEnumerable)records.Invoke(null, [input, null, null])!).Cast<object>().ToList();
                Assert.HasCount(count, actual, kind);
                for (int index = 0; index < count; index++)
                {
                    ParityComparer.AssertSame(expected[index], actual[index], $"[{index}].{layoutName} via {kind}");
                }

                if (input is MemoryStream stream)
                {
                    Assert.AreEqual((long)bytes.Length, stream.Position, kind);
                }
            }

            // The awaitable form over a memory stream and one that hides its buffer.
            MethodInfo recordsAsync = packet.GetMethods().Single(method => method.Name == "Records" + name + "Async");
            foreach (Stream stream in new Stream[] { new MemoryStream(bytes), new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false), })
            {
                List<object> actual = CollectAsync(recordsAsync.Invoke(null, [stream, null, null, CancellationToken.None])!);
                Assert.HasCount(count, actual);
                ParityComparer.AssertSame(expected[count - 1], actual[count - 1], $"[{count - 1}].{layoutName} async");
            }

            // The root forms.
            if (name == "Root")
            {
                MethodInfo rootForm = packet.GetMethods().Single(method => method.Name == "Records" && method.GetParameters()[0].ParameterType == typeof(ReadOnlyMemory<byte>));
                Assert.HasCount(2, ((IEnumerable)rootForm.Invoke(null, [new ReadOnlyMemory<byte>(bytes), null])!).Cast<object>().ToList());
                MethodInfo rootAsync = packet.GetMethods().Single(method => method.Name == "RecordsAsync");
                Assert.HasCount(2, CollectAsync(rootAsync.Invoke(null, [new MemoryStream(bytes), null, CancellationToken.None])!));
            }
        }

        // A fixed-size record is read exactly one at a time from a forward-only stream; a runtime-sized one needs a seekable stream.
        MethodInfo hdrStream = packet.GetMethods().Single(method => method.Name == "RecordsHdr" && method.GetParameters()[0].ParameterType == typeof(Stream));
        using var forward = new ForwardOnly(Headers);
        Assert.HasCount(3, ((IEnumerable)hdrStream.Invoke(null, [forward, null, null])!).Cast<object>().ToList());
        Assert.AreEqual(Headers.Length, forward.BytesRead, "byte-exact");
        MethodInfo hdrAsync = packet.GetMethods().Single(method => method.Name == "RecordsHdrAsync");
        Assert.HasCount(3, CollectAsync(hdrAsync.Invoke(null, [new ForwardOnly(Headers), null, null, CancellationToken.None])!));
        MethodInfo rootStream = packet.GetMethods().Single(method => method.Name == "RecordsRoot" && method.GetParameters()[0].ParameterType == typeof(Stream));
        Assert.IsInstanceOfType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => rootStream.Invoke(null, [new ForwardOnly(Roots), null, null])).InnerException);
        MethodInfo rootAsyncForm = packet.GetMethods().Single(method => method.Name == "RecordsRootAsync");
        Assert.IsInstanceOfType<ArgumentException>(Assert.Throws<TargetInvocationException>(() => rootAsyncForm.Invoke(null, [new ForwardOnly(Roots), null, null, CancellationToken.None])).InnerException);
        Assert.IsInstanceOfType<ArgumentNullException>(Assert.Throws<TargetInvocationException>(() => rootStream.Invoke(null, [null, null, null])).InnerException);

        // A window smaller than the input: the runtime-sized records after the first are read from refilled windows.
        var window = new ReadOptions { MaxTotalBytesRead = 10, };
        List<object> windowed = CollectAsync(rootAsyncForm.Invoke(null, [new MemoryStream(Roots, 0, Roots.Length, writable: false, publiclyVisible: false), null, window, CancellationToken.None])!);
        Assert.HasCount(2, windowed);
        ParityComparer.AssertSame(runtime.ParseMany(Roots, "root").Last(), windowed[1], "[1].root windowed");

        // Cancellation between records: the record before it is delivered, the next step throws.
        using var cancelled = new CancellationTokenSource();
        object sequence = hdrAsync.Invoke(null, [new MemoryStream(Headers), null, null, cancelled.Token])!;
        Type hdr = packet.GetNestedType("Hdr")!;
        object enumerator = typeof(IAsyncEnumerable<>).MakeGenericType(hdr).GetMethod("GetAsyncEnumerator")!.Invoke(sequence, [CancellationToken.None])!;
        MethodInfo moveNext = typeof(IAsyncEnumerator<>).MakeGenericType(hdr).GetMethod("MoveNextAsync")!;
        Assert.IsTrue(((dynamic)moveNext.Invoke(enumerator, null)!).AsTask().Result);
        cancelled.Cancel();
        Assert.IsInstanceOfType<OperationCanceledException>(Assert.Throws<AggregateException>(() => { ((dynamic)moveNext.Invoke(enumerator, null)!).AsTask().Wait(); }).InnerException);
    }

    [TestMethod]
    public void TrailingBytes_FailOnTheStepThatMeetsThem_WithTheRuntimesText()
    {
        GeneratorResult result = GeneratorRunner.Run(Layout).AssertClean();
        Assembly assembly = result.Load();
        Type packet = assembly.GetType("Demo.Packet")!;
        var runtime = (CStruct)packet.GetProperty("Layout")!.GetValue(null)!;

        // Two whole headers and three bytes: the third step fails with the partial-element text at index 2, offset 8.
        byte[] trailing = Headers[..11];
        string expected = Assert.Throws<CStructReadException>(() => runtime.ParseMany(trailing, "hdr").ToList()).Message;
        Assert.AreEqual("The remaining 3 bytes are not a whole number of 4-byte elements: hdr (path '[2].hdr', offset 8).", expected);
        foreach ((string kind, object input) in Inputs(trailing))
        {
            MethodInfo records = packet.GetMethods().Single(method => method.Name == "RecordsHdr" && method.GetParameters()[0].ParameterType.IsInstanceOfType(input) && method.GetParameters()[0].ParameterType != typeof(object));
            IEnumerator steps = ((IEnumerable)records.Invoke(null, [input, null, null])!).GetEnumerator();
            Assert.IsTrue(steps.MoveNext(), kind);
            Assert.IsTrue(steps.MoveNext(), kind);
            Assert.AreEqual(expected, Assert.Throws<CStructReadException>(() => steps.MoveNext(), kind).Message, kind);
        }

        MethodInfo hdrAsync = packet.GetMethods().Single(method => method.Name == "RecordsHdrAsync");
        Assert.AreEqual(expected, Assert.Throws<AggregateException>(() => CollectAsync(hdrAsync.Invoke(null, [new MemoryStream(trailing), null, null, CancellationToken.None])!)).InnerException!.Message);

        // A runtime-sized record cut short fails as the short read it is, at its index, with the runtime's text.
        byte[] cut = Roots[..9];
        string cutExpected = Assert.Throws<CStructReadException>(() => runtime.ParseMany(cut, "root").ToList()).Message;
        StringAssert.Contains(cutExpected, "in '[1].root'");
        MethodInfo rootMemory = packet.GetMethods().Single(method => method.Name == "RecordsRoot" && method.GetParameters()[0].ParameterType == typeof(ReadOnlyMemory<byte>));
        IEnumerator rootSteps = ((IEnumerable)rootMemory.Invoke(null, [new ReadOnlyMemory<byte>(cut), null, null])!).GetEnumerator();
        Assert.IsTrue(rootSteps.MoveNext());
        Assert.AreEqual(cutExpected, Assert.Throws<CStructReadException>(() => rootSteps.MoveNext()).Message);
        MethodInfo rootAsync = packet.GetMethods().Single(method => method.Name == "RecordsRootAsync");
        Assert.AreEqual(cutExpected, Assert.Throws<AggregateException>(() => CollectAsync(rootAsync.Invoke(null, [new MemoryStream(cut), null, null, CancellationToken.None])!)).InnerException!.Message);

        // The view enumerator: three headers without allocating, the same trailing-bytes failure, nothing over an empty span.
        var readings = (object[])assembly.GetType("Demo.Probe")!.GetMethod("Enumerate")!.Invoke(null, [Headers])!;
        CollectionAssert.AreEqual(new int[] { 1, 2, 3, }, (int[])readings[0]);
        Assert.AreEqual(0L, readings[1], "no allocation across the enumeration");
        Assert.AreEqual(expected, readings[2]);
        Assert.AreEqual(2, readings[3], "two whole records before the failure");
        Assert.AreEqual(0, readings[4]);
    }

    [TestMethod]
    public void DerivedNames_AreReserved()
    {
        const string Header = """
            using CStructSharp;

            namespace Demo;

            """;
        GeneratorResult records = GeneratorRunner.Run(Header + """
            [CStructLayout("struct records { uint8 x; }; struct root { records r; };")]
            public static partial class Reserved { }
            """);
        StringAssert.Contains(records.DiagnosticsWithId("CSG003").Single().GetMessage(), "Records");

        GeneratorResult enumerator = GeneratorRunner.Run(Header + """
            [CStructLayout("struct hdr { uint8 x; }; struct hdr_view_enumerator { uint8 y; }; struct root { hdr h; hdr_view_enumerator e; };")]
            public static partial class Reserved { }
            """);
        StringAssert.Contains(enumerator.DiagnosticsWithId("CSG003").Single().GetMessage(), "HdrViewEnumerator");

        GeneratorResult noViews = GeneratorRunner.Run(Header + """
            [CStructLayout("struct hdr { uint8 x; }; struct hdr_view_enumerator { uint8 y; }; struct root { hdr h; hdr_view_enumerator e; };", Views = false)]
            public static partial class Reserved { }
            """).AssertClean();
        Assert.IsTrue(noViews.Source.Contains("public sealed partial class HdrViewEnumerator", StringComparison.Ordinal), "without views the name is free for the composite");
    }

    private static IEnumerable<(string Kind, object Input)> Inputs(byte[] bytes)
    {
        yield return ("memory", new ReadOnlyMemory<byte>(bytes));
        yield return ("sequence", new System.Buffers.ReadOnlySequence<byte>(bytes));
        yield return ("segmented", ViewTests.Segmented(bytes, 3, 2));
        yield return ("stream", new MemoryStream(bytes));
    }

    private static List<object> CollectAsync(object asyncEnumerable)
    {
        // The compiler's iterator class is private, so the interface members are reached through their interface types.
        var list = new List<object>();
        Type item = asyncEnumerable.GetType().GetInterfaces().Single(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IAsyncEnumerable<>)).GetGenericArguments()[0];
        object enumerator = typeof(IAsyncEnumerable<>).MakeGenericType(item).GetMethod("GetAsyncEnumerator")!.Invoke(asyncEnumerable, [CancellationToken.None])!;
        Type enumeratorType = typeof(IAsyncEnumerator<>).MakeGenericType(item);
        MethodInfo moveNext = enumeratorType.GetMethod("MoveNextAsync")!;
        PropertyInfo current = enumeratorType.GetProperty("Current")!;
        while (((dynamic)moveNext.Invoke(enumerator, null)!).AsTask().Result)
        {
            list.Add(current.GetValue(enumerator)!);
        }

        return list;
    }

    private sealed class ForwardOnly(byte[] bytes) : Stream
    {
        private int position;

        public int BytesRead => this.position;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int chunk = Math.Min(Math.Min(count, 3), bytes.Length - this.position);
            Array.Copy(bytes, this.position, buffer, offset, chunk);
            this.position += chunk;
            return chunk;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
