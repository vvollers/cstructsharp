namespace CStructSharp.Generators.Tests;

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Views and stream parsing: a view's accessors decode the same values the reader produces without allocating,
///     a nested static struct is a nested view, a short source fails with the runtime's text, <c>Views = false</c>
///     emits none; <c>Parse(Stream)</c> reads the same value as the span reader and leaves a seekable stream after it.
/// </summary>
[TestClass]
public class ViewTests
{
    private const string Layout = """"
        using System;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("""
            enum kind : uint8 { A = 1, B = 2 };
            struct hdr { uint16 length; kind tag; uint8 flags:3; uint8 level:5; char name[4]; uint32 values[2]; uint16 *link; };
            struct root { uint8 magic; hdr header; uint8 n; uint8 data[n]; uint8 tail; };
            """, Root = "root", LittleEndian = false, Aligned = true, PointerSize = 2{0})]
        public static partial class Packet { }

        public static class Probe
        {
            public static object[] Read(byte[] bytes)
            {
                var root = new Demo.Packet.RootView(bytes);
                var header = root.Header;
                long before = GC.GetAllocatedBytesForCurrentThread();
                byte magic = root.Magic;
                ushort length = header.Length;
                var tag = header.Tag;
                byte level = header.Level;
                byte n = root.N;
                uint second = header.Values(1);
                long link = header.LinkAddress;
                int span = header.Bytes.Length + header.NameBytes.Length + header.ValuesBytes.Length;
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                string name = header.Name;
                string shortRead;
                try
                {
                    _ = new Demo.Packet.HdrView(bytes.AsSpan(0, 3));
                    shortRead = "no failure";
                }
                catch (CStructReadException exception)
                {
                    shortRead = exception.Message;
                }

                return new object[] { magic, length, tag, level, n, name, second, link, allocated, header.Bytes.Length, shortRead, root.ToObject() };
            }
        }
        """";

    [TestMethod]
    public void Views_DecodeStaticMembersWithoutAllocating_AndStreamsParseLikeSpans()
    {
        GeneratorResult result = GeneratorRunner.Run(Layout.Replace("{0}", string.Empty, StringComparison.Ordinal)).AssertClean();
        Snapshot.Match("Views.Packet", result.Source);
        Assembly assembly = result.Load();
        Type packet = assembly.GetType("Demo.Packet")!;
        var runtime = (CStruct)packet.GetProperty("Layout")!.GetValue(null)!;

        // magic, padding to the header's 4-byte alignment, the 20-byte header, n, data[3], tail, the root's tail padding.
        byte[] bytes = [7, 0, 0, 0, 0x01, 0x02, 2, 0b0001_0101, (byte)'a', (byte)'b', 0, 0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 2, 0, 0, 3, 9, 8, 7, 5, 0, 0, 0];

        // A helper compiled with the generated code reads through the views (a ref struct cannot be handled by reflection).
        MethodInfo probe = assembly.GetType("Demo.Probe")!.GetMethod("Read")!;
        var readings = (object[])probe.Invoke(null, [bytes])!;
        Assert.AreEqual((byte)7, readings[0]);
        Assert.AreEqual((ushort)0x0102, readings[1]);
        Assert.AreEqual(2, Convert.ToInt32(readings[2]));
        Assert.AreEqual((byte)2, readings[3]);
        Assert.AreEqual((byte)3, readings[4]);
        Assert.AreEqual("ab\0\0", readings[5], "TrimFixedText is off by default, as in the runtime");
        Assert.AreEqual(2u, readings[6]);
        Assert.AreEqual(2L, readings[7]);
        Assert.AreEqual(0L, readings[8], "no allocation across the accessors");
        Assert.AreEqual(runtime.GetStructSizeInBytes("hdr"), readings[9]);
        Assert.AreEqual("Not enough bytes: needed 20, available 3 (path 'hdr', offset 0).", readings[10]);
        ParityComparer.AssertSame(runtime.Parse(bytes, "root"), readings[11], "root");

        // Parse(Stream): the same value as the span reader, the stream left after the value.
        MethodInfo parseStream = packet.GetMethods().Single(method => method.Name == "ParseRoot" && method.GetParameters()[0].ParameterType == typeof(Stream));
        using var stream = new MemoryStream([0xEE, .. bytes, 0xFF]);
        stream.Position = 1;
        object value = parseStream.Invoke(null, [stream, null, null])!;
        ParityComparer.AssertSame(runtime.Parse(bytes, "root"), value, "root");
        Assert.AreEqual(1 + bytes.Length, stream.Position);
        stream.Position = 1;
        Exception truncated = Assert.Throws<TargetInvocationException>(() => parseStream.Invoke(null, [new MemoryStream(bytes[..10]), null, null])).InnerException!;
        Assert.AreEqual(Assert.Throws<CStructReadException>(() => runtime.Parse(bytes[..10], "root")).Message, truncated.Message);

        // The awaitable forms: ParseRootAsync over a hidden-buffer stream (copied) reads the same value and leaves the
        // stream after the value, restores the origin on a failure, and WriteRootAsync writes the serialized bytes.
        MethodInfo parseAsync = packet.GetMethods().Single(method => method.Name == "ParseRootAsync");
        using var asyncStream = new MemoryStream([0xEE, .. bytes, 0xFF], 0, bytes.Length + 2, writable: false, publiclyVisible: false) { Position = 1 };
        object asyncValue = ((dynamic)parseAsync.Invoke(null, [asyncStream, null, null, CancellationToken.None])!).AsTask().Result;
        ParityComparer.AssertSame(runtime.Parse(bytes, "root"), asyncValue, "root");
        Assert.AreEqual(1 + bytes.Length, asyncStream.Position);
        asyncStream.Position = 1;
        using var shortStream = new MemoryStream(bytes[..10]) { Position = 0 };
        AggregateException asyncFailure = Assert.Throws<AggregateException>(() => { ((dynamic)parseAsync.Invoke(null, [shortStream, null, null, CancellationToken.None])!).AsTask().Wait(); });
        Assert.AreEqual(truncated.Message, asyncFailure.InnerException!.Message);
        Assert.AreEqual(0L, shortStream.Position, "a failed async read restores the origin");
        MethodInfo writeAsync = packet.GetMethods().Single(method => method.Name == "WriteRootAsync");
        using var written = new MemoryStream();
        ((dynamic)writeAsync.Invoke(null, [written, value, null, null, CancellationToken.None])!).AsTask().Wait();
        CollectionAssert.AreEqual(runtime.Serialize("root", runtime.Parse(bytes, "root")), written.ToArray());
        using var cancelledSource = new CancellationTokenSource();
        cancelledSource.Cancel();
        AggregateException cancelledAsync = Assert.Throws<AggregateException>(() => { ((dynamic)parseAsync.Invoke(null, [new MemoryStream(bytes), null, null, cancelledSource.Token])!).AsTask().Wait(); });
        Assert.IsInstanceOfType<OperationCanceledException>(cancelledAsync.InnerException);

        // A ReadOnlySequence<byte>: one segment reads in place; several are copied and read like the flat bytes.
        MethodInfo parseSequence = packet.GetMethods().Single(method => method.Name == "ParseRoot" && method.GetParameters()[0].ParameterType == typeof(System.Buffers.ReadOnlySequence<byte>));
        ParityComparer.AssertSame(runtime.Parse(bytes, "root"), parseSequence.Invoke(null, [new System.Buffers.ReadOnlySequence<byte>(bytes), null, null])!, "root");
        ParityComparer.AssertSame(runtime.Parse(bytes, "root"), parseSequence.Invoke(null, [Segmented(bytes, 3, 5, 9), null, null])!, "root");
        Exception truncatedSequence = Assert.Throws<TargetInvocationException>(() => parseSequence.Invoke(null, [Segmented(bytes[..10], 4), null, null])).InnerException!;
        Assert.AreEqual(truncated.Message, truncatedSequence.Message);

        // A sequence longer than the budget is copied up to the budget plus one byte, exactly as Parse(Stream) buffers,
        // so both forms fail the same way (the runtime's stream reader charges its seeks differently on this edge:
        // a budget smaller than the value is a known difference, recorded in the log).
        var tinyBudget = new ReadOptions { MaxTotalBytesRead = 8 };
        Exception overBudget = Assert.Throws<TargetInvocationException>(() => parseSequence.Invoke(null, [Segmented(bytes, 3, 5, 9), null, tinyBudget])).InnerException!;
        Exception overBudgetStream = Assert.Throws<TargetInvocationException>(() => parseStream.Invoke(null, [new MemoryStream(bytes), null, tinyBudget])).InnerException!;
        Assert.AreEqual(overBudgetStream.Message, overBudget.Message);
        Assert.IsInstanceOfType<CStructException>(overBudget);

        // The token on the options is observed by the cursor at composite entry: a cancelled token ends the generated read.
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        MethodInfo parseSpan = packet.GetMethods().Single(method => method.Name == "ParseRoot" && method.GetParameters()[0].ParameterType == typeof(byte[]));
        Exception cancelledRead = Assert.Throws<TargetInvocationException>(() => parseSpan.Invoke(null, [bytes, null, new ReadOptions { CancellationToken = cancelled.Token }])).InnerException!;
        Assert.IsInstanceOfType<OperationCanceledException>(cancelledRead);
        MethodInfo serialize = packet.GetMethods().Single(method => method.Name == "SerializeRoot" && method.GetParameters().Length == 3 && method.ReturnType == typeof(byte[]));
        Exception cancelledWrite = Assert.Throws<TargetInvocationException>(() => serialize.Invoke(null, [value, null, new WriteOptions { CancellationToken = cancelled.Token }])).InnerException!;
        Assert.IsInstanceOfType<OperationCanceledException>(cancelledWrite);

        // Views = false emits none.
        string probeFree = Layout.Substring(0, Layout.IndexOf("public static class Probe", StringComparison.Ordinal));
        GeneratorResult without = GeneratorRunner.Run(probeFree.Replace("{0}", ", Views = false", StringComparison.Ordinal)).AssertClean();
        Assert.IsFalse(without.Source.Contains("ref struct", StringComparison.Ordinal));
        Assert.IsTrue(result.Source.Contains("public readonly ref struct HdrView", StringComparison.Ordinal));
    }

    /// <summary>A sequence whose segments split <paramref name="bytes"/> at the given lengths (the rest is a final segment).</summary>
    private static System.Buffers.ReadOnlySequence<byte> Segmented(byte[] bytes, params int[] lengths)
    {
        var first = new Segment(bytes.AsMemory(0, lengths[0]), 0);
        Segment last = first;
        int offset = lengths[0];
        for (int index = 1; index < lengths.Length; index++)
        {
            last = last.Append(bytes.AsMemory(offset, lengths[index]));
            offset += lengths[index];
        }

        if (offset < bytes.Length)
        {
            last = last.Append(bytes.AsMemory(offset));
        }

        return new System.Buffers.ReadOnlySequence<byte>(first, 0, last, last.Memory.Length);
    }

    private sealed class Segment : System.Buffers.ReadOnlySequenceSegment<byte>
    {
        public Segment(ReadOnlyMemory<byte> memory, long runningIndex)
        {
            this.Memory = memory;
            this.RunningIndex = runningIndex;
        }

        public Segment Append(ReadOnlyMemory<byte> memory)
        {
            var next = new Segment(memory, this.RunningIndex + this.Memory.Length);
            this.Next = next;
            return next;
        }
    }
}
