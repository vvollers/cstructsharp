namespace CStructSharpWeb.Wasm;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CStructSharp.Diagnostics;

/// <summary>
///     Writes the versioned result envelope every browser export returns, through the one
///     <see cref="InteropJsonWriter"/>. An envelope is one JSON object with seven members, always in this order:
///     <c>contractVersion</c> (<see cref="InteropContractVersion"/>), <c>operation</c> (the export's operation name),
///     <c>success</c>, <c>root</c> (the selected or echoed root, or null), <c>data</c> (the operation's result, null on
///     failure), <c>debug</c> (the byte ranges of a debug parse, otherwise empty), and <c>error</c> (null on success,
///     the <see cref="ErrorDetailsDto"/> fields on failure). The parse exports return the envelope as UTF-8 bytes,
///     every other export as a string; either way it is at most <see cref="InteropLimits.MaximumResultLength"/> bytes.
/// </summary>
public partial class CStructExports
{
    private static readonly byte[] EnvelopeHead = Encoding.UTF8.GetBytes("{\"contractVersion\":" + InteropContractVersion.ToString(CultureInfo.InvariantCulture) + ",\"operation\":");
    private static readonly byte[] EnvelopeSucceeded = ",\"success\":true,\"root\":"u8.ToArray();
    private static readonly byte[] EnvelopeFailed = ",\"success\":false,\"root\":"u8.ToArray();
    private static readonly byte[] EnvelopeData = ",\"data\":"u8.ToArray();
    private static readonly byte[] EnvelopeDebug = ",\"debug\":"u8.ToArray();
    private static readonly byte[] EnvelopeNoError = ",\"error\":null}"u8.ToArray();
    private static readonly byte[] EnvelopeNoDebugNoError = ",\"debug\":[],\"error\":null}"u8.ToArray();
    private static readonly byte[] EnvelopeNoDebugError = ",\"debug\":[],\"error\":"u8.ToArray();

    // One reusable output buffer per thread. The browser runtime runs every export on its one thread, so exports
    // never share it concurrently; each envelope starts by resetting it.
    [ThreadStatic]
    private static InteropJsonWriter? projectionWriter;

    /// <summary>
    ///     Starts an envelope in the thread's reset writer: writes <c>contractVersion</c>, <c>operation</c>,
    ///     <c>success</c>, and <c>root</c>, then the <c>data</c> key. The caller writes the data value next and ends the
    ///     envelope with <see cref="FinishEnvelope"/> (or, for a debug parse, the debug ranges and the error key).
    /// </summary>
    /// <param name="operation">The operation name the envelope reports.</param>
    /// <param name="success">Whether the operation succeeded.</param>
    /// <param name="root">The root or path the operation selected or echoes; null when it has none.</param>
    /// <returns>The writer, positioned where the data value belongs.</returns>
    /// <remarks>Starting an envelope also discards any write output still waiting for <see cref="TakeOutput"/>.</remarks>
    private static InteropJsonWriter StartEnvelope(string operation, bool success, string? root)
    {
        // A new envelope supersedes the previous operation, so its unclaimed write output is dropped here.
        pendingOutput = null;
        InteropJsonWriter writer = projectionWriter ??= new InteropJsonWriter(16 * 1024);
        writer.Reset();
        writer.WriteRawBytes(EnvelopeHead);
        writer.WriteString(operation);
        writer.WriteRawBytes(success ? EnvelopeSucceeded : EnvelopeFailed);
        writer.WriteStringOrNull(root);
        writer.WriteRawBytes(EnvelopeData);
        return writer;
    }

    /// <summary>Ends a successful envelope that carries no debug ranges and returns its JSON text.</summary>
    /// <param name="writer">The writer returned by <see cref="StartEnvelope"/>, after the data value.</param>
    /// <returns>The complete envelope.</returns>
    private static string FinishEnvelope(InteropJsonWriter writer)
    {
        writer.WriteRawBytes(EnvelopeNoDebugNoError);
        return FinishProjection(writer);
    }

    /// <summary>
    ///     Writes the whole successful parse envelope in one pass and returns it as UTF-8 bytes: the parsed value is
    ///     projected straight into the envelope as a JSON value - no intermediate data string, no escaping pass, one
    ///     <c>JSON.parse</c> on the JavaScript side.
    /// </summary>
    /// <param name="root">The resolved root the parse selected.</param>
    /// <param name="result">The parsed value.</param>
    /// <param name="debugData">The byte ranges a debug parse recorded; empty for a values-only parse.</param>
    /// <returns>The complete <c>parse</c> envelope as UTF-8 JSON bytes; the caller owns the array.</returns>
    /// <exception cref="CStructReadLimitException">
    ///     The envelope would be longer than <see cref="InteropLimits.MaximumResultLength"/> bytes.
    /// </exception>
    private static byte[] SerializeParseEnvelope(string root, object? result, IReadOnlyList<DebugData> debugData)
    {
        InteropJsonWriter writer = StartEnvelope("parse", success: true, root);
        writer.WriteValue(result);
        if (debugData.Count == 0)
        {
            writer.WriteRawBytes(EnvelopeNoDebugNoError);
        }
        else
        {
            writer.WriteRawBytes(EnvelopeDebug);
            WriteDebugData(writer, debugData);
            writer.WriteRawBytes(EnvelopeNoError);
        }

        return FinishUtf8Projection(writer);
    }

    /// <summary>Writes the failure envelope of an operation, with <c>data</c> null and the categorized error.</summary>
    /// <param name="operation">The operation name the envelope reports.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="options">
    ///     The parsed options, or null when they could not be read; their <c>root</c> is echoed and their
    ///     <c>redactDiagnostics</c> decides how much of the failure the error reveals.
    /// </param>
    /// <returns>The complete envelope.</returns>
    private static string SerializeFailure(string operation, Exception exception, InteropOptionsDto? options)
    {
        return FinishProjection(WriteFailureEnvelope(operation, exception, options));
    }

    /// <summary>
    ///     Writes the failure envelope of a parse export, as <see cref="SerializeFailure"/> does, and returns it as
    ///     UTF-8 bytes like the parse exports' successful envelopes.
    /// </summary>
    /// <param name="exception">The failure.</param>
    /// <param name="options">The parsed options, or null when they could not be read.</param>
    /// <returns>The complete <c>parse</c> envelope as UTF-8 JSON bytes; the caller owns the array.</returns>
    private static byte[] SerializeParseFailure(Exception exception, InteropOptionsDto? options)
    {
        return FinishUtf8Projection(WriteFailureEnvelope("parse", exception, options));
    }

    /// <summary>Writes a complete failure envelope into the thread's reset writer.</summary>
    /// <param name="operation">The operation name the envelope reports.</param>
    /// <param name="exception">The failure.</param>
    /// <param name="options">The parsed options, or null when they could not be read.</param>
    /// <returns>The writer holding the envelope.</returns>
    private static InteropJsonWriter WriteFailureEnvelope(string operation, Exception exception, InteropOptionsDto? options)
    {
        ErrorDetailsDto error = DescribeError(exception, options);

        // The root the caller asked for; the default root is unknown until the layout compiles.
        InteropJsonWriter writer = StartEnvelope(operation, success: false, options?.Root);
        writer.WriteNull();
        writer.WriteRawBytes(EnvelopeNoDebugError);
        WriteError(writer, error);
        writer.WriteRawBytes("}"u8);
        return writer;
    }

    /// <summary>
    ///     Writes the debug ranges as a JSON array of <c>{start, end, path, type, value}</c> objects; <c>type</c> is
    ///     <c>unknown</c> when the reader recorded none, and <c>value</c> is the decoded value as invariant text
    ///     (decimal digits keep exact 64-bit integers) or null.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="debugData">The ranges, with offsets relative to the operation origin.</param>
    private static void WriteDebugData(InteropJsonWriter writer, IReadOnlyList<DebugData> debugData)
    {
        writer.WriteRawBytes("["u8);
        for (int index = 0; index < debugData.Count; index++)
        {
            DebugData item = debugData[index];
            writer.WriteRawBytes(index == 0 ? "{\"start\":"u8 : ",{\"start\":"u8);
            writer.WriteSafeInteger(item.Start);
            writer.WriteRawBytes(",\"end\":"u8);
            writer.WriteSafeInteger(item.End);
            writer.WriteRawBytes(",\"path\":"u8);
            writer.WriteString(item.Path);
            writer.WriteRawBytes(",\"type\":"u8);
            writer.WriteString(item.TypeName ?? "unknown");
            writer.WriteRawBytes(",\"value\":"u8);
            writer.WriteStringOrNull(item.Value is IFormattable formattable
                                         ? formattable.ToString(null, CultureInfo.InvariantCulture)
                                         : item.Value?.ToString());
            writer.WriteRawBytes("}"u8);
        }

        writer.WriteRawBytes("]"u8);
    }

    /// <summary>Writes an error's eight fields as a JSON object; an unknown fact is written as null.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="error">The error details.</param>
    private static void WriteError(InteropJsonWriter writer, ErrorDetailsDto error)
    {
        writer.WriteRawBytes("{\"code\":"u8);
        writer.WriteString(error.Code);
        writer.WriteRawBytes(",\"message\":"u8);
        writer.WriteString(error.Message);
        writer.WriteRawBytes(",\"path\":"u8);
        writer.WriteStringOrNull(error.Path);
        writer.WriteRawBytes(",\"offset\":"u8);
        writer.WriteIntegerOrNull(error.Offset);
        writer.WriteRawBytes(",\"member\":"u8);
        writer.WriteStringOrNull(error.Member);
        writer.WriteRawBytes(",\"memberType\":"u8);
        writer.WriteStringOrNull(error.MemberType);
        writer.WriteRawBytes(",\"line\":"u8);
        writer.WriteIntegerOrNull(error.Line);
        writer.WriteRawBytes(",\"column\":"u8);
        writer.WriteIntegerOrNull(error.Column);
        writer.WriteRawBytes("}"u8);
    }

    /// <summary>Returns the written JSON text and releases an unusually large per-thread buffer.</summary>
    /// <param name="writer">The thread's writer.</param>
    /// <returns>The text written since the last reset.</returns>
    /// <exception cref="CStructReadLimitException">
    ///     The text is longer than <see cref="InteropLimits.MaximumResultLength"/> bytes.
    /// </exception>
    private static string FinishProjection(InteropJsonWriter writer)
    {
        writer.EnsureWithinLimit();
        string json = writer.ToUtf8String();
        ReleaseLargeWriter(writer);
        return json;
    }

    /// <summary>
    ///     Returns a copy of the written JSON as UTF-8 bytes and releases an unusually large per-thread buffer. The
    ///     parse exports return their envelopes this way: JavaScript decodes the bytes with a UTF-8
    ///     <c>TextDecoder</c>, which builds strings up to the engine's string limit, while a returned .NET string
    ///     is decoded as UTF-16 and fails far below it (at 2^27 characters in Node.js).
    /// </summary>
    /// <remarks>
    ///     A large envelope sits in the writer's fixed-size segments, so this copy is the one moment it exists twice:
    ///     the WebAssembly memory peaks at about twice the envelope, and keeps that size for the runtime's life.
    /// </remarks>
    /// <param name="writer">The thread's writer.</param>
    /// <returns>The bytes written since the last reset; the caller owns the array.</returns>
    /// <exception cref="CStructReadLimitException">
    ///     The output is longer than <see cref="InteropLimits.MaximumResultLength"/> bytes.
    /// </exception>
    private static byte[] FinishUtf8Projection(InteropJsonWriter writer)
    {
        writer.EnsureWithinLimit();
        byte[] json = writer.ToArray();
        ReleaseLargeWriter(writer);
        return json;
    }

    /// <summary>
    ///     Drops the thread's writer after a result that outgrew one writer segment, so its segments can be collected.
    /// </summary>
    /// <param name="writer">The thread's writer, whose output has been copied out.</param>
    private static void ReleaseLargeWriter(InteropJsonWriter writer)
    {
        if (writer.Capacity > InteropJsonWriter.DefaultSegmentLength)
        {
            // Do not pin many megabytes to the thread after one unusually large result.
            projectionWriter = null;
        }
    }
}
