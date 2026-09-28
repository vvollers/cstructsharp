namespace CStructSharpWeb.Wasm;

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Exposes CStructSharp read, write, and debug operations to the browser.
///     Each export accepts browser-friendly strings and returns the shared versioned JSON envelope.
/// </summary>
[SupportedOSPlatform("browser")]
public partial class CStructExports
{
    private static CStruct? workerLayout;

    /// <summary>Returns the managed library version used by the loaded browser bundle.</summary>
    /// <returns>The text <c>CStructSharp WASM </c> followed by the informational assembly version.</returns>
    [JSExport]
    public static string GetVersion()
    {
        string version = typeof(CStruct).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? typeof(CStruct).Assembly.GetName().Version?.ToString() ?? "unknown";
        return "CStructSharp WASM " + version;
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
    ///     The JSON text of a <c>parse</c> envelope: the selected value in <c>data</c>, or <c>error</c> on failure.
    /// </returns>
    [JSExport]
    public static string ParseBytes(string definition, byte[] binaryData, string optionsJson, bool debug)
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
            return SerializeInteropResult(CreateFailure("parse", exception, options));
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
    ///     The JSON text of a <c>parse</c> envelope: the selected value in <c>data</c>, or <c>error</c> on failure.
    /// </returns>
    [JSExport]
    public static string ParseSource(string definition, JSObject source, string optionsJson, bool debug)
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
            return SerializeInteropResult(CreateFailure("parse", exception, options));
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
            workerLayout = CreateCStruct(definition, options);
            return SerializeInteropResult(CreateSuccess("compile", EmptyObject(), ResolveRoot(workerLayout, options)));
        }
        catch (Exception exception)
        {
            return SerializeInteropResult(CreateFailure("compile", exception, options));
        }
    }

    /// <summary>Reads with the immutable layout retained by this worker runtime.</summary>
    /// <param name="source">
    ///     The seekable JavaScript source, read one 64 KiB page at a time through <see cref="JavaScriptSourceStream"/>.
    /// </param>
    /// <param name="optionsJson">The JSON options object (<see cref="InteropOptionsDto"/>).</param>
    /// <param name="debug">Whether the envelope's <c>debug</c> array lists each decoded value's byte range.</param>
    /// <returns>
    ///     The JSON text of a <c>parse</c> envelope; an <c>error</c> envelope when no layout was initialized or the
    ///     read fails.
    /// </returns>
    [JSExport]
    public static string ParseCompiledSource(JSObject source, string optionsJson, bool debug)
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
            return SerializeInteropResult(CreateFailure("parse", exception, options));
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
            return SerializeInteropResult(CreateFailure("resolveAddress", exception, options));
        }
    }

    /// <summary>
    ///     Serializes browser JSON with a CStruct definition and returns the encoded bytes directly - a native
    ///     Uint8Array on the JS side, not Base64 text. Failure is reported by throwing rather than through the
    ///     JSON envelope other exports use, since there is no envelope object to carry an error field alongside a
    ///     native byte-array success payload; the thrown exception's message is the same JSON-serialized
    ///     <see cref="ErrorDetailsDto"/> shape, ready for the JS wrapper to reconstruct the familiar error object.
    /// </summary>
    /// <param name="cstructDefinition">The CStruct layout definition text.</param>
    /// <param name="dataJson">The value to encode as JSON text, at most 1 MiB, bigints as decimal text.</param>
    /// <param name="optionsJson">
    ///     The JSON options object (<see cref="InteropOptionsDto"/>); its <c>root</c> selects the encoded type.
    /// </param>
    /// <returns>The encoded bytes of the selected root.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Any failure; the message is the JSON-serialized <see cref="ErrorDetailsDto"/>.
    /// </exception>
    [JSExport]
    public static byte[] Serialize(
        string cstructDefinition,
        string dataJson,
        string optionsJson)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            return SerializeCore(CreateCStruct(cstructDefinition, options), dataJson, options);
        }
        catch (Exception exception)
        {
            throw CreateBridgeException(exception, options);
        }
    }

    /// <summary>
    ///     Updates one public path in existing bytes and returns the complete updated payload directly - a native
    ///     Uint8Array on the JS side, not Base64 text. Failure is reported by throwing; see <see cref="Serialize"/>.
    /// </summary>
    /// <param name="cstructDefinition">The CStruct layout definition text.</param>
    /// <param name="binaryData">The existing payload, 1 byte through 4 MiB, updated in a managed copy.</param>
    /// <param name="elementNameOrPath">The path of the value to replace, at most 4096 characters.</param>
    /// <param name="valueJson">The replacement value as JSON text, at most 1 MiB.</param>
    /// <param name="optionsJson">
    ///     The JSON options object (<see cref="InteropOptionsDto"/>), including the update traversal limits.
    /// </param>
    /// <returns>The complete payload after the update, the same length as <paramref name="binaryData"/>.</returns>
    /// <exception cref="InvalidOperationException">
    ///     Any failure; the message is the JSON-serialized <see cref="ErrorDetailsDto"/>.
    /// </exception>
    [JSExport]
    public static byte[] UpdateStream(
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
            return UpdateCore(CreateCStruct(cstructDefinition, options), binaryData, elementNameOrPath, valueJson, options);
        }
        catch (Exception exception)
        {
            throw CreateBridgeException(exception, options);
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
            return SerializeInteropResult(CreateFailure("resolveAddress", exception, options));
        }
    }

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

    private static string ResolveAddressResult(CStruct cstruct, Stream stream, string path, InteropOptionsDto options)
    {
        ValidatePath(path);
        long address = cstruct.ResolveAddress(stream, path, options: CreateReadOptions(options));
        using JsonDocument document = JsonDocument.Parse(address is >= -9_007_199_254_740_991 and <= 9_007_199_254_740_991
                                                              ? address.ToString(CultureInfo.InvariantCulture)
                                                              : "\"" + address.ToString(CultureInfo.InvariantCulture) + "\"");
        return SerializeInteropResult(CreateSuccess("resolveAddress", document.RootElement.Clone(), path));
    }

    /// <summary>Projects either a values-only or debug stream read into the common result envelope.</summary>
    private static string ParseStreamResult(string definition, Stream stream, InteropOptionsDto options, bool debug)
    {
        return ParseStreamResult(CreateCStruct(definition, options), stream, options, debug);
    }

    private static string ParseStreamResult(CStruct cstruct, Stream stream, InteropOptionsDto options, bool debug)
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

        var debugDataDtos = new List<DebugDataDto>(debugData.Count);
        foreach (DebugData item in debugData)
        {
            debugDataDtos.Add(
                new DebugDataDto
                {
                    Start = item.Start,
                    End = item.End,
                    Path = item.Path,
                    Type = item.TypeName ?? "unknown",
                    Value = item.Value is IFormattable formattable
                                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                                : item.Value?.ToString(),
                });
        }

        return SerializeParseEnvelope(root, selected, debugDataDtos);
    }
}
