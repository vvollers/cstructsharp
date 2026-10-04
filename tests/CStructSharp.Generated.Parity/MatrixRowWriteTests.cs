namespace CStructSharp.Generated.Parity;

using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>Compares generated row writes with their original scalar cursor sequence and observable output.</summary>
[TestClass]
public class MatrixRowWriteTests
{
    /// <summary>Promoted numeric rows propagate output ownership even though their inline composite has no generated class.</summary>
    [TestMethod]
    public void PromotedRows_KeepOwnedAndBorrowedBytes()
    {
        var value = new MatrixRowWriteLayouts.Promoted.Root { Matrix = [[1, 2], [3, 4]] };
        byte[] expected = [0, 1, 0, 2, 0, 3, 0, 4];
        CollectionAssert.AreEqual(expected, MatrixRowWriteLayouts.Promoted.Serialize(value));
        byte[] destination = new byte[8];
        Assert.AreEqual(8, MatrixRowWriteLayouts.Promoted.Serialize(value, destination));
        CollectionAssert.AreEqual(expected, destination);
    }

    /// <summary>Every byte-budget boundary preserves the original generated exception and successful owned bytes.</summary>
    [TestMethod]
    public void OwnedRows_PreserveScalarBudgetAndRowValidationFailures()
    {
        var value = new MatrixRowWriteLayouts.Header.Root { Tag = 7, Matrix = [[1, 2, 3], [4, 5, 6]], Tail = 9 };
        for (int budget = 0; budget <= 14; budget++)
        {
            AssertOwnedOutcome(value, new WriteOptions { MaxTotalBytesWritten = budget });
        }

        AssertOwnedOutcome(value, new WriteOptions { MaxArrayElements = 5 });
        value.Matrix[1] = null!;
        AssertOwnedOutcome(value, null);
        value.Matrix[1] = [4, 5];
        AssertOwnedOutcome(value, null);
        value.Matrix[1] = [4, 5, 6, 7];
        AssertOwnedOutcome(value, null);
    }

    /// <summary>Nested three-dimensional rows keep byte order, Boolean normalization and zero-length leaf placement.</summary>
    [TestMethod]
    public void NestedRows_PreserveThreeDimensionsBooleansAndEmptyRows()
    {
        var value = new MatrixRowWriteLayouts.Nested.Root
        {
            Content = new MatrixRowWriteLayouts.Nested.Child
            {
                Cube = [[[1, 2, 3], [4, 5, 6]], [[7, 8, 9], [10, 11, 12]]],
                Flags = [[true, false, true], [false, true, false]],
                Empty = [[], []],
            },
            Tail = 9,
        };
        byte[] expected = new byte[55];
        for (int index = 0; index < 12; index++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(expected.AsSpan(index * 4), (uint)(index + 1));
        }

        expected[48] = expected[50] = expected[52] = 1;
        expected[^1] = 9;
        CollectionAssert.AreEqual(expected, MatrixRowWriteLayouts.Nested.Serialize(value));
        byte[] destination = new byte[60];
        Array.Fill(destination, (byte)91);
        Assert.AreEqual(55, MatrixRowWriteLayouts.Nested.Serialize(value, destination));
        CollectionAssert.AreEqual(expected, destination[..55]);
        CollectionAssert.AreEqual(new byte[] { 91, 91, 91, 91, 91 }, destination[55..]);
    }

    /// <summary>Union overwrites and path updates retain the existing scalar or runtime operation and surrounding bytes.</summary>
    [TestMethod]
    public void UnionAndUpdateRows_PreserveBorrowedBytes()
    {
        var value = new MultidimensionalArrayLayouts.Union.Root
        {
            Tag = 7,
            Content = new MultidimensionalArrayLayouts.Union.Value { SelectedMember = "matrix", Matrix = [[1, 2, 3], [4, 5, 6]] },
            Tail = 9,
        };
        byte[] expected = [7, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 9];
        CollectionAssert.AreEqual(expected, MultidimensionalArrayLayouts.Union.Serialize(value));
        byte[] destination = new byte[14];
        Assert.AreEqual(14, MultidimensionalArrayLayouts.Union.Serialize(value, destination));
        CollectionAssert.AreEqual(expected, destination);

        byte[] updated = MatrixRowWriteLayouts.Header.Serialize(new MatrixRowWriteLayouts.Header.Root { Tag = 7, Matrix = [[1, 2, 3], [4, 5, 6]], Tail = 9 });
        MatrixRowWriteLayouts.Header.UpdatePath(updated, "root.matrix", new ushort[][] { [6, 5, 4], [3, 2, 1] });
        CollectionAssert.AreEqual(new byte[] { 7, 6, 0, 5, 0, 4, 0, 3, 0, 2, 0, 1, 0, 9 }, updated);
    }

    /// <summary>Odd-width range validation remains before the scalar reservation, including when the byte budget also fails.</summary>
    [TestMethod]
    public void OddWidthRows_KeepRangeFailureBeforeReservation()
    {
        var value = MultidimensionalArrayLayouts.Mixed.Parse(new byte[50]);
        value.Cube[0][0][0] = 0x1000000;
        Span<byte> encoded = stackalloc byte[3];
        foreach (int budget in new[] { 13, 50 })
        {
            var options = new WriteOptions { MaxTotalBytesWritten = budget };
            byte[] expected = new byte[50];
            Array.Fill(expected, (byte)91);
            CStructException? expectedFailure = null;
            var cursor = new WriteCursor(expected, options, "root");
            try
            {
                cursor.EnterComposite("root", null);
                cursor.Reserve(1, "tag", "uint8")[0] = 0;
                cursor.RequireArrayLength(6, "matrix", "uint16<");
                for (int index = 0; index < 6; index++)
                {
                    Codec.WriteUInt16(cursor.Reserve(2, "matrix", "uint16<"), 0, true);
                }

                cursor.RequireArrayLength(12, "cube", "uint24>");
                try
                {
                    Codec.WriteUInt24(encoded, value.Cube[0][0][0], false);
                }
                catch (CStructException failure)
                {
                    throw cursor.WithMember(failure, "cube", "uint24>");
                }

                encoded.CopyTo(cursor.Reserve(3, "cube", "uint24>"));
            }
            catch (CStructException failure)
            {
                cursor.Complete(failure);
                expectedFailure = failure;
            }

            Assert.IsNotNull(expectedFailure);

            // The owned writer must retain the same range error instead of attempting a whole-row reservation.
            CStructException owned = Assert.Throws<CStructException>(() => MultidimensionalArrayLayouts.Mixed.Serialize(value, options));
            AssertSameFailure(expectedFailure, owned);
            byte[] actual = new byte[50];
            Array.Fill(actual, (byte)91);

            // Caller storage must retain the same prefix and untouched bytes after the rejected value.
            CStructException borrowed = Assert.Throws<CStructException>(() => MultidimensionalArrayLayouts.Mixed.Serialize(value, actual, options));
            AssertSameFailure(expectedFailure, borrowed);
            CollectionAssert.AreEqual(expected, actual);
        }
    }

    /// <summary>Custom array callbacks retain their order, and cancellation between fields adds no numeric-row checkpoint.</summary>
    [TestMethod]
    public void RowShortcut_PreservesCustomCallsAndCancellationCheckpoints()
    {
        using var cancellation = new CancellationTokenSource();
        var calls = new List<int>();

        // Each custom array element observes its place in the original scalar write sequence.
        Action first = () => calls.Add(1);

        // The second callback cancels after the root's existing checkpoint but before numeric rows begin.
        Action second = () =>
        {
            calls.Add(2);
            cancellation.Cancel();
        };
        var value = new MatrixRowWriteLayouts.Callback.Root { Events = [[first, second]], Matrix = [[1, 2, 3], [4, 5, 6]] };
        var options = new WriteOptions { CancellationToken = cancellation.Token };
        CollectionAssert.AreEqual(new byte[] { 7, 7, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 0 }, MatrixRowWriteLayouts.Callback.Serialize(value, options));
        CollectionAssert.AreEqual(new[] { 1, 2 }, calls);

        // Repeating with a token already cancelled must stop at root entry before invoking either codec again.
        OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() => MatrixRowWriteLayouts.Callback.Serialize(value, options));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        CollectionAssert.AreEqual(new[] { 1, 2 }, calls);
    }

    /// <summary>Borrowed spans retain scalar writes, including when each write changes an input element not yet read.</summary>
    [TestMethod]
    public void BorrowedRows_PreserveOverlappingSourceMutationAndFailurePrefix()
    {
        foreach (int offset in new[] { 0, 1, 3 })
        {
            byte[] expected = new byte[32];
            for (int index = 0; index < expected.Length; index++)
            {
                expected[index] = (byte)(index + 1);
            }

            byte[] actual = (byte[])expected.Clone();
            var value = new MatrixRowWriteLayouts.Alias.Root { Matrix = [actual] };
            CStructException? expectedFailure = null;
            var cursor = new WriteCursor(expected.AsSpan(offset), path: "root");
            try
            {
                cursor.EnterComposite("root", null);
                cursor.RequireArrayLength(32, "matrix", "uint8");
                for (int index = 0; index < expected.Length; index++)
                {
                    cursor.Reserve(1, "matrix", "uint8")[0] = expected[index];
                }
            }
            catch (CStructException failure)
            {
                cursor.Complete(failure);
                expectedFailure = failure;
            }

            CStructException? actualFailure = null;
            try
            {
                Assert.AreEqual(32, MatrixRowWriteLayouts.Alias.Serialize(value, actual.AsSpan(offset)));
            }
            catch (CStructException failure)
            {
                actualFailure = failure;
            }

            AssertSameFailure(expectedFailure, actualFailure);
            CollectionAssert.AreEqual(expected, actual);
        }
    }

    /// <summary>Stream and async wrappers write the owned bytes once; validation failures leave position and bytes intact.</summary>
    /// <returns>A task that completes after the stream checks.</returns>
    [TestMethod]
    public async Task OwnedRows_PreserveStreamBytesPositionsAndValidationAtomicity()
    {
        var value = new MatrixRowWriteLayouts.Header.Root { Tag = 7, Matrix = [[1, 2, 3], [4, 5, 6]], Tail = 9 };
        byte[] expected = MatrixRowWriteLayouts.Header.Serialize(value);
        using var stream = new ObservedWriteStream();
        stream.WriteByte(91);
        MatrixRowWriteLayouts.Header.Write(stream, value);
        Assert.AreEqual(15L, stream.Position);
        Assert.AreEqual(1, stream.SynchronousWrites);
        CollectionAssert.AreEqual(expected, stream.ToArray()[1..]);
        stream.Position = 1;
        await MatrixRowWriteLayouts.Header.WriteAsync(stream, value);
        Assert.AreEqual(15L, stream.Position);
        Assert.AreEqual(1, stream.SynchronousWrites);
        Assert.AreEqual(1, stream.AsynchronousWrites);
        CollectionAssert.AreEqual(expected, stream.ToArray()[1..]);

        byte[] previous = stream.ToArray();
        stream.Position = 1;
        value.Matrix[1] = [4];

        // Validation occurs in the complete owned serialization before either stream write is invoked.
        CStructException synchronous = Assert.Throws<CStructException>(() => MatrixRowWriteLayouts.Header.Write(stream, value));
        Assert.AreEqual(1L, stream.Position);
        CollectionAssert.AreEqual(previous, stream.ToArray());
        Assert.AreEqual(1, stream.SynchronousWrites);
        Assert.AreEqual(1, stream.AsynchronousWrites);

        // Observe the awaitable failure through its public wrapper and compare all diagnostic fields.
        CStructException asynchronous = await Assert.ThrowsAsync<CStructException>(async () => await MatrixRowWriteLayouts.Header.WriteAsync(stream, value));
        AssertSameFailure(synchronous, asynchronous);
        Assert.AreEqual(1L, stream.Position);
        CollectionAssert.AreEqual(previous, stream.ToArray());
        Assert.AreEqual(1, stream.SynchronousWrites);
        Assert.AreEqual(1, stream.AsynchronousWrites);
    }

    /// <summary>Compares one owned serialization with the original scalar sequence, including complete failure details.</summary>
    /// <param name="value">The matrix and surrounding scalar values.</param>
    /// <param name="options">The original write limits.</param>
    private static void AssertOwnedOutcome(MatrixRowWriteLayouts.Header.Root value, WriteOptions? options)
    {
        byte[] expected = new byte[14];
        CStructException? expectedFailure = null;
        var cursor = new WriteCursor(expected, options, "root");
        try
        {
            cursor.EnterComposite("root", null);
            cursor.Reserve(1, "tag", "uint8")[0] = value.Tag;
            cursor.RequireArrayLength(6, "matrix", "uint16<");
            RequireLength(ref cursor, value.Matrix.Length, 2);
            foreach (ushort[] row in value.Matrix)
            {
                if (row is null)
                {
                    throw cursor.Fail("Null is valid only for a scalar pointer field: matrix", "matrix", "uint16<");
                }

                RequireLength(ref cursor, row.Length, 3);
                foreach (ushort element in row)
                {
                    Codec.WriteUInt16(cursor.Reserve(2, "matrix", "uint16<"), element, true);
                }
            }

            cursor.Reserve(1, "tail", "uint8")[0] = value.Tail;
        }
        catch (CStructException failure)
        {
            cursor.Complete(failure);
            expectedFailure = failure;
        }

        CStructException? actualFailure = null;
        try
        {
            CollectionAssert.AreEqual(expected, MatrixRowWriteLayouts.Header.Serialize(value, options));
        }
        catch (CStructException failure)
        {
            actualFailure = failure;
        }

        AssertSameFailure(expectedFailure, actualFailure);
    }

    /// <summary>Applies the original generated row-length checks in their original order.</summary>
    /// <param name="cursor">The original scalar writer's failure context.</param>
    /// <param name="actual">The supplied element count.</param>
    /// <param name="expected">The declared element count.</param>
    private static void RequireLength(ref WriteCursor cursor, int actual, int expected)
    {
        if (actual > expected)
        {
            throw cursor.FailArrayTooMany("matrix", expected, "matrix", "uint16<");
        }

        if (actual != expected)
        {
            throw cursor.FailArrayLengthMismatch("matrix", expected, actual, "matrix", "uint16<");
        }
    }

    /// <summary>Checks the complete generated diagnostic and inner exception against the original cursor failure.</summary>
    /// <param name="expected">The original failure, or null after success.</param>
    /// <param name="actual">The generated failure, or null after success.</param>
    private static void AssertSameFailure(CStructException? expected, CStructException? actual)
    {
        Assert.AreEqual(expected?.GetType(), actual?.GetType());
        Assert.AreEqual(expected?.Message, actual?.Message);
        Assert.AreEqual(expected?.Offset, actual?.Offset);
        Assert.AreEqual(expected?.Path, actual?.Path);
        Assert.AreEqual(expected?.Member, actual?.Member);
        Assert.AreEqual(expected?.MemberType, actual?.MemberType);
        Assert.AreEqual(expected?.InnerException?.GetType(), actual?.InnerException?.GetType());
        Assert.AreEqual(expected?.InnerException?.Message, actual?.InnerException?.Message);
    }

    /// <summary>Records each public stream-write invocation without counting its internal memory copy twice.</summary>
    private sealed class ObservedWriteStream : MemoryStream
    {
        /// <summary>Gets the number of synchronous array-write calls received.</summary>
        public int SynchronousWrites { get; private set; }

        /// <summary>Gets the number of asynchronous memory-write calls received.</summary>
        public int AsynchronousWrites { get; private set; }

        /// <inheritdoc/>
        public override void Write(byte[] buffer, int offset, int count)
        {
            this.SynchronousWrites++;
            base.Write(buffer, offset, count);
        }

        /// <inheritdoc/>
        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.AsynchronousWrites++;
            cancellationToken.ThrowIfCancellationRequested();
            byte[] copy = buffer.ToArray();
            base.Write(copy, 0, copy.Length);
            return ValueTask.CompletedTask;
        }
    }
}
