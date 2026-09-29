namespace CStructSharp.Tests;

/// <summary>
///     Opens the stream forms of <see cref="EngineInput"/> over a copy of some bytes, so a differential read or record
///     sequence can run over every kind of stream the library distinguishes: a hidden-buffer memory stream, an exposed
///     buffer at a non-zero origin, fragmenting streams, and a real file.
/// </summary>
internal static class EngineStreams
{
    /// <summary>
    ///     The stream position at which the data of an <see cref="EngineInput.ExposedStream"/> starts: odd, so the sweeps
    ///     check that aligned members are placed from the record's own first byte, not from the stream's origin.
    /// </summary>
    public const int ExposedStart = 5;

    /// <summary>The number of buffer bytes before an <see cref="EngineInput.ExposedStream"/>'s origin.</summary>
    private const int ExposedOrigin = 3;

    /// <summary>The stream forms, in the order the sweeps run them.</summary>
    public static readonly EngineInput[] All =
    [
        EngineInput.Stream, EngineInput.ExposedStream, EngineInput.ChunkedStream1, EngineInput.ChunkedStream3, EngineInput.ChunkedStream7, EngineInput.FileStream,
    ];

    /// <summary>Returns whether <paramref name="input"/> is one of the stream forms.</summary>
    /// <param name="input">The input form.</param>
    /// <returns><see langword="true"/> for a stream form.</returns>
    public static bool IsStream(EngineInput input) => input is EngineInput.Stream or >= EngineInput.ExposedStream;

    /// <summary>
    ///     The stream position at which the data of <paramref name="input"/> starts: <see cref="ExposedStart"/> for an
    ///     exposed stream, 0 otherwise. A read that follows absolute pointers sees the same targets as a span only when
    ///     it passes this value as a relative <see cref="ReadOptions.Origin"/>.
    /// </summary>
    /// <param name="input">A stream form.</param>
    /// <returns>The start position.</returns>
    public static long StartOf(EngineInput input) => input == EngineInput.ExposedStream ? ExposedStart : 0;

    /// <summary>
    ///     Opens <paramref name="input"/> over a copy of <paramref name="data"/>, positioned at the data's first byte
    ///     (<see cref="StartOf"/>). Every stream is read-only except the file stream, which the caller must dispose to
    ///     delete its temporary file.
    /// </summary>
    /// <param name="input">A stream form.</param>
    /// <param name="data">The bytes the stream holds after its start.</param>
    /// <returns>The open stream.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="input"/> is not a stream form.</exception>
    public static Stream Open(EngineInput input, byte[] data)
    {
        switch (input)
        {
        case EngineInput.Stream:
            return new MemoryStream((byte[])data.Clone(), writable: false);

        case EngineInput.ExposedStream:
            {
                // Filler before the origin and between the origin and the data: neither is part of the input, and a
                // read that strays into them sees 0xEE or 0xDD rather than plausible zeros.
                byte[] buffer = new byte[ExposedOrigin + ExposedStart + data.Length];
                buffer.AsSpan(0, ExposedOrigin).Fill(0xEE);
                buffer.AsSpan(ExposedOrigin, ExposedStart).Fill(0xDD);
                data.CopyTo(buffer, ExposedOrigin + ExposedStart);
                var stream = new MemoryStream(buffer, ExposedOrigin, ExposedStart + data.Length, writable: false, publiclyVisible: true);
                stream.Position = ExposedStart;
                return stream;
            }

        case EngineInput.ChunkedStream1:
            return new ChunkedMemoryStream((byte[])data.Clone(), 1, writable: false);

        case EngineInput.ChunkedStream3:
            return new ChunkedMemoryStream((byte[])data.Clone(), 3, writable: false);

        case EngineInput.ChunkedStream7:
            return new ChunkedMemoryStream((byte[])data.Clone(), 7, writable: false);

        case EngineInput.FileStream:
            {
                string path = Path.Combine(Path.GetTempPath(), "cstructsharp-engine-" + Guid.NewGuid().ToString("N") + ".bin");
                var file = new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose);
                file.Write(data);
                file.Position = 0;
                return file;
            }

        default:
            throw new ArgumentOutOfRangeException(nameof(input), input, "Not a stream form.");
        }
    }
}
