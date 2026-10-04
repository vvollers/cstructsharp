namespace CStructSharp.Generated.Parity;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Generators.Tests;

/// <summary>Checks bitfield leaf values, complete ownership, repeated charges and original observable read order.</summary>
[TestClass]
[DoNotParallelize]
public class BitfieldLeafTests
{
    /// <summary>Mixed packed windows decode every field while each parse and array element owns its value.</summary>
    [TestMethod]
    public void PackedWindows_PreserveValuesBytesAndOwnership()
    {
        byte[] bytes = [0x21, 0x35, 0x15, 0xDC, 0xF1, 0x85, 0xC4, 0xE3, 0x56, 0x25, 0xAB, 0xAB, 0x45, 0xC8];
        BitfieldLeafLayouts.Packed.Root first = BitfieldLeafLayouts.Packed.Parse(bytes);
        BitfieldLeafLayouts.Packed.Root second = BitfieldLeafLayouts.Packed.Parse(bytes);
        var runtime = BitfieldLeafLayouts.Packed.Layout.Parse(bytes, "root");
        ParityComparer.AssertSame(runtime, first, "root", strict: true);
        CollectionAssert.AreEqual(BitfieldLeafLayouts.Packed.Layout.Serialize("root", runtime), BitfieldLeafLayouts.Packed.Serialize(first));
        Assert.AreNotSame(first.Items, second.Items);
        Assert.AreNotSame(first.Items[0], second.Items[0]);
        Assert.AreNotSame(first.Items[0], first.Items[1]);
        Array.Fill(bytes, (byte)0);
        first.Items[0].A = 0;
        Assert.AreEqual((byte)1, second.Items[0].A);
        Assert.AreEqual((byte)3, first.Items[1].A);
    }

    /// <summary>Compiled offsets retain alignment, both allocation directions, MSVC packing, signed casts and enums.</summary>
    [TestMethod]
    public void PackingAndSeparators_PreserveValuesAndBytes()
    {
        byte[] aligned = Filled(BitfieldLeafLayouts.AlignedLeaf.Sizes.Root);
        var alignedRuntime = BitfieldLeafLayouts.AlignedLeaf.Layout.Parse(aligned, "root");
        BitfieldLeafLayouts.AlignedLeaf.Root alignedValue = BitfieldLeafLayouts.AlignedLeaf.Parse(aligned);
        ParityComparer.AssertSame(alignedRuntime, alignedValue, "root", strict: true);
        CollectionAssert.AreEqual(BitfieldLeafLayouts.AlignedLeaf.Layout.Serialize("root", alignedRuntime), BitfieldLeafLayouts.AlignedLeaf.Serialize(alignedValue));

        byte[] msvc = Filled(BitfieldLeafLayouts.Msvc.Sizes.Root);
        var msvcRuntime = BitfieldLeafLayouts.Msvc.Layout.Parse(msvc, "root");
        BitfieldLeafLayouts.Msvc.Root msvcValue = BitfieldLeafLayouts.Msvc.Parse(msvc);
        ParityComparer.AssertSame(msvcRuntime, msvcValue, "root", strict: true);
        CollectionAssert.AreEqual(BitfieldLeafLayouts.Msvc.Layout.Serialize("root", msvcRuntime), BitfieldLeafLayouts.Msvc.Serialize(msvcValue));
    }

    /// <summary>Every incomplete source and repeated-read budget keeps the original generated failure and byte offset.</summary>
    [TestMethod]
    public void PackedLeaf_PreservesExactFailureContract()
    {
        byte[] bytes = Filled(7);
        for (int length = 0; length < bytes.Length; length++)
        {
            AssertOriginalFailure(bytes[..length], null);
        }

        for (int budget = 0; budget < 13; budget++)
        {
            AssertOriginalFailure(bytes, new ReadOptions { MaxTotalBytesRead = budget });
        }

        BitfieldLeafLayouts.Packed.ParseRec(bytes, options: new ReadOptions { MaxTotalBytesRead = 13 });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        // Entry cancellation preserves its token instead of consuming the bitfield shortcut.
        OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() => BitfieldLeafLayouts.Packed.ParseRec(bytes, options: new ReadOptions { CancellationToken = cancellation.Token }));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
    }

    /// <summary>A union retains both owned leaf values and its original repeated storage charges.</summary>
    [TestMethod]
    public void UnionAndNesting_PreserveRepeatedChargesAndLimits()
    {
        byte[] bytes = [0xB3, 0xA7];
        BitfieldLeafLayouts.Overlap.Root value = BitfieldLeafLayouts.Overlap.Parse(bytes, new ReadOptions { MaxTotalBytesRead = 10 });
        Assert.AreNotSame(value.First, value.Second);
        CollectionAssert.AreEqual(bytes, value.RawStorage);
        ParityComparer.AssertSame(BitfieldLeafLayouts.Overlap.Layout.ReadValue(bytes, "root"), value, "root", strict: true);

        // Raw storage charges two bytes, then each of the two leaves rereads its two-byte unit twice.
        CStructReadLimitException budget = Assert.Throws<CStructReadLimitException>(() => BitfieldLeafLayouts.Overlap.Parse(bytes, new ReadOptions { MaxTotalBytesRead = 9 }));
        Assert.AreEqual("b", budget.Member);
        Assert.AreEqual(0L, budget.Offset);

        // The parent remains incremental, so the child still enters its original nesting checkpoint.
        CStructReadLimitException nesting = Assert.Throws<CStructReadLimitException>(() => BitfieldLeafLayouts.Packed.Parse(Filled(14), new ReadOptions { MaxNestingDepth = 1 }));
        Assert.AreEqual("items", nesting.Member);
        Assert.AreEqual(0L, nesting.Offset);
    }

    /// <summary>Parent and leaf consumer constructors keep their calls, mutation visibility and cancellation checkpoints.</summary>
    [TestMethod]
    public void ConsumerConstructors_PreserveActionsAndCancellation()
    {
        int parentCalls = 0;
        byte[] bytes = [0, 9];

        // A parent callback runs before the child reads any storage.
        BitfieldLeafLayouts.Parent.Root.Constructing = () =>
        {
            parentCalls++;
            bytes[0] = 0x23;
        };
        try
        {
            BitfieldLeafLayouts.Parent.Root value = BitfieldLeafLayouts.Parent.Parse(bytes);
            Assert.AreEqual(1, parentCalls);
            Assert.AreEqual((byte)3, value.Child.A);
            Assert.AreEqual((byte)4, value.Child.B);
            using var cancellation = new CancellationTokenSource();
            BitfieldLeafLayouts.Parent.Root.Constructing = cancellation.Cancel;

            // Cancellation during the parent constructor is observed when entering the child.
            OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() => BitfieldLeafLayouts.Parent.Parse(bytes, new ReadOptions { CancellationToken = cancellation.Token }));
            Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        }
        finally
        {
            BitfieldLeafLayouts.Parent.Root.Constructing = null;
        }

        using var leafCancellation = new CancellationTokenSource();
        int leafCalls = 0;

        // The leaf has already entered when its constructor runs; scalar bitfield reads add no cancellation check.
        BitfieldLeafLayouts.Constructor.Rec.Constructing = () =>
        {
            leafCalls++;
            bytes[0] = 0x35;
            leafCancellation.Cancel();
        };
        try
        {
            BitfieldLeafLayouts.Constructor.Rec value = BitfieldLeafLayouts.Constructor.Parse(bytes, new ReadOptions { CancellationToken = leafCancellation.Token });
            Assert.AreEqual(1, leafCalls);
            Assert.AreEqual((byte)5, value.A);
            Assert.AreEqual((byte)6, value.B);
        }
        finally
        {
            BitfieldLeafLayouts.Constructor.Rec.Constructing = null;
        }
    }

    /// <summary>Stream adapters keep exact consumed positions and restore their origin after a leaf failure.</summary>
    /// <returns>The completion of both asynchronous read paths.</returns>
    [TestMethod]
    public async Task StreamForms_PreserveConsumptionAndFailureRestoration()
    {
        byte[] bytes = [9, .. Filled(7), 10];
        using var stream = new MemoryStream(bytes, writable: false);
        stream.Position = 1;
        BitfieldLeafLayouts.Packed.Rec synchronous = BitfieldLeafLayouts.Packed.ParseRec(stream);
        Assert.AreEqual(8L, stream.Position);
        stream.Position = 1;
        BitfieldLeafLayouts.Packed.Rec asynchronous = await BitfieldLeafLayouts.Packed.ParseRecAsync(stream);
        Assert.AreEqual(synchronous.F, asynchronous.F);
        Assert.AreEqual(8L, stream.Position);
        stream.Position = 1;

        // A rejected repeated charge must leave a seekable stream at the original position.
        CStructReadLimitException failure = await Assert.ThrowsAsync<CStructReadLimitException>(async () => await BitfieldLeafLayouts.Packed.ParseRecAsync(stream, options: new ReadOptions { MaxTotalBytesRead = 12 }));
        Assert.AreEqual("f", failure.Member);
        Assert.AreEqual(1L, stream.Position);
    }

    /// <summary>An implicit constructor's caller initializer preserves mutation and adds no new cancellation checkpoint.</summary>
    [TestMethod]
    public void ConsumerInitializer_PreservesImplicitConstructorEffects()
    {
        byte[] bytes = [0];
        int calls = 0;
        using var cancellation = new CancellationTokenSource();

        // Initializers execute after composite entry and before the original scalar bitfield reads.
        BitfieldLeafLayouts.Initializer.Initialize = () =>
        {
            calls++;
            bytes[0] = 0x35;
            cancellation.Cancel();
            return 42;
        };
        try
        {
            BitfieldLeafLayouts.Initializer.Rec value = BitfieldLeafLayouts.Initializer.Parse(bytes, new ReadOptions { CancellationToken = cancellation.Token });
            Assert.AreEqual(1, calls);
            Assert.AreEqual(42, value.Initialized);
            Assert.AreEqual((byte)5, value.A);
            Assert.AreEqual((byte)6, value.B);
        }
        finally
        {
            BitfieldLeafLayouts.Initializer.Initialize = null;
        }
    }

    /// <summary>Creates deterministic nonzero bytes for layout comparisons.</summary>
    /// <param name="length">The complete encoded extent in bytes.</param>
    /// <returns>The owned source bytes.</returns>
    private static byte[] Filled(int length)
    {
        var bytes = new byte[length];
        for (int index = 0; index < length; index++)
        {
            bytes[index] = (byte)((index * 29) + 177);
        }

        return bytes;
    }

    /// <summary>Compares all diagnostic fields with the unchanged cursor sequence of the packed leaf reader.</summary>
    /// <param name="bytes">The full input or a truncated prefix.</param>
    /// <param name="options">The read limits under test.</param>
    private static void AssertOriginalFailure(byte[] bytes, ReadOptions? options)
    {
        CStructException expected = OriginalFailure(bytes, options);

        // Capture the generated failure without changing the original cursor's public diagnostic contract.
        CStructException actual = Assert.Throws<CStructException>(() => BitfieldLeafLayouts.Packed.ParseRec(bytes, options: options));
        Assert.AreEqual(expected.GetType(), actual.GetType());
        Assert.AreEqual(expected.Message, actual.Message);
        Assert.AreEqual(expected.Offset, actual.Offset);
        Assert.AreEqual(expected.Path, actual.Path);
        Assert.AreEqual(expected.Member, actual.Member);
        Assert.AreEqual(expected.MemberType, actual.MemberType);
        Assert.AreEqual(expected.InnerException?.GetType(), actual.InnerException?.GetType());
        Assert.AreEqual(expected.InnerException?.Message, actual.InnerException?.Message);
    }

    /// <summary>Runs the original generated field placement, repeated takes and cursor resets as a failure oracle.</summary>
    /// <param name="bytes">The full input or truncated prefix.</param>
    /// <param name="options">The limits applied to the original sequence.</param>
    /// <returns>The completed original failure with its field and path metadata.</returns>
    private static CStructException OriginalFailure(byte[] bytes, ReadOptions? options)
    {
        var cursor = new ReadCursor(bytes, options, "rec");
        try
        {
            cursor.EnterComposite("rec", null);
            var placement = CompositeCursor.Start(0, false, BitfieldPacking.SysV, BitfieldAllocation.LowBitFirst);
            (string Name, string Type, int Size, int Width)[] fields =
            [
                ("a", "uint8", 1, 3), ("b", "uint8", 1, 5), ("c", "uint16", 2, 4),
                (string.Empty, "uint16", 2, 4), ("d", "uint16", 2, 8), ("e", "uint32", 4, 12), ("f", "uint32", 4, 20),
            ];
            foreach ((string name, string type, int size, int width) in fields)
            {
                BitfieldSlot slot = placement.AdvanceToBitfield(size, size, width, 56, true, name);
                cursor.Seek(slot.UnitStart, name, type);
                Codec.ReadUnsigned(cursor.Take(slot.UnitSize, name, type), true);
                if (((slot.BitOffset + width) / 8) + 1 <= slot.UnitSize)
                {
                    cursor.Position = (int)slot.UnitStart;
                }
            }

            cursor.Seek(placement.Finish(1), null, null);
        }
        catch (CStructException failure)
        {
            cursor.Complete(failure);
            return failure;
        }

        throw new InvalidOperationException("Expected the original reader to fail for these bytes and limits.");
    }
}
