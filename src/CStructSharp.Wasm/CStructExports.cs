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
    [JSExport]
    public static string GetVersion()
    {
        string version = typeof(CStruct).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? typeof(CStruct).Assembly.GetName().Version?.ToString() ?? "unknown";
        return "CStructSharp WASM " + version;
    }

    /// <summary>
    ///     Parses binary data with bounded layout and read options and returns values plus byte mappings.
    ///     <paramref name="binaryData"/> crosses the interop boundary as a native byte array - the caller's
    ///     Uint8Array is copied directly into it, not encoded as Base64 text.
    /// </summary>
    [JSExport]
    public static string ParseWithDebug(string cstructDefinition, byte[] binaryData, string optionsJson)
    {
        return ParseBytes(cstructDefinition, binaryData, optionsJson, true);
    }

    /// <summary>
    ///     Parses a complete managed copy of the caller's bytes on the calling thread. The JavaScript adapter uses it
    ///     for small inputs (E3.6): one byte[] marshal is far cheaper than staging the input and round-tripping the
    ///     worker that <see cref="ParseSource"/> is designed for.
    /// </summary>
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

    /// <summary>Serializes with the layout retained by this worker runtime; see <see cref="Serialize"/>.</summary>
    [JSExport]
    public static byte[] SerializeCompiled(string dataJson, string optionsJson)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            return SerializeCore(RequireWorkerLayout(), dataJson, options);
        }
        catch (Exception exception)
        {
            throw CreateBridgeException(exception, options);
        }
    }

    /// <summary>Updates with the layout retained by this worker runtime; see <see cref="UpdateStream"/>.</summary>
    [JSExport]
    public static byte[] UpdateCompiled(byte[] binaryData, string elementNameOrPath, string valueJson, string optionsJson)
    {
        InteropOptionsDto? options = null;
        try
        {
            options = ParseOptions(optionsJson);
            return UpdateCore(RequireWorkerLayout(), binaryData, elementNameOrPath, valueJson, options);
        }
        catch (Exception exception)
        {
            throw CreateBridgeException(exception, options);
        }
    }

    /// <summary>Resolves an address with the layout retained by this worker runtime; see <see cref="ResolveAddress"/>.</summary>
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

        return cstruct.Serialize(root, data!, options: CreateWriteOptions(options));
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
