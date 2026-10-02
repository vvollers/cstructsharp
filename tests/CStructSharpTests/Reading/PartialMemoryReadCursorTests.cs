namespace CStructSharp.Tests;

using System.Runtime.CompilerServices;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Streams;

/// <summary>
///     Exercises a memory cursor over the first part of a longer input (<see cref="BufferedInput"/>) directly: it models
///     the whole input's length and positions, raises <see cref="BufferedInputShortfallException"/> for bytes past the
///     buffered part, and fails as the whole input would for positions past the input's end.
/// </summary>
[TestClass]
public class PartialMemoryReadCursorTests
{
    /// <summary>
    ///     A known whole length (here one that needs more than 32 bits) is the cursor's length: a position may pass the
    ///     buffered bytes up to it, a read there asks for more of the input, and a position past it fails.
    /// </summary>
    [TestMethod]
    public unsafe void KnownLength_ModelsTheWholeInput()
    {
        const long whole = (1L << 40) + 3;
        byte[] buffered = [1, 2, 3, 4,];
        fixed (byte* region = buffered)
        {
            // The helpers take the address, since a lambda cannot capture a fixed local.
            nint address = (nint)region;
            var cursor = new MemoryReadCursor(region, buffered.Length, 0, long.MaxValue, long.MaxValue, default, whole);

            Assert.AreEqual(whole, cursor.Length);
            Assert.IsFalse(cursor.IsShortBy(whole), "the input holds every byte to its end");
            Assert.IsTrue(cursor.IsShortBy(whole + 1), "no byte lies past the input's end");
            Assert.IsFalse(cursor.EndsAtOrBefore(whole - 1));
            Assert.IsTrue(cursor.EndsAtOrBefore(whole));

            cursor.Position = whole;
            Assert.AreEqual(whole, cursor.Position, "the position may reach the end of the input past the buffered bytes");
            try
            {
                cursor.ReadExactly(new byte[1]);
                Assert.Fail("No byte lies past the input's end.");
            }
            catch (CStructReadException failure)
            {
                StringAssert.Contains(failure.Message, ReadFailures.ShortRead(1, 0));
                Assert.AreEqual(whole, cursor.Position, "a short read leaves the position at the end of the whole input");
            }

            BufferedInputShortfallException shortfall = Assert.Throws<BufferedInputShortfallException>(() => ReadOneByte(address, buffered.Length, whole, 6));
            Assert.AreEqual(7L, shortfall.NeededLength, "the byte's end, from the input's first byte");
            Assert.Throws<CStructReadException>(() => MoveTo(address, buffered.Length, whole, whole + 1));
        }
    }

    /// <summary>
    ///     An unknown length, and a known length too long to record (2^48 bytes or more), ask for more of the input
    ///     wherever the answer needs the input's end; the largest recordable length is reported exactly.
    /// </summary>
    [TestMethod]
    public unsafe void UnknownOrUnrecordableLength_AsksForMoreInput()
    {
        byte[] buffered = [1, 2, 3, 4,];
        fixed (byte* region = buffered)
        {
            // The helpers take the address, since a lambda cannot capture a fixed local.
            nint address = (nint)region;
            foreach (long continued in new[] { BufferedInput.UnknownLength, 1L << 48, long.MaxValue, })
            {
                var cursor = new MemoryReadCursor(region, buffered.Length, 0, long.MaxValue, long.MaxValue, default, continued);
                BufferedInputShortfallException shortfall = Assert.Throws<BufferedInputShortfallException>(() => LengthOf(address, buffered.Length, continued));
                Assert.AreEqual(long.MaxValue, shortfall.NeededLength, "the length needs the whole input");
                Assert.IsFalse(cursor.EndsAtOrBefore(3), "the buffered bytes answer an address they hold");
                Assert.AreEqual(9L, Assert.Throws<BufferedInputShortfallException>(() => MoveTo(address, buffered.Length, continued, 9)).NeededLength);
            }

            var largest = new MemoryReadCursor(region, buffered.Length, 0, long.MaxValue, long.MaxValue, default, (1L << 48) - 1);
            Assert.AreEqual((1L << 48) - 1, largest.Length);
        }
    }

    /// <summary>
    ///     The whole length survives its split into a low 32-bit and a high 16-bit part, also when bit 31 is set (the low
    ///     part is stored as a negative <see cref="int"/>) and at the edges of the high part.
    /// </summary>
    /// <param name="whole">The whole input's length in bytes.</param>
    [TestMethod]
    [DataRow(5L)]
    [DataRow(1L << 31)]
    [DataRow((1L << 32) - 1)]
    [DataRow(1L << 32)]
    [DataRow((1L << 32) + (1L << 31) + 7)]
    [DataRow((1L << 47) + (1L << 31))]
    public unsafe void KnownLength_SurvivesThePackedSplit(long whole)
    {
        byte[] buffered = [1, 2, 3, 4,];
        fixed (byte* region = buffered)
        {
            var cursor = new MemoryReadCursor(region, buffered.Length, 0, long.MaxValue, long.MaxValue, default, whole);

            Assert.AreEqual(whole, cursor.Length);
            Assert.IsFalse(cursor.EndsAtOrBefore(whole - 1));
            Assert.IsTrue(cursor.EndsAtOrBefore(whole));
            cursor.Position = whole;
            Assert.AreEqual(whole, cursor.Position);
        }
    }

    /// <summary>
    ///     The core keeps the size it has without the buffered-input support - the whole length shares the array-offset
    ///     slot and padding - since it is embedded in every budget stream and every memory cursor a span read creates.
    /// </summary>
    [TestMethod]
    public void Core_KeepsItsSize()
    {
        // Two pointer-sized members (the region and the array), four longs, and one 8-byte group of small members.
        Assert.AreEqual((2 * IntPtr.Size) + (4 * sizeof(long)) + 8, Unsafe.SizeOf<MemoryReadCore>());
    }

    /// <summary>Reads one byte at <paramref name="position"/> from a fresh partly buffered cursor.</summary>
    /// <param name="region">The address of the buffered bytes, pinned by the caller.</param>
    /// <param name="length">The buffered length in bytes.</param>
    /// <param name="continued">The whole input's length, or <see cref="BufferedInput.UnknownLength"/>.</param>
    /// <param name="position">The position to read at.</param>
    /// <returns>The byte.</returns>
    private static unsafe byte ReadOneByte(nint region, int length, long continued, long position)
    {
        var cursor = new MemoryReadCursor((byte*)region, length, 0, long.MaxValue, long.MaxValue, default, continued);
        cursor.Position = position;
        return cursor.ReadByteExactly();
    }

    /// <summary>Moves a fresh partly buffered cursor to <paramref name="position"/>.</summary>
    /// <param name="region">The address of the buffered bytes, pinned by the caller.</param>
    /// <param name="length">The buffered length in bytes.</param>
    /// <param name="continued">The whole input's length, or <see cref="BufferedInput.UnknownLength"/>.</param>
    /// <param name="position">The new position.</param>
    private static unsafe void MoveTo(nint region, int length, long continued, long position)
    {
        var cursor = new MemoryReadCursor((byte*)region, length, 0, long.MaxValue, long.MaxValue, default, continued);
        cursor.Position = position;
    }

    /// <summary>Reads the length of a fresh partly buffered cursor.</summary>
    /// <param name="region">The address of the buffered bytes, pinned by the caller.</param>
    /// <param name="length">The buffered length in bytes.</param>
    /// <param name="continued">The whole input's length, or <see cref="BufferedInput.UnknownLength"/>.</param>
    /// <returns>The cursor's length.</returns>
    private static unsafe long LengthOf(nint region, int length, long continued)
        => new MemoryReadCursor((byte*)region, length, 0, long.MaxValue, long.MaxValue, default, continued).Length;
}
