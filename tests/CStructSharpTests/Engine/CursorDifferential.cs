namespace CStructSharp.Tests;

using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Reading;
using CStructSharp.Streams;

/// <summary>
///     Drives <see cref="StreamReadCursor"/> and <see cref="MemoryReadCursor"/> through the same scripted steps over the
///     same input and records a trace per side: every step's value or failure (type, message, offset, inner cause) and the
///     position after it, relative to the data's first byte, then the position flushed back to the source. Equal traces
///     under every read budget mean equal charge points; equal traces after a <see cref="CursorOperation.Cancel"/> step
///     mean equal cancellation boundaries. The golden reference (<see cref="EngineGolden"/>) holds the traces of
///     <c>ReadBudgetStream</c> driven by the reads of a stream source: the stream cursor's traces are checked against it, and
///     the memory cursor's against the stream cursor's (<see cref="Expected"/>).
/// </summary>
internal static class CursorDifferential
{
    /// <summary>The per-string budget of every run: small, so bounded and terminated texts can exceed it.</summary>
    public const long MaxStringBytes = 24;

    /// <summary>The array index of a <see cref="EngineInput.Memory"/> input's first byte, so array offsets are exercised.</summary>
    private const int MemoryOffset = 3;

    /// <summary>The input forms a <see cref="MemoryReadCursor"/> reads: pinned regions, arrays and an exposed memory stream.</summary>
    public static readonly EngineInput[] MemoryForms = [EngineInput.Span, EngineInput.ByteArray, EngineInput.Memory, EngineInput.ExposedStream];

    /// <summary>The stream forms only a <see cref="StreamReadCursor"/> reads.</summary>
    public static readonly EngineInput[] StreamOnlyForms =
        [EngineInput.Stream, EngineInput.ChunkedStream1, EngineInput.ChunkedStream3, EngineInput.ChunkedStream7, EngineInput.FileStream];

    /// <summary>The terminated-string encodings and terminators a <see cref="CursorOperation.Terminated"/> step selects.</summary>
    private static readonly (Encoding Encoding, char Terminator)[] TerminatedForms =
    [
        (PrimitiveCodecs.StrictAsciiEncoding, '\0'),
        (PrimitiveCodecs.StrictUtf8Encoding, '\n'),
        (PrimitiveCodecs.StrictUtf16LittleEndianEncoding, '\0'),
        (PrimitiveCodecs.StrictUtf16BigEndianEncoding, '\n'),
    ];

    /// <summary>The bounded-text types a <see cref="CursorOperation.Bounded"/> step selects.</summary>
    private static readonly string[] BoundedTypes = ["utf8", "latin1", "utf16le", "cp437"];

    /// <summary>The element codecs an <see cref="CursorOperation.Array"/> step selects: one per width and both byte orders.</summary>
    private static readonly PrimitiveCodec[] ArrayCodecs =
    [
        new(PrimitiveCodecKind.UInt8, 1, true, '\0', true),
        new(PrimitiveCodecKind.Int16, 2, true, '\0', true),
        new(PrimitiveCodecKind.UInt24, 3, false, '\0', true),
        new(PrimitiveCodecKind.Int32, 4, false, '\0', true),
        new(PrimitiveCodecKind.Float64, 8, true, '\0', true),
        new(PrimitiveCodecKind.Bool, 1, true, '\0', true),
    ];

    /// <summary>Gets the number of array codecs a script may select.</summary>
    public static int ArrayCodecCount => ArrayCodecs.Length;

    /// <summary>Gets the number of terminated forms a script may select.</summary>
    public static int TerminatedFormCount => TerminatedForms.Length;

    /// <summary>Gets the number of bounded types a script may select.</summary>
    public static int BoundedTypeCount => BoundedTypes.Length;

    /// <summary>
    ///     Runs <paramref name="script"/> on the reference side of a comparison: the stream cursor (<see cref="StreamCursor"/>),
    ///     whose traces the golden outcomes check.
    /// </summary>
    /// <param name="input">The input form.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="script">The steps.</param>
    /// <param name="budget">The operation's <c>MaxTotalBytesRead</c>.</param>
    /// <returns>The trace.</returns>
    public static List<string> Expected(EngineInput input, byte[] data, CursorStep[] script, long budget)
        => StreamCursor(input, data, script, budget);

    /// <summary>Runs <paramref name="script"/> through a <see cref="StreamReadCursor"/> over the operation's budget stream.</summary>
    /// <param name="input">The input form.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="script">The steps.</param>
    /// <param name="budget">The operation's <c>MaxTotalBytesRead</c>.</param>
    /// <returns>The trace.</returns>
    public static List<string> StreamCursor(EngineInput input, byte[] data, CursorStep[] script, long budget)
    {
        return WithSource(input, data, (source, start) =>
        {
            using var cancellation = new CancellationTokenSource();
            using var stream = new ReadBudgetStream(source, MaxStringBytes, budget, cancellation.Token);
            var cursor = new StreamReadCursor(stream);
            return RunCursor(ref cursor, script, start, cancellation, source);
        });
    }

    /// <summary>
    ///     Runs <paramref name="script"/> through a <see cref="MemoryReadCursor"/>: over a pinned copy (span), the array
    ///     itself (byte array), an array at an offset (memory) or a caller's exposed memory stream.
    /// </summary>
    /// <param name="input">A memory form.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="script">The steps.</param>
    /// <param name="budget">The operation's <c>MaxTotalBytesRead</c>.</param>
    /// <param name="planted">Whether to run the <see cref="PlantedReadCursor"/> defect instead.</param>
    /// <returns>The trace.</returns>
    public static unsafe List<string> MemoryCursor(EngineInput input, byte[] data, CursorStep[] script, long budget, bool planted)
    {
        using var cancellation = new CancellationTokenSource();
        switch (input)
        {
        case EngineInput.Span:
            {
                byte[] copy = (byte[])data.Clone();
                fixed (byte* region = copy)
                {
                    var cursor = new MemoryReadCursor(region, copy.Length, 0, MaxStringBytes, budget, cancellation.Token);
                    return Drive(ref cursor, script, 0, cancellation, null, planted);
                }
            }

        case EngineInput.ByteArray:
            {
                var cursor = new MemoryReadCursor((byte[])data.Clone(), 0, data.Length, 0, MaxStringBytes, budget, cancellation.Token);
                return Drive(ref cursor, script, 0, cancellation, null, planted);
            }

        case EngineInput.Memory:
            {
                var cursor = new MemoryReadCursor(Backing(data), MemoryOffset, data.Length, 0, MaxStringBytes, budget, cancellation.Token);
                return Drive(ref cursor, script, 0, cancellation, null, planted);
            }

        case EngineInput.ExposedStream:
            {
                using Stream source = EngineStreams.Open(input, data);
                Assert.IsTrue(MemoryReadCursor.TryCreate(source, MaxStringBytes, budget, cancellation.Token, out MemoryReadCursor cursor));
                Assert.AreEqual(EngineStreams.ExposedStart, source.Position, "creating the cursor leaves the caller's stream alone");
                return Drive(ref cursor, script, EngineStreams.ExposedStart, cancellation, source, planted);
            }

        default:
            throw new ArgumentOutOfRangeException(nameof(input), input, "Not a memory form.");
        }
    }

    /// <summary>Opens <paramref name="input"/> over <paramref name="data"/> as <c>ReadBudgetStream</c>'s source and runs <paramref name="run"/> on it.</summary>
    /// <param name="input">The input form.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="run">Runs the script given the source and the position of the data's first byte.</param>
    /// <returns>The trace <paramref name="run"/> returns.</returns>
    public static unsafe List<string> WithSource(EngineInput input, byte[] data, Func<Stream, long, List<string>> run)
    {
        switch (input)
        {
        case EngineInput.Span:
        case EngineInput.ByteArray:
            {
                // The public span and array overloads pin the input and read it through a read-only region stream.
                byte[] copy = (byte[])data.Clone();
                fixed (byte* region = copy)
                {
                    using var source = new FixedBufferStream(region, copy.Length, writable: false);
                    return run(source, 0);
                }
            }

        case EngineInput.Memory:
            {
                byte[] backing = Backing(data);
                fixed (byte* region = backing)
                {
                    using var source = new FixedBufferStream(region + MemoryOffset, data.Length, writable: false);
                    return run(source, 0);
                }
            }

        default:
            {
                using Stream source = EngineStreams.Open(input, data);
                return run(source, EngineStreams.StartOf(input));
            }
        }
    }

    /// <summary>Copies <paramref name="data"/> into a larger array at <see cref="MemoryOffset"/>, surrounded by filler.</summary>
    /// <param name="data">The input bytes.</param>
    /// <returns>The backing array.</returns>
    private static byte[] Backing(byte[] data)
    {
        byte[] backing = new byte[MemoryOffset + data.Length + 2];
        backing.AsSpan().Fill(0xEE);
        data.CopyTo(backing, MemoryOffset);
        return backing;
    }

    /// <summary>Runs the script on <paramref name="cursor"/>, or on the planted defect wrapped around it.</summary>
    /// <param name="cursor">The memory cursor.</param>
    /// <param name="script">The steps.</param>
    /// <param name="start">The absolute position of the data's first byte.</param>
    /// <param name="cancellation">The source of the cursor's token.</param>
    /// <param name="owner">The caller's stream the cursor flushes to, or null.</param>
    /// <param name="planted">Whether to run the planted defect.</param>
    /// <returns>The trace.</returns>
    private static List<string> Drive(ref MemoryReadCursor cursor, CursorStep[] script, long start, CancellationTokenSource cancellation, Stream? owner, bool planted)
    {
        if (!planted)
        {
            return RunCursor(ref cursor, script, start, cancellation, owner);
        }

        var defect = new PlantedReadCursor(cursor);
        return RunCursor(ref defect, script, start, cancellation, owner);
    }

    /// <summary>The generic executor shape: runs every step through <see cref="IReadCursor"/> members only.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The cursor.</param>
    /// <param name="script">The steps.</param>
    /// <param name="start">The absolute position of the data's first byte.</param>
    /// <param name="cancellation">The source of the cursor's token.</param>
    /// <param name="owner">The caller's stream the final position is flushed to, or null to read the cursor's.</param>
    /// <returns>The trace.</returns>
    private static List<string> RunCursor<TCursor>(ref TCursor cursor, CursorStep[] script, long start, CancellationTokenSource cancellation, Stream? owner)
        where TCursor : struct, IReadCursor
    {
        var trace = new List<string>(script.Length + 1);
        foreach (CursorStep step in script)
        {
            string result;
            try
            {
                result = ApplyToCursor(ref cursor, step, start, cancellation);
            }
            catch (Exception exception) when (exception is not AssertFailedException)
            {
                result = Render(exception);
            }

            trace.Add($"{step} -> {result} @{cursor.Position - start}");
        }

        cursor.FlushPosition();
        trace.Add($"flushed @{(owner?.Position ?? cursor.Position) - start}");
        return trace;
    }

    /// <summary>Performs one step on a cursor.</summary>
    /// <typeparam name="TCursor">The cursor type.</typeparam>
    /// <param name="cursor">The cursor.</param>
    /// <param name="step">The step.</param>
    /// <param name="start">The absolute position of the data's first byte.</param>
    /// <param name="cancellation">The source of the cursor's token.</param>
    /// <returns>The rendered result.</returns>
    private static string ApplyToCursor<TCursor>(ref TCursor cursor, CursorStep step, long start, CancellationTokenSource cancellation)
        where TCursor : struct, IReadCursor
    {
        switch (step.Operation)
        {
        case CursorOperation.Fixed:
            {
                Span<byte> scratch = stackalloc byte[step.A];
                return Hex(cursor.ReadFixed(scratch));
            }

        case CursorOperation.Seek:
            cursor.Position = start + step.A;
            return "ok";
        case CursorOperation.Skip:
            cursor.Skip(step.A);
            return "ok";
        case CursorOperation.Align:
            cursor.Align(start + step.A, step.B);
            return "ok";
        case CursorOperation.Array:
            return RenderArray(cursor.ReadPrimitiveArray(ArrayCodecs[step.A], step.B));
        case CursorOperation.Terminated:
            return Quote(cursor.ReadTerminatedString(TerminatedForms[step.A].Encoding, TerminatedForms[step.A].Terminator));
        case CursorOperation.Bounded:
            return Quote(cursor.ReadBoundedText(step.A, BoundedTypes[step.B]));
        case CursorOperation.IsShortBy:
            return cursor.IsShortBy(step.A) ? "short" : "not short";
        case CursorOperation.SpanWithinBudget:
            return cursor.TryReadSpanWithinBudget(step.A, out ReadOnlySpan<byte> span) ? Hex(span) : "declined";
        case CursorOperation.BlockWithinBudget:
            {
                byte[] block = new byte[step.A];
                return cursor.TryReadBlockWithinBudget(block) ? Hex(block) : "declined";
            }

        case CursorOperation.Exact:
            {
                byte[] bytes = new byte[step.A];
                cursor.ReadExactly(bytes);
                return Hex(bytes);
            }

        case CursorOperation.ByteExactly:
            return cursor.ReadByteExactly().ToString("X2", CultureInfo.InvariantCulture);
        case CursorOperation.PeekAdvance:
            {
                if (!cursor.TryPeekRemaining(out ReadOnlySpan<byte> remaining))
                {
                    return "no peek";
                }

                int consumed = Math.Min(step.A, remaining.Length);
                cursor.Advance(consumed);
                return "advanced " + consumed;
            }

        case CursorOperation.Length:
            return (cursor.Length - start).ToString(CultureInfo.InvariantCulture);
        case CursorOperation.Cancel:
            cancellation.Cancel();
            return "cancelled";
        case CursorOperation.Checkpoint:
            cursor.ThrowIfCancellationRequested();
            return "ok";
        default:
            throw new ArgumentOutOfRangeException(nameof(step), step, "Unknown operation.");
        }
    }

    /// <summary>Renders a failure: its type, message, attached offset and inner cause.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The text.</returns>
    private static string Render(Exception exception)
    {
        var text = new StringBuilder("!").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
        if (exception is CStructException { Offset: { } offset })
        {
            text.Append(" [offset ").Append(offset).Append(']');
        }

        if (exception.InnerException is { } inner)
        {
            text.Append(" <- ").Append(inner.GetType().Name).Append(": ").Append(inner.Message);
        }

        return text.ToString();
    }

    /// <summary>Renders an array value: its runtime type, length and a hash of its elements' invariant texts.</summary>
    /// <param name="values">The array value.</param>
    /// <returns>The text.</returns>
    private static string RenderArray(IList<object?> values)
    {
        ulong hash = 14695981039346656037;
        foreach (object? value in values)
        {
            foreach (char character in Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null")
            {
                hash = (hash ^ character) * 1099511628211;
            }

            hash = (hash ^ ',') * 1099511628211;
        }

        return $"{values.GetType().Name}[{values.Count}] {hash:X16}";
    }

    /// <summary>Renders bytes as hexadecimal.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The text.</returns>
    private static string Hex(ReadOnlySpan<byte> bytes) => Convert.ToHexString(bytes);

    /// <summary>Renders text with its control characters escaped.</summary>
    /// <param name="text">The text.</param>
    /// <returns>The quoted text.</returns>
    private static string Quote(string text) => "\"" + text.Replace("\0", "\\0", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal) + "\"";
}
