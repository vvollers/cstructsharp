namespace CStructSharpWeb.Wasm;

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Exposes CStructSharp read, write, and debug operations to the browser.
///     Each export accepts browser-friendly strings and returns the shared versioned JSON envelope (see
///     <see cref="StartEnvelope"/>); failures are reported in the envelope, never thrown. The parse exports return
///     the envelope as UTF-8 bytes, because a parse result can be too long for the runtime's string marshaling; the
///     others return it as a string. The one exception is <see cref="TakeOutput"/>, which hands over the bytes a
///     successful write produced.
/// </summary>
/// <remarks>
///     The .NET WebAssembly runtime runs managed code on one thread per runtime instance (the page's runtime, and one
///     per source worker), and JavaScript calls the exports synchronously. The static state below - the worker's
///     retained layout and the pending write output - therefore belongs to exactly one caller at a time.
/// </remarks>
[SupportedOSPlatform("browser")]
public partial class CStructExports
{
    private static CStruct? workerLayout;

    // The bytes of the last successful Serialize or UpdateStream, until TakeOutput hands them over. Starting any
    // envelope clears it, so the output always belongs to the operation whose envelope was returned last.
    private static byte[]? pendingOutput;

    /// <summary>Returns the managed library version used by the loaded browser bundle.</summary>
    /// <returns>
    ///     The JSON text of a <c>version</c> envelope whose <c>data</c> is <c>{"version": ...}</c>: the text
    ///     <c>CStructSharp WASM </c> followed by the informational assembly version.
    /// </returns>
    [JSExport]
    public static string GetVersion()
    {
        string version = typeof(CStruct).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? typeof(CStruct).Assembly.GetName().Version?.ToString() ?? "unknown";
        InteropJsonWriter writer = StartEnvelope("version", success: true, root: null);
        writer.WriteRawBytes("{\"version\":"u8);
        writer.WriteString("CStructSharp WASM " + version);
        writer.WriteRawBytes("}"u8);
        return FinishEnvelope(writer);
    }

    /// <summary>
    ///     Hands over the bytes of the last successful <see cref="Serialize"/> or <see cref="UpdateStream"/> and
    ///     clears them - a native Uint8Array on the JavaScript side, not Base64 text. The adapter calls it directly
    ///     after a success envelope, whose <c>data.byteLength</c> gives the expected length.
    /// </summary>
    /// <returns>The pending output; the caller owns the array.</returns>
    /// <exception cref="InvalidOperationException">
    ///     No output is pending: the last envelope was a failure, came from another export, or its output was already
    ///     taken.
    /// </exception>
    [JSExport]
    public static byte[] TakeOutput()
    {
        byte[] output = pendingOutput ??
                        throw new InvalidOperationException("No operation output is pending. Call TakeOutput once, directly after a successful Serialize or UpdateStream envelope.");
        pendingOutput = null;
        return output;
    }

    /// <summary>
    ///     Parses a complete managed copy of the caller's bytes on the calling thread. The JavaScript adapter uses it
    ///     for small inputs: one byte[] marshal is far cheaper than staging the input and round-tripping the
    ///     worker that <see cref="ParseSource"/> is designed for.
    /// </summary>
    /// <param name="definition">The CStruct layout definition text; compiled once and cached.</param>
    /// <param name="binaryData">The bytes to parse: 1 byte through 4 MiB.</param>
    /// <param name="optionsJson">The JSON options object (<see cref="InteropOptionsDto"/>).</param>
    /// <param name="debug">Whether the envelope's <c>debug</c> array lists each decoded value's byte range.</param>
    /// <returns>
    ///     The <c>parse</c> envelope as UTF-8 JSON bytes: the selected value in <c>data</c>, or <c>error</c> on failure
    ///     (a <c>read-budget</c> error when the envelope would exceed <see cref="InteropLimits.MaximumResultLength"/>
    ///     bytes).
    /// </returns>
    [JSExport]
    public static byte[] ParseBytes(string definition, byte[] binaryData, string optionsJson, bool debug)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            byte[] ownedBinaryData = ValidateBinaryData(binaryData);
            using var stream = new MemoryStream(ownedBinaryData, writable: false);
            return ParseStreamResult(definition, stream, options, debug);
        }
        catch (Exception exception)
        {
            return SerializeParseFailure(exception, options);
        }
    }

    /// <summary>Reads a seekable JavaScript source without copying the complete source into WASM memory.</summary>
    /// <param name="definition">The CStruct layout definition text; compiled once and cached.</param>
    /// <param name="source">
    ///     The seekable JavaScript source, read one 64 KiB page at a time through <see cref="JavaScriptSourceStream"/>.
    /// </param>
    /// <param name="optionsJson">The JSON options object (<see cref="InteropOptionsDto"/>).</param>
    /// <param name="debug">Whether the envelope's <c>debug</c> array lists each decoded value's byte range.</param>
    /// <returns>
    ///     The <c>parse</c> envelope as UTF-8 JSON bytes: the selected value in <c>data</c>, or <c>error</c> on failure
    ///     (a <c>read-budget</c> error when the envelope would exceed <see cref="InteropLimits.MaximumResultLength"/>
    ///     bytes).
    /// </returns>
    [JSExport]
    public static byte[] ParseSource(string definition, JSObject source, string optionsJson, bool debug)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            using var stream = new JavaScriptSourceStream(source);
            return ParseStreamResult(definition, stream, options, debug);
        }
        catch (Exception exception)
        {
            return SerializeParseFailure(exception, options);
        }
    }

    /// <summary>Initializes the one retained layout owned by a dedicated JavaScript worker.</summary>
    /// <param name="definition">The CStruct layout definition text; compiled once and cached.</param>
    /// <param name="optionsJson">
    ///     The JSON options object (<see cref="InteropOptionsDto"/>) carrying the compile-time choices.
    /// </param>
    /// <returns>
    ///     The JSON text of a <c>compile</c> envelope: an empty object in <c>data</c> and the resolved root on
    ///     success, or <c>error</c> (with the layout's line and column when known) on failure, which leaves the
    ///     retained layout unchanged.
    /// </returns>
    [JSExport]
    public static string InitializeCompiledLayout(string definition, string optionsJson)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            CStruct layout = CreateCStruct(definition, options);
            string root = ResolveRoot(layout, options);

            // Replace the retained layout only once the new one is complete, so a failure leaves the old one.
            workerLayout = layout;
            InteropJsonWriter writer = StartEnvelope("compile", success: true, root);
            writer.WriteRawBytes("{}"u8);
            return FinishEnvelope(writer);
        }
        catch (Exception exception)
        {
            return SerializeFailure("compile", exception, options);
        }
    }

    /// <summary>Reads with the immutable layout retained by this worker runtime.</summary>
    /// <param name="source">
    ///     The seekable JavaScript source, read one 64 KiB page at a time through <see cref="JavaScriptSourceStream"/>.
    /// </param>
    /// <param name="optionsJson">The JSON options object (<see cref="InteropOptionsDto"/>).</param>
    /// <param name="debug">Whether the envelope's <c>debug</c> array lists each decoded value's byte range.</param>
    /// <returns>
    ///     The <c>parse</c> envelope as UTF-8 JSON bytes; an <c>error</c> envelope when no layout was initialized, the
    ///     read fails, or the envelope would exceed <see cref="InteropLimits.MaximumResultLength"/> bytes.
    /// </returns>
    [JSExport]
    public static byte[] ParseCompiledSource(JSObject source, string optionsJson, bool debug)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            using var stream = new JavaScriptSourceStream(source);
            return ParseStreamResult(RequireWorkerLayout(), stream, options, debug);
        }
        catch (Exception exception)
        {
            return SerializeParseFailure(exception, options);
        }
    }

    /// <summary>Resolves an address with the layout retained by this worker runtime; see <see cref="ResolveAddress"/>.</summary>
    /// <param name="source">
    ///     The seekable JavaScript source, read one 64 KiB page at a time through <see cref="JavaScriptSourceStream"/>.
    /// </param>
    /// <param name="path">The root name or nested field path to locate.</param>
    /// <param name="optionsJson">The JSON options object (<see cref="InteropOptionsDto"/>).</param>
    /// <returns>
    ///     The JSON text of a <c>resolveAddress</c> envelope; an <c>error</c> envelope when no layout was initialized
    ///     or the path does not resolve.
    /// </returns>
    [JSExport]
    public static string ResolveAddressCompiled(JSObject source, string path, string optionsJson)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            using var stream = new JavaScriptSourceStream(source);
            return ResolveAddressResult(RequireWorkerLayout(), stream, path, options);
        }
        catch (Exception exception)
        {
            return SerializeFailure("resolveAddress", exception, options);
        }
    }

    /// <summary>
    ///     Serializes browser JSON with a CStruct definition. The envelope reports the outcome; on success the encoded
    ///     bytes wait for <see cref="TakeOutput"/>, so they cross to JavaScript as a native Uint8Array rather than
    ///     inside the JSON text.
    /// </summary>
    /// <param name="cstructDefinition">The CStruct layout definition text.</param>
    /// <param name="dataJson">The value to encode as JSON text, at most 1 MiB, bigints as decimal text.</param>
    /// <param name="optionsJson">
    ///     The JSON options object (<see cref="InteropOptionsDto"/>); its <c>root</c> selects the encoded type.
    /// </param>
    /// <returns>
    ///     The JSON text of a <c>serialize</c> envelope that echoes the <c>root</c> option: <c>data</c> is
    ///     <c>{"byteLength": n}</c> for the <c>n</c> encoded bytes <see cref="TakeOutput"/> returns, or <c>error</c>
    ///     on failure.
    /// </returns>
    [JSExport]
    public static string Serialize(
        string cstructDefinition,
        string dataJson,
        string optionsJson)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            return SerializeOutputResult("serialize", SerializeCore(CreateCStruct(cstructDefinition, options), dataJson, options), options);
        }
        catch (Exception exception)
        {
            return SerializeFailure("serialize", exception, options);
        }
    }

    /// <summary>
    ///     Updates one public path in a copy of existing bytes. The envelope reports the outcome; on success the
    ///     complete updated payload waits for <see cref="TakeOutput"/>, as for <see cref="Serialize"/>.
    /// </summary>
    /// <param name="cstructDefinition">The CStruct layout definition text.</param>
    /// <param name="binaryData">The existing payload, 1 byte through 4 MiB, updated in a managed copy.</param>
    /// <param name="elementNameOrPath">The path of the value to replace, at most 4096 characters.</param>
    /// <param name="valueJson">The replacement value as JSON text, at most 1 MiB.</param>
    /// <param name="optionsJson">
    ///     The JSON options object (<see cref="InteropOptionsDto"/>), including the update traversal limits.
    /// </param>
    /// <returns>
    ///     The JSON text of an <c>update</c> envelope that echoes the <c>root</c> option: <c>data</c> is
    ///     <c>{"byteLength": n}</c> for the complete updated payload <see cref="TakeOutput"/> returns (the same length
    ///     as <paramref name="binaryData"/>), or <c>error</c> on failure.
    /// </returns>
    [JSExport]
    public static string UpdateStream(
        string cstructDefinition,
        byte[] binaryData,
        string elementNameOrPath,
        string valueJson,
        string optionsJson)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            return SerializeOutputResult("update", UpdateCore(CreateCStruct(cstructDefinition, options), binaryData, elementNameOrPath, valueJson, options), options);
        }
        catch (Exception exception)
        {
            return SerializeFailure("update", exception, options);
        }
    }

    /// <summary>
    ///     Resolves the absolute position of a path in a seekable JavaScript source and returns it in the envelope's
    ///     <c>data</c> as a number (a decimal string beyond Number's exact range).
    /// </summary>
    /// <param name="definition">The CStruct layout definition text; compiled once and cached.</param>
    /// <param name="source">
    ///     The seekable JavaScript source, read one 64 KiB page at a time through <see cref="JavaScriptSourceStream"/>.
    /// </param>
    /// <param name="path">The root name or nested field path to locate, at most 4096 characters.</param>
    /// <param name="optionsJson">The JSON options object (<see cref="InteropOptionsDto"/>).</param>
    /// <returns>The JSON text of a <c>resolveAddress</c> envelope, or <c>error</c> on failure.</returns>
    [JSExport]
    public static string ResolveAddress(string definition, JSObject source, string path, string optionsJson)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            using var stream = new JavaScriptSourceStream(source);
            return ResolveAddressResult(CreateCStruct(definition, options), stream, path, options);
        }
        catch (Exception exception)
        {
            return SerializeFailure("resolveAddress", exception, options);
        }
    }

    /// <summary>Returns the layout <see cref="InitializeCompiledLayout"/> retained in this worker runtime.</summary>
    /// <returns>The retained layout.</returns>
    /// <exception cref="InvalidOperationException">No layout was initialized.</exception>
    private static CStruct RequireWorkerLayout()
    {
        return workerLayout ?? throw new InvalidOperationException("No compiled layout.");
    }

    /// <summary>Serializes the browser's JSON value as the selected root, unwrapping a value still wrapped under the root's name.</summary>
    /// <param name="cstruct">The compiled layout.</param>
    /// <param name="dataJson">The value as JSON.</param>
    /// <param name="options">The browser's options.</param>
    /// <returns>The serialized bytes.</returns>
    private static byte[] SerializeCore(CStruct cstruct, string dataJson, InteropOptionsDto options)
    {
        ValidateJson(dataJson);
        object? data = ParseJsonValue(dataJson);
        string root = ResolveRoot(cstruct, options);

        // A value that came from a parse of a whole struct may still be wrapped under the root's name
        // (`{ header: { ... } }`); a wrapper is an object whose only member is the root and is itself an object.
        if (data is IDictionary<string, object?> wrapper && wrapper.Count == 1 && wrapper.TryGetValue(root, out object? inner) && inner is IDictionary<string, object?> or UnionValue)
        {
            data = inner;
        }

        return cstruct.Serialize(root, data!, options: CreateWriteOptions<WriteOptions>(options));
    }

    /// <summary>Applies one path update to a managed copy of the caller's bytes.</summary>
    /// <param name="cstruct">The compiled layout.</param>
    /// <param name="binaryData">The existing payload; it is not modified.</param>
    /// <param name="elementNameOrPath">The path of the value to replace.</param>
    /// <param name="valueJson">The replacement value as JSON.</param>
    /// <param name="options">The browser's options.</param>
    /// <returns>The complete updated payload.</returns>
    private static byte[] UpdateCore(CStruct cstruct, byte[] binaryData, string elementNameOrPath, string valueJson, InteropOptionsDto options)
    {
        ValidatePath(elementNameOrPath);
        ValidateJson(valueJson);
        byte[] ownedBinaryData = ValidateBinaryData(binaryData);
        object? value = ParseJsonValue(valueJson);
        using var stream = new MemoryStream(ownedBinaryData);
        cstruct.Update(stream, elementNameOrPath, value!, options: CreateUpdateOptions(options));
        return stream.ToArray();
    }

    /// <summary>
    ///     Writes the success envelope of a byte-producing write and stores its bytes for <see cref="TakeOutput"/>.
    /// </summary>
    /// <param name="operation">The operation name: <c>serialize</c> or <c>update</c>.</param>
    /// <param name="output">The produced bytes.</param>
    /// <param name="options">The browser's options; their <c>root</c> is echoed.</param>
    /// <returns>The envelope, whose <c>data</c> is <c>{"byteLength": n}</c>.</returns>
    private static string SerializeOutputResult(string operation, byte[] output, InteropOptionsDto options)
    {
        InteropJsonWriter writer = StartEnvelope(operation, success: true, options.Root);
        writer.WriteRawBytes("{\"byteLength\":"u8);
        writer.WriteSafeInteger(output.Length);
        writer.WriteRawBytes("}"u8);
        string envelope = FinishEnvelope(writer);

        // Set after the envelope is complete: starting an envelope clears the pending output.
        pendingOutput = output;
        return envelope;
    }

    /// <summary>
    ///     Resolves a path's absolute position and writes it as the <c>resolveAddress</c> envelope's <c>data</c>: a
    ///     number, or a decimal string beyond JavaScript's exact integer range.
    /// </summary>
    /// <param name="cstruct">The compiled layout.</param>
    /// <param name="stream">The seekable source.</param>
    /// <param name="path">The path to locate; echoed as the envelope's <c>root</c>.</param>
    /// <param name="options">The browser's options.</param>
    /// <returns>The envelope.</returns>
    private static string ResolveAddressResult(CStruct cstruct, Stream stream, string path, InteropOptionsDto options)
    {
        ValidatePath(path);
        long address = cstruct.ResolveAddress(stream, path, options: CreateReadOptions(options));
        InteropJsonWriter writer = StartEnvelope("resolveAddress", success: true, path);
        writer.WriteSafeInteger(address);
        return FinishEnvelope(writer);
    }

    /// <summary>Compiles (or reuses) a layout, then projects a values-only or debug stream read into the parse envelope.</summary>
    /// <param name="definition">The layout definition text.</param>
    /// <param name="stream">The seekable source.</param>
    /// <param name="options">The browser's options.</param>
    /// <param name="debug">Whether to record each value's byte range.</param>
    /// <returns>The parse envelope as UTF-8 JSON bytes.</returns>
    private static byte[] ParseStreamResult(string definition, Stream stream, InteropOptionsDto options, bool debug)
    {
        return ParseStreamResult(CreateCStruct(definition, options), stream, options, debug);
    }

    /// <summary>Projects a values-only or debug stream read with a compiled layout into the parse envelope.</summary>
    /// <param name="cstruct">The compiled layout.</param>
    /// <param name="stream">The seekable source.</param>
    /// <param name="options">The browser's options.</param>
    /// <param name="debug">Whether to record each value's byte range.</param>
    /// <returns>The parse envelope as UTF-8 JSON bytes.</returns>
    private static byte[] ParseStreamResult(CStruct cstruct, Stream stream, InteropOptionsDto options, bool debug)
    {
        string root = ResolveRoot(cstruct, options);
        ReadOptions readOptions = CreateReadOptions(options);
        IReadOnlyList<DebugData> debugData = [];
        object? selected;
        if (debug)
        {
            (selected, debugData) = cstruct.ReadValueWithDebug(stream, root, options: readOptions);
        }
        else
        {
            selected = cstruct.ReadValue(stream, root, options: readOptions);
        }

        return SerializeParseEnvelope(root, selected, debugData);
    }
}
