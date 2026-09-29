namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;

/// <summary>
///     The cursor differential: <see cref="MemoryReadCursor"/> and <see cref="StreamReadCursor"/> against
///     <c>ReadBudgetStream</c> driven the way the interpreter drives it, over every input form the sweeps use, with
///     scripted operation sequences under a sweep of read budgets. Traces compare values, positions after every step
///     (including after failures), failure types, messages, offsets and inner causes, the charge points (the step at
///     which each budget fails), cancellation observation points and the flushed final position.
/// </summary>
[TestClass]
public class CursorDifferentialTests
{
    /// <summary>The length of the block-boundary input: two 64 KiB blocks and an odd tail.</summary>
    private const int LargeLength = (2 * 65536) + 37;

    /// <summary>Every budget from nothing to beyond any small script's charges, and unlimited.</summary>
    private static readonly long[] SmallBudgets =
        [.. Enumerable.Range(0, 49).Select(budget => (long)budget), 64, 96, 128, 192, 256, 257, 300, 400, 600, 1000, long.MaxValue];

    /// <summary>A sparser sweep for the file form, whose every run creates a temporary file.</summary>
    private static readonly long[] FileBudgets = [0, 1, 2, 3, 5, 8, 13, 21, 34, 64, 256, long.MaxValue];

    /// <summary>Budgets around the 64 KiB block boundaries and the ends of the large scripts' reads.</summary>
    private static readonly long[] LargeBudgets =
        [0, 1, 65535, 65536, 65537, 65538, 130000, 130001, 131071, 131072, 131073, 131076, 131077, 131109, 131200, long.MaxValue];

    /// <summary>The failure texts and types the sweeps must reach.</summary>
    private static readonly string[] FailureKinds =
    [
        ReadFailures.TotalBytesLimit, ReadFailures.OutsideRegion, ReadFailures.ShortReadPrefix, ReadFailures.BoundedTextLimit,
        ReadFailures.BoundedTextShortRead, ReadFailures.BoundedTextInvalid, ReadFailures.TerminatedStringLimit,
        ReadFailures.TerminatedStringUnterminated, ReadFailures.TerminatedStringInvalid, nameof(EndOfStreamException),
        nameof(OperationCanceledException), "declined", "no peek", "advanced",
    ];

    /// <summary>
    ///     Over small inputs (empty, text with terminators, UTF-16, invalid UTF-8, a byte ramp), both cursors produce
    ///     ReadBudgetStream's trace for every script, input form and budget; the memory cursor for every memory form.
    /// </summary>
    [TestMethod]
    public void Cursors_MatchReadBudgetStream_OverSmallInputs()
    {
        var differences = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (byte[] data in SmallInputs())
        {
            foreach (CursorStep[] script in SmallScripts())
            {
                foreach (EngineInput input in CursorDifferential.MemoryForms.Concat(CursorDifferential.StreamOnlyForms))
                {
                    foreach (long budget in input == EngineInput.FileStream ? FileBudgets : SmallBudgets)
                    {
                        CompareSides(input, data, script, budget, planted: false, differences, seen);
                    }
                }
            }
        }

        Assert.IsEmpty(differences, string.Join("\n\n", differences.Take(5)));
        AssertExercised(seen);
    }

    /// <summary>
    ///     Typed arrays across the 64 KiB block boundaries: both cursors fail the budget at the same block, preflight
    ///     the same impossible counts, and observe cancellation before the same block as ReadBudgetStream.
    /// </summary>
    [TestMethod]
    public void Cursors_MatchReadBudgetStream_AtBlockBoundaries()
    {
        byte[] data = new byte[LargeLength];
        for (int index = 0; index < data.Length; index++)
        {
            data[index] = (byte)((index * 31) + 7);
        }

        var differences = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (CursorStep[] script in LargeScripts())
        {
            foreach (EngineInput input in CursorDifferential.MemoryForms.Concat(CursorDifferential.StreamOnlyForms))
            {
                foreach (long budget in LargeBudgets)
                {
                    CompareSides(input, data, script, budget, planted: false, differences, seen);
                }
            }
        }

        Assert.IsEmpty(differences, string.Join("\n\n", differences.Take(5)));
        Assert.Contains(ReadFailures.TotalBytesLimit, seen, "some budget fails inside an array");
        Assert.Contains(nameof(OperationCanceledException), seen, "an array observes cancellation");
    }

    /// <summary>
    ///     A memory cursor whose short <c>ReadExactly</c> leaves the position at the read's start instead of the end of
    ///     the input is reported: the differential compares positions after failures, not just values.
    /// </summary>
    [TestMethod]
    public void PlantedDifference_IsReported()
    {
        var differences = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (byte[] data in SmallInputs())
        {
            foreach (CursorStep[] script in SmallScripts())
            {
                foreach (EngineInput input in CursorDifferential.MemoryForms)
                {
                    CompareSides(input, data, script, long.MaxValue, planted: true, differences, seen);
                }
            }
        }

        Assert.IsNotEmpty(differences, "the planted defect went unnoticed");
        Assert.IsTrue(differences.All(difference => difference.Contains("memory cursor", StringComparison.Ordinal)), differences[0]);
        StringAssert.Contains(differences[0], ReadFailures.ShortReadPrefix);
    }

    /// <summary>
    ///     A memory cursor is created exactly for the streams ReadBudgetStream reads from memory (an exposed memory
    ///     stream), starts at the stream's position, and writes its final position back only on flush.
    /// </summary>
    [TestMethod]
    public void MemoryCursor_IsCreatedForExposedMemoryStreamsOnly()
    {
        byte[] data = [1, 2, 3, 4, 5, 6];
        foreach (EngineInput input in CursorDifferential.StreamOnlyForms)
        {
            using Stream source = EngineStreams.Open(input, data);
            Assert.IsFalse(MemoryReadCursor.TryCreate(source, 16, 16, default, out _), input.ToString());
        }

        using Stream exposed = EngineStreams.Open(EngineInput.ExposedStream, data);
        Assert.IsTrue(MemoryReadCursor.TryCreate(exposed, 16, 16, default, out MemoryReadCursor cursor));
        Assert.AreEqual(EngineStreams.ExposedStart, cursor.Position);
        Assert.AreEqual(EngineStreams.ExposedStart + data.Length, cursor.Length);
        Span<byte> scratch = stackalloc byte[4];
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4, }, cursor.ReadFixed(scratch).ToArray());
        Assert.AreEqual(EngineStreams.ExposedStart, exposed.Position, "the caller's stream moves only on flush");
        cursor.FlushPosition();
        Assert.AreEqual(EngineStreams.ExposedStart + 4, exposed.Position);
    }

    /// <summary>
    ///     After cancellation, fixed-width and exact reads still succeed and only the documented boundaries (an explicit
    ///     checkpoint, an array block, a string chunk) throw - on every side, so the differential compares real
    ///     observation points.
    /// </summary>
    [TestMethod]
    public void Cancellation_IsObservedOnlyAtBoundaries()
    {
        byte[] data = Encoding.ASCII.GetBytes("abcdefgh\0ijklmnop");
        CursorStep[] script =
        [
            new(CursorOperation.Cancel, 0), new(CursorOperation.Fixed, 4), new(CursorOperation.Exact, 2), new(CursorOperation.ByteExactly, 0),
            new(CursorOperation.Bounded, 2, 0), new(CursorOperation.Checkpoint, 0), new(CursorOperation.Array, 0, 2), new(CursorOperation.Terminated, 0),
        ];
        List<string> trace = CursorDifferential.Reference(EngineInput.Span, data, script, long.MaxValue);
        string[] observed = [.. trace.Where(line => line.Contains(nameof(OperationCanceledException), StringComparison.Ordinal)).Select(line => line[..line.IndexOf('(', StringComparison.Ordinal)])];
        CollectionAssert.AreEqual(new[] { "Checkpoint", "Array", "Terminated", }, observed, string.Join("\n", trace));

        var differences = new List<string>();
        foreach (EngineInput input in CursorDifferential.MemoryForms.Concat(CursorDifferential.StreamOnlyForms))
        {
            CompareSides(input, data, script, long.MaxValue, planted: false, differences, new HashSet<string>(StringComparer.Ordinal));
        }

        Assert.IsEmpty(differences, string.Join("\n\n", differences));
    }

    /// <summary>Runs one case on every side that reads <paramref name="input"/> and records any trace that differs.</summary>
    /// <param name="input">The input form.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="script">The steps.</param>
    /// <param name="budget">The read budget.</param>
    /// <param name="planted">Whether the memory side runs the planted defect.</param>
    /// <param name="differences">Receives a description of each differing side.</param>
    /// <param name="seen">Receives the failure kinds the reference trace contains.</param>
    private static void CompareSides(EngineInput input, byte[] data, CursorStep[] script, long budget, bool planted, List<string> differences, HashSet<string> seen)
    {
        List<string> expected = CursorDifferential.Reference(input, data, script, budget);
        foreach (string kind in FailureKinds)
        {
            if (expected.Exists(line => line.Contains(kind, StringComparison.Ordinal)))
            {
                seen.Add(kind);
            }
        }

        string label = $"{input}, {data.Length} bytes, budget {budget}, script {string.Join(" ", script)}";
        if (!planted)
        {
            Record(differences, "stream cursor", label, expected, CursorDifferential.StreamCursor(input, data, script, budget));
        }

        if (CursorDifferential.MemoryForms.Contains(input))
        {
            Record(differences, "memory cursor", label, expected, CursorDifferential.MemoryCursor(input, data, script, budget, planted));
        }
    }

    /// <summary>Adds a description of the first differing line when the traces differ.</summary>
    /// <param name="differences">The list of differences.</param>
    /// <param name="side">The side being compared.</param>
    /// <param name="label">The case.</param>
    /// <param name="expected">ReadBudgetStream's trace.</param>
    /// <param name="actual">The side's trace.</param>
    private static void Record(List<string> differences, string side, string label, List<string> expected, List<string> actual)
    {
        int line = 0;
        while (line < expected.Count && line < actual.Count && expected[line] == actual[line])
        {
            line++;
        }

        if (line == expected.Count && line == actual.Count)
        {
            return;
        }

        differences.Add($"{side} differs ({label}) at line {line}:\n  ReadBudgetStream: {(line < expected.Count ? expected[line] : "<end>")}\n  {side}: {(line < actual.Count ? actual[line] : "<end>")}");
    }

    /// <summary>Asserts that the small sweep produced every failure kind, so no comparison is vacuous.</summary>
    /// <param name="seen">The failure kinds the reference traces contained.</param>
    private static void AssertExercised(HashSet<string> seen)
    {
        foreach (string kind in FailureKinds)
        {
            Assert.Contains(kind, seen, "no reference trace contains " + kind);
        }
    }

    /// <summary>The small inputs: empty, one byte, text with both terminators, UTF-16 text, invalid UTF-8 and a ramp.</summary>
    /// <returns>The inputs.</returns>
    private static IEnumerable<byte[]> SmallInputs()
    {
        yield return [];
        yield return [0x41];
        yield return Encoding.ASCII.GetBytes("Hi\0there\nabc\0");
        yield return [.. Encoding.Unicode.GetBytes("Ab\n\0xyz"), 0x41, .. Encoding.BigEndianUnicode.GetBytes("Q\n"), 0x00];
        yield return [0xC3, 0x28, 0x0A, 0x80, 0x00, 0xE2, 0x82, 0x41, 0x00];
        yield return [.. Enumerable.Range(0, 48).Select(value => (byte)(value * 7))];
    }

    /// <summary>Hand-written scripts for each concern, then seeded random scripts over every operation.</summary>
    /// <returns>The scripts.</returns>
    private static IEnumerable<CursorStep[]> SmallScripts()
    {
        // Every fixed width, to the end of the input and past it.
        yield return
        [
            new(CursorOperation.Fixed, 1), new(CursorOperation.Fixed, 2), new(CursorOperation.Fixed, 3), new(CursorOperation.Fixed, 4),
            new(CursorOperation.Fixed, 6), new(CursorOperation.Fixed, 8), new(CursorOperation.Fixed, 16), new(CursorOperation.Fixed, 4),
            new(CursorOperation.Fixed, 1), new(CursorOperation.Length, 0),
        ];

        // Positions: inside, at and past the end, negative, relative moves and alignment from an odd origin.
        yield return
        [
            new(CursorOperation.Seek, 3), new(CursorOperation.Fixed, 4), new(CursorOperation.Skip, -2), new(CursorOperation.Fixed, 2),
            new(CursorOperation.Seek, 1000), new(CursorOperation.Seek, -1), new(CursorOperation.Seek, 13), new(CursorOperation.Seek, 0),
            new(CursorOperation.Skip, 5), new(CursorOperation.Align, 0, 8), new(CursorOperation.Align, 1, 4), new(CursorOperation.Fixed, 1),
            new(CursorOperation.Align, 3, 16), new(CursorOperation.Skip, 40), new(CursorOperation.Align, 0, 64),
        ];

        // Terminated strings in every form, rereading from the start.
        yield return
        [
            new(CursorOperation.Terminated, 0), new(CursorOperation.Terminated, 1), new(CursorOperation.Seek, 0), new(CursorOperation.Terminated, 2),
            new(CursorOperation.Seek, 0), new(CursorOperation.Terminated, 3), new(CursorOperation.Seek, 1), new(CursorOperation.Terminated, 2),
        ];

        // Bounded text: in range, odd UTF-16, over the string budget, short, invalid.
        yield return
        [
            new(CursorOperation.Bounded, 5, 0), new(CursorOperation.Bounded, 4, 2), new(CursorOperation.Bounded, 30, 1),
            new(CursorOperation.Bounded, 3, 3), new(CursorOperation.Bounded, 3, 2), new(CursorOperation.Seek, 0), new(CursorOperation.Bounded, 20, 0),
            new(CursorOperation.Bounded, 0, 0),
        ];

        // Typed arrays of every codec, empty, and a count the input cannot back.
        yield return
        [
            new(CursorOperation.Array, 0, 5), new(CursorOperation.Array, 1, 3), new(CursorOperation.Array, 2, 2), new(CursorOperation.Array, 0, 0),
            new(CursorOperation.Array, 3, 100), new(CursorOperation.Seek, 0), new(CursorOperation.Array, 4, 2), new(CursorOperation.Array, 5, 4),
            new(CursorOperation.Fixed, 1),
        ];

        // Staging, raw exact reads, peeking and the end of the input.
        yield return
        [
            new(CursorOperation.IsShortBy, 5), new(CursorOperation.SpanWithinBudget, 4), new(CursorOperation.BlockWithinBudget, 4),
            new(CursorOperation.Exact, 3), new(CursorOperation.ByteExactly, 0), new(CursorOperation.PeekAdvance, 2), new(CursorOperation.IsShortBy, 0),
            new(CursorOperation.Exact, 100), new(CursorOperation.ByteExactly, 0), new(CursorOperation.Exact, 0), new(CursorOperation.SpanWithinBudget, 0),
        ];

        // Cancellation: which steps observe it.
        yield return
        [
            new(CursorOperation.Fixed, 2), new(CursorOperation.Cancel, 0), new(CursorOperation.Fixed, 2), new(CursorOperation.Checkpoint, 0),
            new(CursorOperation.Array, 0, 2), new(CursorOperation.Terminated, 0), new(CursorOperation.Exact, 2), new(CursorOperation.Seek, 0),
            new(CursorOperation.Bounded, 2, 0), new(CursorOperation.Array, 0, 0),
        ];

        var random = new Random(20260929);
        for (int script = 0; script < 32; script++)
        {
            var steps = new CursorStep[random.Next(6, 13)];
            for (int index = 0; index < steps.Length; index++)
            {
                steps[index] = RandomStep(random);
            }

            yield return steps;
        }
    }

    /// <summary>Draws one step with operands sized for the small inputs.</summary>
    /// <param name="random">The seeded generator.</param>
    /// <returns>The step.</returns>
    private static CursorStep RandomStep(Random random)
    {
        int[] widths = [1, 2, 3, 4, 6, 8, 16];
        int[] alignments = [1, 2, 4, 8];
        return random.Next(16) switch
        {
            0 => new(CursorOperation.Fixed, widths[random.Next(widths.Length)]),
            1 => new(CursorOperation.Seek, random.Next(-2, 50)),
            2 => new(CursorOperation.Skip, random.Next(-6, 10)),
            3 => new(CursorOperation.Align, random.Next(0, 4), alignments[random.Next(alignments.Length)]),
            4 => new(CursorOperation.Array, random.Next(CursorDifferential.ArrayCodecCount), random.Next(0, 12)),
            5 => new(CursorOperation.Terminated, random.Next(CursorDifferential.TerminatedFormCount)),
            6 => new(CursorOperation.Bounded, random.Next(0, 30), random.Next(CursorDifferential.BoundedTypeCount)),
            7 => new(CursorOperation.IsShortBy, random.Next(0, 50)),
            8 => new(CursorOperation.SpanWithinBudget, random.Next(0, 12)),
            9 => new(CursorOperation.BlockWithinBudget, random.Next(0, 12)),
            10 => new(CursorOperation.Exact, random.Next(0, 12)),
            11 => new(CursorOperation.ByteExactly, 0),
            12 => new(CursorOperation.PeekAdvance, random.Next(0, 8)),
            13 => new(CursorOperation.Length, 0),
            14 => new(random.Next(3) == 0 ? CursorOperation.Cancel : CursorOperation.Checkpoint, 0),
            _ => new(CursorOperation.Checkpoint, 0),
        };
    }

    /// <summary>Scripts over the large input that cross the 64 KiB block boundaries.</summary>
    /// <returns>The scripts.</returns>
    private static IEnumerable<CursorStep[]> LargeScripts()
    {
        // Exactly two blocks of int16, then fixed and exact reads that run short at the tail.
        yield return [new(CursorOperation.Array, 1, 65536), new(CursorOperation.Fixed, 4), new(CursorOperation.Exact, 40), new(CursorOperation.Length, 0)];

        // A block and a partial one, cancellation before the next array, and fixed reads that ignore it.
        yield return
        [
            new(CursorOperation.Fixed, 1), new(CursorOperation.Array, 0, 131000), new(CursorOperation.Cancel, 0), new(CursorOperation.Fixed, 2),
            new(CursorOperation.Array, 3, 3), new(CursorOperation.Checkpoint, 0),
        ];

        // A count the input cannot back fails before reading, and the position ends at the end.
        yield return [new(CursorOperation.Array, 2, 50000), new(CursorOperation.Fixed, 1)];

        // Float64 blocks, then booleans and a terminated string from the tail.
        yield return [new(CursorOperation.Array, 4, 16384), new(CursorOperation.Array, 5, 30), new(CursorOperation.Terminated, 0)];

        // An odd start: blocks of whole elements that no longer match the input's 64 KiB offsets.
        yield return [new(CursorOperation.Seek, 1), new(CursorOperation.Array, 1, 65000), new(CursorOperation.PeekAdvance, 5000)];
    }
}
