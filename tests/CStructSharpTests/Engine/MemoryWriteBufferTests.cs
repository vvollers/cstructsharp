namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Streams;

/// <summary>
///     The compiled engine's memory write destination (<see cref="MemoryWriteBuffer"/>) against a reference stream: scripts
///     of position moves, writes, zero fills, blocks, budget checks and read-backs run on both a buffer and a
///     <see cref="WriteBudgetStream"/> over the same destination (a <see cref="FixedBufferStream"/> for a span, an
///     <see cref="OwnedMemoryStream"/> for a new array), and every result, exception, position, length and byte must agree.
/// </summary>
[TestClass]
public class MemoryWriteBufferTests
{
    /// <summary>The largest span a script writes into.</summary>
    private const int SpanCapacity = 8200;

    /// <summary>Writes a block with a charge (a span cannot be a generic argument, so this is a delegate type of its own).</summary>
    /// <param name="block">The bytes.</param>
    /// <param name="charged">The charge.</param>
    private delegate void WriteBlockAction(ReadOnlySpan<byte> block, int charged);

    /// <summary>Gets the scripts, as data rows: a name, the options, the span capacity (-1 for a new array) and the operations.</summary>
    public static IEnumerable<object[]> Scripts
    {
        get
        {
            var free = new WriteOptions();
            var tight = new WriteOptions { MaxTotalBytesWritten = 5, MaxStringBytes = 3, };
            var huge = new WriteOptions { MaxTotalBytesWritten = long.MaxValue, };
            string[] gaps = ["write 1 2 3", "position 6", "write 7 8", "position 0", "read 12", "zeroes 3", "write-byte 9", "position 20", "write-array 4 5", "read 4"];
            string[] budget = ["write 1 2 3", "write 4 5 6", "position 10", "write", "zeroes 0", "position 1", "zeroes 4", "zeroes 5", "string 3", "string 4", "string -1"];
            string[] blocks = ["afford 4 4", "block 1 2 3 4 / 2", "afford 1 9", "afford 2147483647 1", "afford 1 2147483647", "block 5 6 / 1", "position 3", "block 7 8 9", "read 9"];
            string[] room = ["position 4", "write 1 2 3", "position 9", "position -1", "zeroes 8195", "position 8199", "write 1 2", "write-byte 3", "afford 2 0", "afford 1 0", "read 1"];
            string[] limits = ["position -1", "position 2147483648", "position 2147483646", "write 1 2", "write-byte 1"];
            foreach (int capacity in (int[])[-1, 0, 5, SpanCapacity])
            {
                yield return ["gaps", free, capacity, gaps];
                yield return ["budget", tight, capacity, budget];
                yield return ["blocks", tight, capacity, blocks];
                yield return ["room", free, capacity, room];
            }

            yield return ["limits", huge, -1, limits];
        }
    }

    /// <summary>A script's operations give the same results on the engine's buffer as on a budget stream over the same destination.</summary>
    /// <param name="name">The script's name.</param>
    /// <param name="options">The operation's options, which set the budgets.</param>
    /// <param name="capacity">The span's length, or -1 for a new array.</param>
    /// <param name="script">The operations.</param>
    [TestMethod]
    [DynamicData(nameof(Scripts))]
    public unsafe void Script_BehavesAsTheBudgetStreams(string name, WriteOptions options, int capacity, string[] script)
    {
        byte[] engineStorage = Enumerable.Repeat((byte)0xCC, Math.Max(capacity, 0)).ToArray();
        byte[] referenceStorage = (byte[])engineStorage.Clone();
        fixed (byte* engineRegion = engineStorage)
        {
            fixed (byte* referenceRegion = referenceStorage)
            {
                using MemoryWriteBuffer buffer = capacity < 0 ? MemoryWriteBuffer.ForNewArray(options) : MemoryWriteBuffer.ForSpan(engineRegion, capacity, options);
                using Stream inner = capacity < 0 ? new OwnedMemoryStream() : new FixedBufferStream(referenceRegion, capacity, writable: true);
                using var stream = new WriteBudgetStream(inner, options);
                string expected = Run(script, new Target(stream, stream.WriteZeroes, stream.EnsureStringBytes, (size, charged) => stream.CanAffordBlock(size, charged) && (inner is not FixedBufferStream region || stream.Position + size <= region.Capacity), stream.WriteBlock));
                string actual = Run(script, new Target(buffer, buffer.WriteZeroes, buffer.EnsureStringBytes, buffer.CanAffordBlock, buffer.WriteBlock));
                Assert.AreEqual(expected, actual, name + " (capacity " + capacity + ")");
                CollectionAssert.AreEqual(referenceStorage, engineStorage, name + ": the span holds the same bytes");
                CollectionAssert.AreEqual(capacity < 0 ? ((MemoryStream)inner).ToArray() : referenceStorage[..(int)inner.Length], buffer.ToArray(), name + ": the written bytes agree");
            }
        }
    }

    /// <summary>
    ///     The same scripts against a union's staging buffer and a reference staging stream - a budget stream over a
    ///     fixed <see cref="MemoryStream"/> holding the union's zero bytes - agree too, including a write past the union.
    /// </summary>
    /// <param name="name">The script's name.</param>
    /// <param name="options">The operation's options.</param>
    /// <param name="capacity">The union's size (for a -1 row, 12).</param>
    /// <param name="script">The operations.</param>
    [TestMethod]
    [DynamicData(nameof(Scripts))]
    public void StagingScript_BehavesAsTheStagingBudgetStream(string name, WriteOptions options, int capacity, string[] script)
    {
        int size = capacity < 0 ? 12 : capacity;
        byte[] engineStorage = new byte[size + 4];
        byte[] referenceStorage = new byte[size];
        using MemoryWriteBuffer buffer = MemoryWriteBuffer.ForStaging(engineStorage, size, options);
        using var inner = new MemoryStream(referenceStorage, writable: true);
        using var stream = new WriteBudgetStream(inner, options);
        string expected = Run(script, new Target(stream, stream.WriteZeroes, stream.EnsureStringBytes, stream.CanAffordBlock, stream.WriteBlock));
        string actual = Run(script, new Target(buffer, buffer.WriteZeroes, buffer.EnsureStringBytes, buffer.CanAffordBlock, buffer.WriteBlock));
        Assert.AreEqual(expected, actual, name + " (staging " + size + ")");
        CollectionAssert.AreEqual(referenceStorage, engineStorage[..size], name + ": the staged bytes agree");
        Assert.IsFalse(buffer.AllowsBlocks);
    }

    /// <summary>The operations the engine never uses are refused rather than half-implemented.</summary>
    [TestMethod]
    public void SeekAndSetLength_AreNotSupported()
    {
        using MemoryWriteBuffer buffer = MemoryWriteBuffer.ForNewArray(new WriteOptions());
        Assert.Throws<NotSupportedException>(() => buffer.Seek(0, SeekOrigin.Begin));
        Assert.Throws<NotSupportedException>(() => buffer.SetLength(1));
        buffer.Flush();
        Assert.IsTrue(buffer.CanRead && buffer.CanWrite && buffer.CanSeek);
        Assert.AreEqual(new WriteOptions().MaxStringBytes, buffer.MaxStringBytes);
    }

    /// <summary>Runs a script and renders every result and the state after each operation.</summary>
    /// <param name="script">The operations: <c>position N</c>, <c>write B..</c>, <c>write-array B..</c>, <c>write-byte B</c>, <c>zeroes N</c>, <c>read N</c>, <c>string N</c>, <c>afford SIZE CHARGED</c>, <c>block B.. [/ CHARGED]</c>.</param>
    /// <param name="target">The implementation.</param>
    /// <returns>The rendering.</returns>
    private static string Run(string[] script, Target target)
    {
        var output = new StringBuilder();
        foreach (string line in script)
        {
            string[] words = line.Split(' ');
            string result;
            try
            {
                result = Apply(words, target);
            }
            catch (Exception exception)
            {
                result = Describe(exception);
            }

            output.Append(line).Append(" => ").Append(result).Append(" @").Append(target.Stream.Position).Append(" len ").Append(target.Stream.Length).Append('\n');
        }

        return output.ToString();
    }

    /// <summary>Applies one operation.</summary>
    /// <param name="words">The operation's words.</param>
    /// <param name="target">The implementation.</param>
    /// <returns>The operation's result.</returns>
    private static string Apply(string[] words, Target target)
    {
        // Parses the byte values of an operation, up to a charge separator.
        byte[] Bytes(IEnumerable<string> values) => values.TakeWhile(value => value != "/").Select(byte.Parse).ToArray();
        switch (words[0])
        {
        case "position":
            target.Stream.Position = long.Parse(words[1]);
            return "ok";
        case "write":
            target.Stream.Write(Bytes(words.Skip(1)).AsSpan());
            return "ok";
        case "write-array":
            byte[] array = Bytes(words.Skip(1));
            target.Stream.Write(array, 0, array.Length);
            return "ok";
        case "write-byte":
            target.Stream.WriteByte(byte.Parse(words[1]));
            return "ok";
        case "zeroes":
            target.WriteZeroes(int.Parse(words[1]));
            return "ok";
        case "read":
            byte[] read = new byte[int.Parse(words[1])];
            int count = target.Stream.Read(read, 0, read.Length);
            return count + ": " + Convert.ToHexString(read, 0, count);
        case "string":
            target.EnsureStringBytes(long.Parse(words[1]));
            return "ok";
        case "afford":
            return target.CanAffordBlock(int.Parse(words[1]), int.Parse(words[2])).ToString();
        case "block":
            byte[] block = Bytes(words.Skip(1));
            int slash = Array.IndexOf(words, "/");
            target.WriteBlock(block, slash < 0 ? block.Length : int.Parse(words[slash + 1]));
            return "ok";
        default:
            throw new ArgumentException("Unknown operation: " + words[0]);
        }
    }

    /// <summary>Renders a failure: its type and message, its offset for a library failure, and its inner failure.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The rendering.</returns>
    private static string Describe(Exception exception)
    {
        string text = exception.GetType().Name + ": " + exception.Message;
        if (exception is CStructException library)
        {
            text += " (offset " + library.Offset + ")";
        }

        return exception.InnerException is { } inner ? text + " <- " + Describe(inner) : text;
    }

    /// <summary>One implementation under test: the stream and its budget operations.</summary>
    /// <param name="Stream">The stream the positions, writes and reads go through.</param>
    /// <param name="WriteZeroes">The zero fill.</param>
    /// <param name="EnsureStringBytes">The per-string limit check.</param>
    /// <param name="CanAffordBlock">Whether a block fits the budget and the destination's room.</param>
    /// <param name="WriteBlock">The block write.</param>
    private sealed record Target(Stream Stream, Action<int> WriteZeroes, Action<long> EnsureStringBytes, Func<int, int, bool> CanAffordBlock, WriteBlockAction WriteBlock);
}
