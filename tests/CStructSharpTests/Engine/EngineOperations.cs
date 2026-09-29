namespace CStructSharp.Tests;

using System.Buffers;
using System.Globalization;
using CStructSharp.Fuzzing;
using CStructSharp.Values;

/// <summary>
///     Builds the <see cref="DifferentialOperation"/>s the differential harness compares: every public read, debug
///     read, address and length query, write, and update, over each input form. Each run gets its own copy of the
///     input and a fresh destination, so the two sides cannot influence each other; a failure is rendered under
///     <c>failure</c>, and stream positions, returned counts and destination contents are rendered after it.
/// </summary>
internal static class EngineOperations
{
    /// <summary>The byte a span destination is filled with before a write, so untouched capacity stays visible.</summary>
    public const byte Unwritten = 0xCC;

    /// <summary>Reads through a span; a lambda cannot capture a span, so the span arrives as a parameter.</summary>
    /// <param name="source">The input bytes.</param>
    /// <param name="options">The side's read options.</param>
    /// <returns>The operation's result.</returns>
    private delegate object? SpanRead(ReadOnlySpan<byte> source, ReadOptions options);

    /// <summary><c>Parse</c> of a root or a nested struct.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The input form.</param>
    /// <param name="path">The root name or path, or <see langword="null"/> for the first struct.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation Parse(CStruct layout, byte[] data, EngineInput input, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => Read(
            "Parse " + path,
            data,
            input,
            options,
            (source, read) => layout.Parse(source, path, variables, read),
            (source, read) => layout.Parse(source, path, variables, read),
            (source, read) => layout.Parse(source, path, variables, read),
            (source, read) => layout.Parse(source, path, variables, read),
            (source, read) => layout.Parse(source, path, variables, read),
            RenderValue,
            EngineExpectations.Read(layout, path));

    /// <summary><c>ParseAsync</c> over a stream that hides its buffer.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="path">The root name or path, or <see langword="null"/> for the first struct.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ParseAsync(CStruct layout, byte[] data, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => ParseAsync(layout, data, EngineInput.Stream, path, variables, options);

    /// <summary><c>ParseAsync</c> over one of the stream forms.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The stream form (<see cref="EngineStreams.IsStream"/>).</param>
    /// <param name="path">The root name or path, or <see langword="null"/> for the first struct.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ParseAsync(CStruct layout, byte[] data, EngineInput input, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => StreamRead("ParseAsync " + path, data, input, options, (source, read) => layout.ParseAsync(source, path, variables, read).AsTask().GetAwaiter().GetResult(), RenderValue, EngineExpectations.Read(layout, path));

    /// <summary><c>ParseMany</c>: every record until the input ends, then the record count.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The records' bytes.</param>
    /// <param name="input">The input form: <see cref="EngineInput.Memory"/>, <see cref="EngineInput.Sequence"/>, or a stream form.</param>
    /// <param name="path">The record struct's name, or <see langword="null"/> for the first struct.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ParseMany(CStruct layout, byte[] data, EngineInput input, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
    {
        return new DifferentialOperation(
            "ParseMany " + path + " (" + input + ")",
            (side, output) =>
            {
                ReadOptions read = side.Read(options);
                byte[] copy = (byte[])data.Clone();
                using Stream? stream = EngineStreams.IsStream(input) ? EngineStreams.Open(input, copy) : null;
                int count = 0;
                output.Capture(
                    "failure",
                    () =>
                    {
                        IEnumerable<StructValue> records = input switch
                        {
                            EngineInput.Memory => layout.ParseMany(copy.AsMemory(), path, variables, read),
                            EngineInput.Sequence => layout.ParseMany(ChunkedSequence.Of(copy), path, variables, read),
                            _ when stream is not null => layout.ParseMany(stream, path, variables, read),
                            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "ParseMany reads memory, a sequence, or a stream."),
                        };
                        foreach (StructValue record in records)
                        {
                            output.Value("record[" + count + "]", record);
                            count++;
                        }
                    });
                output.Line("count", count.ToString(CultureInfo.InvariantCulture));
                RenderPosition(output, stream, EngineStreams.IsStream(input) ? EngineStreams.StartOf(input) : 0);
            },
            EngineExpectations.Read(layout, path));
    }

    /// <summary><c>ReadValue</c> of a root or a path, in its natural representation.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The input form.</param>
    /// <param name="path">The root name or path, or <see langword="null"/> for the first struct or union.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ReadValue(CStruct layout, byte[] data, EngineInput input, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => Read(
            "ReadValue " + path,
            data,
            input,
            options,
            (source, read) => layout.ReadValue(source, path, variables, read),
            (source, read) => layout.ReadValue(source, path, variables, read),
            (source, read) => layout.ReadValue(source, path, variables, read),
            (source, read) => layout.ReadValue(source, path, variables, read),
            (source, read) => layout.ReadValue(source, path, variables, read),
            RenderValue,
            EngineExpectations.Read(layout, path));

    /// <summary><c>ReadValue&lt;T&gt;</c> of a root or a path, converted or bound to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The destination type.</typeparam>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The input form.</param>
    /// <param name="path">The root name or path, or <see langword="null"/> for the first struct or union.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ReadValue<T>(CStruct layout, byte[] data, EngineInput input, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => Read(
            "ReadValue<" + CanonicalText.TypeName(typeof(T)) + "> " + path,
            data,
            input,
            options,
            (source, read) => layout.ReadValue<T>(source, path, variables, read),
            (source, read) => layout.ReadValue<T>(source, path, variables, read),
            (source, read) => layout.ReadValue<T>(source, path, variables, read),
            (source, read) => layout.ReadValue<T>(source, path, variables, read),
            (source, read) => layout.ReadValue<T>(source, path, variables, read),
            RenderValue,
            EngineExpectations.Read(layout, path));

    /// <summary><c>ParseWithDebug</c>: the struct, then every debug record.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The input form.</param>
    /// <param name="path">The root name or path, or <see langword="null"/> for the first struct.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ParseWithDebug(CStruct layout, byte[] data, EngineInput input, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => Read(
            "ParseWithDebug " + path,
            data,
            input,
            options,
            (source, read) => layout.ParseWithDebug(source, path, variables, read),
            (source, read) => layout.ParseWithDebug(source, path, variables, read),
            (source, read) => layout.ParseWithDebug(source, path, variables, read),
            (source, read) => layout.ParseWithDebug(source, path, variables, read),
            (source, read) => layout.ParseWithDebug(source, path, variables, read),
            RenderDebugResult,
            EngineExpectations.DebugRead(layout, path));

    /// <summary><c>ParseWithDebugAsync</c> over one of the stream forms: the struct, then every debug record in stream coordinates.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The stream form (<see cref="EngineStreams.IsStream"/>).</param>
    /// <param name="path">The root name or path, or <see langword="null"/> for the first struct.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ParseWithDebugAsync(CStruct layout, byte[] data, EngineInput input, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => StreamRead(
            "ParseWithDebugAsync " + path,
            data,
            input,
            options,
            (source, read) => layout.ParseWithDebugAsync(source, path, variables, read).AsTask().GetAwaiter().GetResult(),
            RenderDebugResult,
            EngineExpectations.DebugRead(layout, path));

    /// <summary><c>ReadValueWithDebugAsync</c> over one of the stream forms: the value, then every debug record in stream coordinates.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The stream form (<see cref="EngineStreams.IsStream"/>).</param>
    /// <param name="path">The root name or path, or <see langword="null"/> for the first struct or union.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ReadValueWithDebugAsync(CStruct layout, byte[] data, EngineInput input, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => StreamRead(
            "ReadValueWithDebugAsync " + path,
            data,
            input,
            options,
            (source, read) => layout.ReadValueWithDebugAsync(source, path, variables, read).AsTask().GetAwaiter().GetResult(),
            RenderDebugResult,
            EngineExpectations.DebugRead(layout, path));

    /// <summary><c>ReadValueWithDebug</c>: the value, then every debug record.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The input form.</param>
    /// <param name="path">The root name or path, or <see langword="null"/> for the first struct or union.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ReadValueWithDebug(CStruct layout, byte[] data, EngineInput input, string? path = null, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => Read(
            "ReadValueWithDebug " + path,
            data,
            input,
            options,
            (source, read) => layout.ReadValueWithDebug(source, path, variables, read),
            (source, read) => layout.ReadValueWithDebug(source, path, variables, read),
            (source, read) => layout.ReadValueWithDebug(source, path, variables, read),
            (source, read) => layout.ReadValueWithDebug(source, path, variables, read),
            (source, read) => layout.ReadValueWithDebug(source, path, variables, read),
            RenderDebugResult,
            EngineExpectations.DebugRead(layout, path));

    /// <summary><c>ResolveAddress</c>: the position of a path.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The input form.</param>
    /// <param name="path">The path to locate.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation ResolveAddress(CStruct layout, byte[] data, EngineInput input, string path, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => Read(
            "ResolveAddress " + path,
            data,
            input,
            options,
            (source, read) => layout.ResolveAddress(source, path, variables, read),
            (source, read) => layout.ResolveAddress(source, path, variables, read),
            (source, read) => layout.ResolveAddress(source, path, variables, read),
            (source, read) => layout.ResolveAddress(source, path, variables, read),
            (source, read) => layout.ResolveAddress(source, path, variables, read),
            RenderValue,
            EngineExpectations.Read(layout, path));

    /// <summary><c>GetArrayLength</c>: the element or character count of a path.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input bytes.</param>
    /// <param name="input">The input form.</param>
    /// <param name="path">The path of an array or string.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation GetArrayLength(CStruct layout, byte[] data, EngineInput input, string path, IReadOnlyDictionary<string, int>? variables = null, ReadOptions? options = null)
        => Read(
            "GetArrayLength " + path,
            data,
            input,
            options,
            (source, read) => layout.GetArrayLength(source, path, variables, read),
            (source, read) => layout.GetArrayLength(source, path, variables, read),
            (source, read) => layout.GetArrayLength(source, path, variables, read),
            (source, read) => layout.GetArrayLength(source, path, variables, read),
            (source, read) => layout.GetArrayLength(source, path, variables, read),
            RenderValue,
            EngineExpectations.Read(layout, path));

    /// <summary><c>Serialize</c> to a new byte array.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="path">The root name or path to write.</param>
    /// <param name="value">The value to encode; it is shared by both sides and must not be changed by a write.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's write options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation Serialize(CStruct layout, string path, object value, IReadOnlyDictionary<string, int>? variables = null, WriteOptions? options = null)
    {
        return new DifferentialOperation(
            "Serialize " + path,
            (side, output) => output.Capture("failure", () => output.Bytes("result", layout.Serialize(path, value, variables, side.Write(options)))),
            EngineExpectations.RootWrite(layout, path, options));
    }

    /// <summary><c>Serialize</c> into a span of <paramref name="capacity"/> bytes filled with <see cref="Unwritten"/>: the returned count, then the whole span.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="capacity">The destination's length in bytes.</param>
    /// <param name="path">The root name or path to write.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's write options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation SerializeToSpan(CStruct layout, int capacity, string path, object value, IReadOnlyDictionary<string, int>? variables = null, WriteOptions? options = null)
    {
        return new DifferentialOperation(
            "Serialize(Span[" + capacity + "]) " + path,
            (side, output) =>
            {
                byte[] destination = new byte[capacity];
                destination.AsSpan().Fill(Unwritten);
                output.Capture("failure", () => output.Value("result", layout.Serialize(destination.AsSpan(), path, value, variables, side.Write(options))));
                output.Bytes("destination", destination);
            },
            EngineExpectations.RootWrite(layout, path, options));
    }

    /// <summary><c>Serialize</c> to an <see cref="ArrayBufferWriter{T}"/>: the returned count, then everything the writer holds.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="path">The root name or path to write.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's write options, which each side adjusts.</param>
    /// <param name="initialCapacity">The buffer writer's initial capacity; a small one makes the write ask for more.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation SerializeToBufferWriter(CStruct layout, string path, object value, IReadOnlyDictionary<string, int>? variables = null, WriteOptions? options = null, int initialCapacity = 1)
    {
        return new DifferentialOperation(
            "Serialize(IBufferWriter) " + path,
            (side, output) =>
            {
                var destination = new ArrayBufferWriter<byte>(initialCapacity);
                output.Capture("failure", () => output.Value("result", layout.Serialize(destination, path, value, variables, side.Write(options))));
                output.Bytes("destination", destination.WrittenSpan);
            });
    }

    /// <summary>
    ///     <c>Serialize</c> to a <see cref="WindowedBufferWriter"/> that hands out windows of <paramref name="window"/>
    ///     bytes: the returned count, then everything advanced.
    /// </summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="window">The window size in bytes for requests that hint a smaller size.</param>
    /// <param name="path">The root name or path to write.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's write options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation SerializeToWindows(CStruct layout, int window, string path, object value, IReadOnlyDictionary<string, int>? variables = null, WriteOptions? options = null)
    {
        return new DifferentialOperation(
            "Serialize(IBufferWriter windows of " + window + ") " + path,
            (side, output) =>
            {
                var destination = new WindowedBufferWriter(window);
                output.Capture("failure", () => output.Value("result", layout.Serialize(destination, path, value, variables, side.Write(options))));
                output.Bytes("destination", destination.Written);
            });
    }

    /// <summary><c>Write</c> into a stream that already holds <paramref name="prefill"/>, starting at <paramref name="start"/>: the final position, then the stream's contents.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="prefill">The stream's bytes before the write.</param>
    /// <param name="start">The position the write starts at.</param>
    /// <param name="path">The root name or path to write.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's write options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation Write(CStruct layout, byte[] prefill, long start, string path, object value, IReadOnlyDictionary<string, int>? variables = null, WriteOptions? options = null)
        => StreamWrite("Write " + path, prefill, start, (stream, side) => layout.Write(stream, path, value, variables, side.Write(options)));

    /// <summary><c>WriteAsync</c> into a stream that already holds <paramref name="prefill"/>, starting at <paramref name="start"/>.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="prefill">The stream's bytes before the write.</param>
    /// <param name="start">The position the write starts at.</param>
    /// <param name="path">The root name or path to write.</param>
    /// <param name="value">The value to encode.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's write options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation WriteAsync(CStruct layout, byte[] prefill, long start, string path, object value, IReadOnlyDictionary<string, int>? variables = null, WriteOptions? options = null)
        => StreamWrite("WriteAsync " + path, prefill, start, (stream, side) => layout.WriteAsync(stream, path, value, variables, side.Write(options)).AsTask().GetAwaiter().GetResult(), EngineExpectations.RootWrite(layout, path, options));

    /// <summary><c>Update</c> of a span or a stream holding <paramref name="data"/>: the data afterwards, and a stream's final position.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The bytes to update; each side updates its own copy.</param>
    /// <param name="input">The destination form: <see cref="EngineInput.Span"/> or <see cref="EngineInput.Stream"/>.</param>
    /// <param name="path">The path of the value to replace.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's update options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation Update(CStruct layout, byte[] data, EngineInput input, string path, object value, IReadOnlyDictionary<string, int>? variables = null, UpdateOptions? options = null)
    {
        if (input == EngineInput.Stream)
        {
            return StreamWrite("Update " + path, data, 0, (stream, side) => layout.Update(stream, path, value, variables, side.Update(options)));
        }

        if (input != EngineInput.Span)
        {
            throw new ArgumentOutOfRangeException(nameof(input), input, "Update changes a span or a stream.");
        }

        return new DifferentialOperation(
            "Update(Span) " + path,
            (side, output) =>
            {
                byte[] copy = (byte[])data.Clone();
                output.Capture("failure", () => layout.Update(copy.AsSpan(), path, value, variables, side.Update(options)));
                output.Bytes("data", copy);
            });
    }

    /// <summary><c>UpdateAsync</c> of a stream holding <paramref name="data"/>.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The bytes to update; each side updates its own copy.</param>
    /// <param name="path">The path of the value to replace.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The case's update options, which each side adjusts.</param>
    /// <returns>The operation.</returns>
    public static DifferentialOperation UpdateAsync(CStruct layout, byte[] data, string path, object value, IReadOnlyDictionary<string, int>? variables = null, UpdateOptions? options = null)
        => StreamWrite("UpdateAsync " + path, data, 0, (stream, side) => layout.UpdateAsync(stream, path, value, variables, side.Update(options)).AsTask().GetAwaiter().GetResult());

    /// <summary>Renders a result as a value under <c>result</c>.</summary>
    /// <param name="output">The rendering.</param>
    /// <param name="result">The result.</param>
    private static void RenderValue(CanonicalText output, object? result) => output.Value("result", result);

    /// <summary>Renders a <see cref="ParseResult"/> or <see cref="ReadResult"/>: the value under <c>result</c>, the records under <c>debug</c>.</summary>
    /// <param name="output">The rendering.</param>
    /// <param name="result">The debug result.</param>
    private static void RenderDebugResult(CanonicalText output, object? result)
    {
        (object? value, IReadOnlyList<Diagnostics.DebugData> debug) = result switch
        {
            ParseResult parse => ((object?)parse.Value, parse.Debug),
            ReadResult read => (read.Value, read.Debug),
            _ => throw new ArgumentException("Not a debug result: " + result, nameof(result)),
        };
        output.Value("result", value);
        output.Debug("debug", debug);
    }

    /// <summary>
    ///     Renders a stream's final position under <c>position</c>, when there is a stream, relative to the position its
    ///     data starts at, so every stream form renders the same number for the same outcome.
    /// </summary>
    /// <param name="output">The rendering.</param>
    /// <param name="stream">The stream, or <see langword="null"/>.</param>
    /// <param name="start">The stream position of the data's first byte.</param>
    private static void RenderPosition(CanonicalText output, Stream? stream, long start = 0)
    {
        if (stream is not null)
        {
            output.Line("position", (stream.Position - start).ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>A read over one input form; the stream form also renders its final position.</summary>
    /// <param name="name">The operation's name.</param>
    /// <param name="data">The input bytes; each run reads its own copy.</param>
    /// <param name="input">The input form.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <param name="span">The call over a span.</param>
    /// <param name="array">The call over a byte array.</param>
    /// <param name="memory">The call over read-only memory.</param>
    /// <param name="stream">The call over a stream.</param>
    /// <param name="sequence">The call over a multi-segment sequence.</param>
    /// <param name="render">Renders the call's result.</param>
    /// <param name="engine">Whether automatic selection must run the engine (<see cref="DifferentialOperation.Engine"/>).</param>
    /// <returns>The operation.</returns>
    private static DifferentialOperation Read(
        string name,
        byte[] data,
        EngineInput input,
        ReadOptions? options,
        SpanRead span,
        Func<byte[], ReadOptions, object?> array,
        Func<ReadOnlyMemory<byte>, ReadOptions, object?> memory,
        Func<Stream, ReadOptions, object?> stream,
        Func<ReadOnlySequence<byte>, ReadOptions, object?> sequence,
        Action<CanonicalText, object?> render,
        bool? engine = false)
    {
        if (EngineStreams.IsStream(input))
        {
            return StreamRead(name, data, input, options, stream, render, engine);
        }

        return new DifferentialOperation(
            name + " (" + input + ")",
            (side, output) =>
            {
                ReadOptions read = side.Read(options);
                byte[] copy = (byte[])data.Clone();

                // Runs the call over the chosen input form; the rendering happens inside so a failure replaces it.
                output.Capture(
                    "failure",
                    () =>
                    {
                        object? result = input switch
                        {
                            EngineInput.Span => span(copy, read),
                            EngineInput.ByteArray => array(copy, read),
                            EngineInput.Memory => memory(copy, read),
                            EngineInput.Sequence => sequence(ChunkedSequence.Of(copy), read),
                            _ => throw new ArgumentOutOfRangeException(nameof(input), input, "Unknown input form."),
                        };
                        render(output, result);
                    });
            },
            engine);
    }

    /// <summary>A read over one of the stream forms, rendering the result and then the stream's final position relative to the data's start.</summary>
    /// <param name="name">The operation's name.</param>
    /// <param name="data">The input bytes; each run reads its own copy.</param>
    /// <param name="input">The stream form.</param>
    /// <param name="options">The case's read options, which each side adjusts.</param>
    /// <param name="call">The call over the stream.</param>
    /// <param name="render">Renders the call's result.</param>
    /// <param name="engine">Whether automatic selection must run the engine (<see cref="DifferentialOperation.Engine"/>).</param>
    /// <returns>The operation.</returns>
    private static DifferentialOperation StreamRead(string name, byte[] data, EngineInput input, ReadOptions? options, Func<Stream, ReadOptions, object?> call, Action<CanonicalText, object?> render, bool? engine = false)
    {
        return new DifferentialOperation(
            name + " (" + input + ")",
            (side, output) =>
            {
                using Stream source = EngineStreams.Open(input, data);
                ReadOptions read = side.Read(options);
                output.Capture("failure", () => render(output, call(source, read)));
                RenderPosition(output, source, EngineStreams.StartOf(input));
            },
            engine);
    }

    /// <summary>A write or update of an expandable stream holding <paramref name="prefill"/>: the final position, then the contents.</summary>
    /// <param name="name">The operation's name.</param>
    /// <param name="prefill">The stream's bytes before the call.</param>
    /// <param name="start">The stream position the call starts at.</param>
    /// <param name="call">The call, given the stream and the side.</param>
    /// <param name="engine">
    ///     Whether automatic selection must run the engine (<see cref="DifferentialOperation.Engine"/>): <c>WriteAsync</c>
    ///     serializes through the engine before it writes the stream.
    /// </param>
    /// <returns>The operation.</returns>
    private static DifferentialOperation StreamWrite(string name, byte[] prefill, long start, Action<Stream, EngineSide> call, bool? engine = false)
    {
        return new DifferentialOperation(
            name + " (Stream)",
            (side, output) =>
            {
                using var stream = new MemoryStream();
                stream.Write(prefill);
                stream.Position = start;
                output.Capture("failure", () => call(stream, side));
                RenderPosition(output, stream);
                output.Bytes("stream", stream.ToArray());
            },
            engine);
    }
}
