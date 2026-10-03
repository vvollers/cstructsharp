namespace CStructSharp.Generated.Parity;

using System;
using System.Threading;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Generators.Tests;

/// <summary>Checks values, independent ownership and unchanged failures when generated readers decode numeric rows.</summary>
[TestClass]
public class MultidimensionalArrayTests
{
    /// <summary>The original cursor-consumption sequence used as the failure contract for each focused layout.</summary>
    private enum ArrayLayout
    {
        Mixed,
        Union,
        Block,
    }

    /// <summary>Two and three dimensions preserve byte order, declared shape, and independent owned rows on every parse.</summary>
    [TestMethod]
    public void NumericRows_PreserveShapeEndianAndOwnership()
    {
        byte[] bytes = MixedBytes();
        MultidimensionalArrayLayouts.Mixed.Root first = MultidimensionalArrayLayouts.Mixed.Parse(bytes);
        MultidimensionalArrayLayouts.Mixed.Root second = MultidimensionalArrayLayouts.Mixed.Parse(bytes);
        ParityComparer.AssertSame(MultidimensionalArrayLayouts.Mixed.Layout.Parse(bytes, "root"), first, "root", strict: true);

        Assert.AreEqual((byte)7, first.Tag);
        Assert.AreEqual((byte)9, first.Tail);
        Assert.AreEqual(2, first.Matrix.Length);
        CollectionAssert.AreEqual(new ushort[] { 1, 2, 3 }, first.Matrix[0]);
        CollectionAssert.AreEqual(new ushort[] { 4, 5, 6 }, first.Matrix[1]);
        Assert.AreEqual(2, first.Cube.Length);
        Assert.AreEqual(2, first.Cube[0].Length);
        CollectionAssert.AreEqual(new uint[] { 0x10001, 0x10002, 0x10003 }, first.Cube[0][0]);
        CollectionAssert.AreEqual(new uint[] { 0x1000A, 0x1000B, 0x1000C }, first.Cube[1][1]);
        Assert.AreNotSame(first.Matrix[0], first.Matrix[1]);
        Assert.AreNotSame(first.Matrix[0], second.Matrix[0]);
        Assert.AreNotSame(first.Cube[0], first.Cube[1]);
        Assert.AreNotSame(first.Cube[0][0], first.Cube[0][1]);
        Assert.AreNotSame(first.Cube[0][0], second.Cube[0][0]);

        Array.Fill(bytes, (byte)0);
        first.Matrix[0][0] = 99;
        first.Cube[0][0][0] = 99;
        Assert.AreEqual((ushort)1, second.Matrix[0][0]);
        Assert.AreEqual((ushort)4, first.Matrix[1][0]);
        Assert.AreEqual(0x10001u, second.Cube[0][0][0]);
        Assert.AreEqual(0x10004u, first.Cube[0][1][0]);
    }

    /// <summary>Any zero dimension keeps the existing fresh empty outer array, without adding empty inner rows.</summary>
    [TestMethod]
    public void ZeroDimensions_KeepEmptyOuterArrays()
    {
        byte[] bytes = [9];
        MultidimensionalArrayLayouts.Empty.Root first = MultidimensionalArrayLayouts.Empty.Parse(bytes);
        MultidimensionalArrayLayouts.Empty.Root second = MultidimensionalArrayLayouts.Empty.Parse(bytes);
        Assert.IsEmpty(first.First);
        Assert.IsEmpty(first.Middle);
        Assert.IsEmpty(first.Last);
        Assert.AreNotSame(first.First, second.First);
        Assert.AreNotSame(first.Middle, second.Middle);
        Assert.AreNotSame(first.Last, second.Last);
        Assert.AreEqual((byte)9, first.Tail);
    }

    /// <summary>All short inputs, read budgets and array limits preserve the original generated cursor failures.</summary>
    [TestMethod]
    public void NumericRows_PreserveTruncationsBudgetsAndCancellation()
    {
        byte[] bytes = MixedBytes();

        // Adapt the generated result to the common failure comparator without reflection.
        Func<byte[], ReadOptions?, object> parse = static (data, options) => MultidimensionalArrayLayouts.Mixed.Parse(data, options);
        for (int length = 0; length < bytes.Length; length++)
        {
            AssertFailureContract(ArrayLayout.Mixed, parse, bytes[..length], null);
        }

        for (int budget = 0; budget < bytes.Length; budget++)
        {
            AssertFailureContract(ArrayLayout.Mixed, parse, bytes, new ReadOptions { MaxTotalBytesRead = budget });
        }

        AssertFailureContract(ArrayLayout.Mixed, parse, bytes, new ReadOptions { MaxArrayElements = 5 });
        AssertFailureContract(ArrayLayout.Mixed, parse, bytes, new ReadOptions { MaxArrayElements = 11 });
        MultidimensionalArrayLayouts.Mixed.Parse(bytes, new ReadOptions { MaxArrayElements = 12, MaxTotalBytesRead = bytes.Length });

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // A cancelled parse must not publish a partial row graph and must preserve the supplied token.
        OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() => MultidimensionalArrayLayouts.Mixed.Parse(bytes, new ReadOptions { CancellationToken = cancellation.Token }));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
    }

    /// <summary>A union retains all overlapping values and charges every original per-element read.</summary>
    [TestMethod]
    public void UnionRows_PreserveValuesAndRepeatedReadBudgets()
    {
        byte[] bytes = [7, 0, 1, 0, 2, 0, 3, 0, 4, 0, 5, 0, 6, 9];
        MultidimensionalArrayLayouts.Union.Root value = MultidimensionalArrayLayouts.Union.Parse(bytes);
        ParityComparer.AssertSame(MultidimensionalArrayLayouts.Union.Layout.Parse(bytes, "root"), value, "root", strict: true);
        CollectionAssert.AreEqual(new ushort[] { 4, 5, 6 }, value.Content.Matrix[1]);
        CollectionAssert.AreEqual(bytes[1..13], value.Content.RawStorage);

        // Use the same compiled layout for both paths while varying the exact first failing read.
        Func<byte[], ReadOptions?, object> parse = static (data, options) => MultidimensionalArrayLayouts.Union.Parse(data, options);
        for (int budget = 0; budget < 38; budget++)
        {
            AssertFailureContract(ArrayLayout.Union, parse, bytes, new ReadOptions { MaxTotalBytesRead = budget });
        }

        MultidimensionalArrayLayouts.Union.Parse(bytes, new ReadOptions { MaxTotalBytesRead = 38 });
        for (int length = 0; length < bytes.Length; length++)
        {
            AssertFailureContract(ArrayLayout.Union, parse, bytes[..length], null);
        }
    }

    /// <summary>Row boundaries do not replace the original 64 KiB block boundaries for odd-width elements.</summary>
    [TestMethod]
    public void NumericRows_PreserveBlockBoundaryFailures()
    {
        byte[] bytes = new byte[65540];
        bytes[0] = 7;
        bytes[^1] = 9;
        for (int index = 0; index < 21846; index++)
        {
            int value = index + 1;
            bytes[1 + (index * 3)] = (byte)(value >> 16);
            bytes[2 + (index * 3)] = (byte)(value >> 8);
            bytes[3 + (index * 3)] = (byte)value;
        }

        MultidimensionalArrayLayouts.Block.Root parsed = MultidimensionalArrayLayouts.Block.Parse(bytes);
        Assert.AreEqual(1u, parsed.Matrix[0][0]);
        Assert.AreEqual(10924u, parsed.Matrix[1][0]);
        Assert.AreEqual(21846u, parsed.Matrix[1][^1]);
        Assert.AreEqual((byte)9, parsed.Tail);
        ParityComparer.AssertSame(MultidimensionalArrayLayouts.Block.Layout.Parse(bytes, "root"), parsed, "root", strict: true);

        // The original whole-field block reader remains the failure oracle, independent of row lengths.
        Func<byte[], ReadOptions?, object> parse = static (data, options) => MultidimensionalArrayLayouts.Block.Parse(data, options);
        foreach (int length in new[] { 1, 32770, 65535, 65536, 65537, 65538, 65539 })
        {
            AssertFailureContract(ArrayLayout.Block, parse, bytes[..length], null);
        }

        foreach (int budget in new[] { 1, 32770, 65535, 65536, 65537, 65538, 65539 })
        {
            AssertFailureContract(ArrayLayout.Block, parse, bytes, new ReadOptions { MaxTotalBytesRead = budget });
        }
    }

    /// <summary>Builds the small mixed-endian payload with distinguishable values in every row.</summary>
    /// <returns>Owned bytes for the mixed matrix and cube, including their leading and trailing scalar fields.</returns>
    private static byte[] MixedBytes()
    {
        byte[] bytes = new byte[50];
        bytes[0] = 7;
        for (int index = 0; index < 6; index++)
        {
            bytes[1 + (index * 2)] = (byte)(index + 1);
        }

        for (int index = 0; index < 12; index++)
        {
            bytes[13 + (index * 3)] = 1;
            bytes[15 + (index * 3)] = (byte)(index + 1);
        }

        bytes[^1] = 9;
        return bytes;
    }

    /// <summary>Compares the precise first failure with the generated reader's original cursor-consumption sequence.</summary>
    /// <param name="layout">The original validation and consumption sequence.</param>
    /// <param name="parse">The generated reader.</param>
    /// <param name="bytes">The full input or a truncated prefix.</param>
    /// <param name="options">The read limits under test, or the defaults.</param>
    private static void AssertFailureContract(ArrayLayout layout, Func<byte[], ReadOptions?, object> parse, byte[] bytes, ReadOptions? options)
    {
        CStructException expected = OriginalCursorFailure(layout, bytes, options);

        // Capture the generated failure without changing its exception type or metadata.
        CStructException actual = Assert.Throws<CStructException>(() => parse(bytes, options));
        Assert.AreEqual(expected.GetType(), actual.GetType());
        Assert.AreEqual(expected.Message, actual.Message);
        Assert.AreEqual(expected.Offset, actual.Offset);
        Assert.AreEqual(expected.Path, actual.Path);
        Assert.AreEqual(expected.Member, actual.Member);
        Assert.AreEqual(expected.MemberType, actual.MemberType);
        Assert.AreEqual(expected.InnerException?.GetType(), actual.InnerException?.GetType());
        Assert.AreEqual(expected.InnerException?.Message, actual.InnerException?.Message);
    }

    /// <summary>
    ///     Exercises the original generated reader's public cursor operations without materializing its arrays.
    ///     Failure comparison uses this sequence because runtime short reads have an inner exception and some runtime
    ///     budget failures report a different offset; those existing differences must not be changed by row decoding.
    /// </summary>
    /// <param name="layout">The layout's fixed consumption sequence.</param>
    /// <param name="bytes">The complete source or a truncated prefix.</param>
    /// <param name="options">The limits applied before decoding.</param>
    /// <returns>The original generated failure, including its field context and cursor position.</returns>
    private static CStructException OriginalCursorFailure(ArrayLayout layout, byte[] bytes, ReadOptions? options)
    {
        var cursor = new ReadCursor(bytes, options, "root");
        try
        {
            cursor.EnterComposite("root", null);
            cursor.Take(1, "tag", "uint8");
            switch (layout)
            {
            case ArrayLayout.Mixed:
                cursor.RequireArrayLength(6, "matrix", "uint16<");
                cursor.TakeInBlocks(6, 2, "matrix", "uint16<");
                cursor.RequireArrayLength(12, "cube", "uint24>");
                cursor.TakeInBlocks(12, 3, "cube", "uint24>");
                break;
            case ArrayLayout.Union:
                cursor.EnterComposite("content", "value");
                cursor.Take(12, "content", "value");
                cursor.EnterUnion();
                cursor.Position = 1;
                cursor.RequireArrayLength(6, "matrix", "uint16>");
                cursor.TakeElements(6, 2, "matrix", "uint16>");
                cursor.Position = 1;
                cursor.RequireArrayLength(12, "octets", "uint8");
                cursor.TakeElements(12, 1, "octets", "uint8");
                cursor.ExitUnion();
                cursor.Position = 13;
                cursor.ExitComposite();
                break;
            case ArrayLayout.Block:
                cursor.RequireArrayLength(21846, "matrix", "uint24>");
                cursor.TakeInBlocks(21846, 3, "matrix", "uint24>");
                break;
            }

            cursor.Take(1, "tail", "uint8");
        }
        catch (CStructException failure)
        {
            cursor.Complete(failure);
            return failure;
        }

        Assert.Fail("The original cursor sequence should fail for this input and limits.");
        throw new InvalidOperationException("Unreachable after failed assertion.");
    }
}
