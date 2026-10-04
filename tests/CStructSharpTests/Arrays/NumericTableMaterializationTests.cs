namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using CStructSharp.Values;

/// <summary>Checks final numeric rows against the original flat reader, including block boundaries and owned mutable lists.</summary>
[TestClass]
public class NumericTableMaterializationTests
{
    /// <summary>Borrowing input during decoding never borrows storage in the returned rows, including a block-crossing row.</summary>
    [TestMethod]
    public void NumericRows_OwnValuesAfterSourceMutation()
    {
        var layout = new CStruct("struct root { uint16> grid[2][20001]; };");
        byte[] source = new byte[80004];
        Array.Fill(source, (byte)0x17);
        var value = layout.Parse(source, "root");
        Array.Fill(source, (byte)0x81);
        var rows = (List<object?>)value["grid"]!;
        foreach (object? item in rows)
        {
            var row = (List<object?>)item!;
            Assert.AreEqual(20001, row.Count);
            foreach (object? element in row)
            {
                Assert.AreEqual((ushort)0x1717, element);
            }
        }

        ((List<object?>)rows[0]!)[0] = (ushort)0x4242;
        Assert.AreEqual((ushort)0x1717, ((List<object?>)rows[1]!)[0]);
        Assert.AreEqual((byte)0x81, source[0]);
    }

    /// <summary>Every bulk numeric codec retains its boxed leaf types and row shape across unaligned block boundaries.</summary>
    /// <param name="bigEndian">Whether multi-byte leaves use most-significant-byte-first encoding.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void NumericRows_PreserveValuesAndOwnership(bool bigEndian)
    {
        foreach (string type in new[] { "uint8", "int8", "bool", "uint16", "int16", "uint24", "int24", "uint32", "int32", "uint64", "int64", "float32", "float64" })
        {
            string order = type is "uint8" or "int8" or "bool" ? string.Empty : bigEndian ? ">" : "<";
            var layout = new CStruct($"struct root {{ {type}{order} grid[2][3][7001]; uint8 tail; }};");
            byte[] bytes = new byte[layout.GetStructSizeInBytes("root")];
            new Random(527).NextBytes(bytes);
            StructValue actual = layout.Parse(bytes, "root");
            StructValue expected = layout.Parse(bytes, "root", options: ExecutionPaths.NoFastPaths());
            var planes = (List<object?>)actual["grid"]!;
            var expectedPlanes = (List<object?>)expected["grid"]!;
            Assert.AreEqual(2, planes.Count);
            for (int plane = 0; plane < 2; plane++)
            {
                var rows = (List<object?>)planes[plane]!;
                var expectedRows = (List<object?>)expectedPlanes[plane]!;
                Assert.AreEqual(3, rows.Count);
                for (int row = 0; row < 3; row++)
                {
                    var values = (List<object?>)rows[row]!;
                    var expectedValues = (List<object?>)expectedRows[row]!;
                    Assert.AreEqual(7001, values.Count);
                    CollectionAssert.AreEqual(expectedValues, values, type);
                    Assert.AreEqual(expectedValues[0]!.GetType(), values[0]!.GetType(), type);
                    Assert.AreNotSame(expectedValues, values);
                }

                Assert.AreNotSame(rows[0], rows[1]);
            }

            var first = (List<object?>)((List<object?>)planes[0]!)[0]!;
            first[0] = "mutated";
            first.Add(null);
            Assert.AreEqual(7002, first.Count);
            Assert.AreEqual(7001, ((List<object?>)((List<object?>)expectedPlanes[0]!)[0]!).Count);
            Assert.AreEqual(expected["tail"], actual["tail"]);
        }
    }

    /// <summary>Zero extents and standalone, promoted and union tables retain their original success or failure outcome.</summary>
    [TestMethod]
    public void NumericRows_PreserveOtherShapes()
    {
        foreach (string definition in new[]
        {
            "struct root { uint16 grid[0][3]; };",
            "struct root { uint16 grid[3][0]; };",
            "struct root { uint16 grid[2][1][3]; };",
            "struct root { struct { uint16 grid[2][3]; }; uint8 tail; };",
            "union root { uint16 grid[2][3]; uint8 bytes[12]; };",
        })
        {
            var layout = new CStruct(definition);
            AssertSameOutcome(layout, new byte[32], null);
        }

        var standalone = new CStruct("typedef uint16 grid[2][3];");
        Assert.AreEqual(OperationOutcome.Render(standalone.ReadValue(new byte[12], "grid", options: ExecutionPaths.NoFastPaths())), OperationOutcome.Render(standalone.ReadValue(new byte[12], "grid")));
    }

    /// <summary>Truncation, limits and cancellation still fail at the original block and retain complete diagnostics.</summary>
    [TestMethod]
    public void NumericRows_PreserveFailures()
    {
        var layout = new CStruct("struct root { uint8 head; uint24> grid[2][12001]; uint8 tail; };");
        byte[] bytes = new byte[(2 * 12001 * 3) + 2];
        foreach (int length in new[] { 0, 1, 65535, 65536, 65537, bytes.Length - 2, bytes.Length - 1, bytes.Length })
        {
            AssertSameOutcome(layout, bytes[..length], null);
        }

        foreach (int budget in new[] { 1, 65535, 65536, 65537, bytes.Length - 1, bytes.Length })
        {
            AssertSameOutcome(layout, bytes, new ReadOptions { MaxTotalBytesRead = budget });
        }

        AssertSameOutcome(layout, bytes, new ReadOptions { MaxArrayElements = 24001 });
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        AssertSameOutcome(layout, bytes, new ReadOptions { CancellationToken = cancelled.Token });
    }

    /// <summary>A stream that cancels after its first block observes identical read calls, positions and cancellation failures.</summary>
    [TestMethod]
    public void NumericRows_PreserveStreamInteractions()
    {
        var layout = new CStruct("struct root { uint16 grid[3][12001]; };");
        foreach (bool cancel in new[] { false, true })
        {
            using var actualCancellation = new CancellationTokenSource();
            using var expectedCancellation = new CancellationTokenSource();
            using var actual = new ObservedStream(new byte[72006], cancel ? actualCancellation : null);
            using var expected = new ObservedStream(new byte[72006], cancel ? expectedCancellation : null);

            // Record the optimized operation's full outcome, including cancellation after the first physical read.
            OperationOutcome actualOutcome = OperationOutcome.Of(() => layout.Parse(actual, "root", options: new ReadOptions { CancellationToken = actualCancellation.Token }), typeof(OperationCanceledException));

            // Disable the fused row construction while retaining the original numeric block reader.
            OperationOutcome expectedOutcome = OperationOutcome.Of(() => layout.Parse(expected, "root", options: ExecutionPaths.NoFastPaths(new ReadOptions { CancellationToken = expectedCancellation.Token })), typeof(OperationCanceledException));
            OperationOutcome.AssertSame(expectedOutcome, actualOutcome, "stream cancellation " + cancel);
            CollectionAssert.AreEqual(expected.Requests, actual.Requests);
            Assert.AreEqual(expected.Position, actual.Position);
            CollectionAssert.AreEqual(cancel ? new[] { 65536 } : new[] { 65536, 6470 }, actual.Requests);
            Assert.AreEqual(cancel ? typeof(OperationCanceledException) : null, actualOutcome.Failure?.GetType());
            Assert.AreEqual(cancel ? 65536L : 72006L, actual.Position);
        }
    }

    /// <summary>Compares exact outcomes and positions using exposed and non-exposable input streams.</summary>
    /// <param name="layout">The prepared schema.</param>
    /// <param name="bytes">Complete or truncated input bytes.</param>
    /// <param name="options">The optional limits or cancellation token.</param>
    private static void AssertSameOutcome(CStruct layout, byte[] bytes, ReadOptions? options)
    {
        foreach (bool exposed in new[] { false, true })
        {
            using var actual = new MemoryStream(bytes, 0, bytes.Length, false, exposed);
            using var expected = new MemoryStream(bytes, 0, bytes.Length, false, exposed);

            // Observe the operation with final-row materialization enabled.
            OperationOutcome actualOutcome = OperationOutcome.Of(() => layout.ReadValue(actual, "root", options: options), typeof(OperationCanceledException), typeof(DivideByZeroException));

            // Observe the original flat-list materialization with the same cursor and limits.
            OperationOutcome expectedOutcome = OperationOutcome.Of(() => layout.ReadValue(expected, "root", options: ExecutionPaths.NoFastPaths(options)), typeof(OperationCanceledException), typeof(DivideByZeroException));
            OperationOutcome.AssertSame(expectedOutcome, actualOutcome, "exposed " + exposed);
            Assert.AreEqual(expected.Position, actual.Position);
        }
    }

    /// <summary>Records physical read sizes and optionally cancels after the first completed read.</summary>
    private sealed class ObservedStream : MemoryStream
    {
        private readonly CancellationTokenSource? cancellation;

        /// <summary>Wraps a non-exposable input and optional cancellation source.</summary>
        /// <param name="bytes">The owned test input.</param>
        /// <param name="cancellation">The token source cancelled after a read, or null.</param>
        public ObservedStream(byte[] bytes, CancellationTokenSource? cancellation)
            : base(bytes, false) => this.cancellation = cancellation;

        /// <summary>Gets physical read request sizes in invocation order.</summary>
        public List<int> Requests { get; } = [];

        /// <summary>Reads the requested span, records its size, and then triggers optional cancellation.</summary>
        /// <param name="buffer">The destination that receives source bytes.</param>
        /// <returns>The number of bytes copied.</returns>
        public override int Read(Span<byte> buffer)
        {
            this.Requests.Add(buffer.Length);
            int count = base.Read(buffer);
            this.cancellation?.Cancel();
            return count;
        }
    }
}
