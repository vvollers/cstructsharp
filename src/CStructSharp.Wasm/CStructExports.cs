namespace CStructSharpWeb.Wasm;

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using CStructSharp;

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
        return ParseWithDebugInternal(cstructDefinition, binaryData, optionsJson);
    }

    /// <summary>Reads a seekable JavaScript source without copying the complete source into WASM memory.</summary>
    [JSExport]
    public static string ParseSource(string definition, JSObject source, string optionsJson, bool debug)
    {
        try
        {
            InteropOptionsDto options = ParseOptions(optionsJson);
            using var stream = new JavaScriptSourceStream(source);
            return ParseStreamResult(definition, stream, options, debug);
        }
        catch (Exception exception)
        {
            return SerializeInteropResult(CreateFailure("parse", exception));
        }
    }

    /// <summary>Initializes the one retained layout owned by a dedicated JavaScript worker.</summary>
    [JSExport]
    public static string InitializeCompiledLayout(string definition, string optionsJson)
    {
        try
        {
            workerLayout = CreateCStruct(definition, ParseOptions(optionsJson));
            return SerializeInteropResult(CreateSuccess("parse", "{}"));
        }
        catch (Exception exception)
        {
            return SerializeInteropResult(CreateFailure("parse", exception));
        }
    }

    /// <summary>Reads with the immutable layout retained by this worker runtime.</summary>
    [JSExport]
    public static string ParseCompiledSource(JSObject source, string optionsJson, bool debug)
    {
        try
        {
            InteropOptionsDto options = ParseOptions(optionsJson);
            using var stream = new JavaScriptSourceStream(source);
            return ParseStreamResult(workerLayout ?? throw new InvalidOperationException("No compiled layout."), stream, options, debug);
        }
        catch (Exception exception)
        {
            return SerializeInteropResult(CreateFailure("parse", exception));
        }
    }

    /// <summary>
    ///     Serializes browser JSON with a CStruct definition and returns the encoded bytes directly - a native
    ///     Uint8Array on the JS side, not Base64 text. Failure is reported by throwing rather than through the
    ///     JSON envelope other exports use, since there is no envelope object to carry an Error field alongside a
    ///     native byte-array success payload; the thrown exception's message is the same JSON-serialized
    ///     <see cref="ErrorDetailsDto"/> shape, ready for the JS wrapper to reconstruct the familiar error object.
    /// </summary>
    [JSExport]
    public static byte[] Serialize(
        string cstructDefinition,
        string dataJson,
        string optionsJson)
    {
        try
        {
            ValidateJson(dataJson);
            InteropOptionsDto options = ParseOptions(optionsJson);
            object? data = ParseJsonValue(dataJson);
            CStruct cstruct = CreateCStruct(cstructDefinition, options);

            string root = string.IsNullOrWhiteSpace(options.RootTypeName)
                              ? ResolveDefaultRootTypeName(cstruct)
                              : options.RootTypeName;
            return cstruct.Serialize(root, data!, options: CreateWriteOptions(options));
        }
        catch (Exception exception)
        {
            throw CreateBridgeException(exception);
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
        try
        {
            ValidatePath(elementNameOrPath);
            ValidateJson(valueJson);
            InteropOptionsDto options = ParseOptions(optionsJson);
            byte[] ownedBinaryData = ValidateBinaryData(binaryData);
            object? value = ParseJsonValue(valueJson);
            CStruct cstruct = CreateCStruct(cstructDefinition, options);
            using var stream = new MemoryStream(ownedBinaryData);

            cstruct.UpdateStream(stream, elementNameOrPath, value!, options: CreateUpdateOptions(options));
            return stream.ToArray();
        }
        catch (Exception exception)
        {
            throw CreateBridgeException(exception);
        }
    }

    /// <summary>Performs the shared parse operation and projects internal debug records into transport DTOs.</summary>
    private static string ParseWithDebugInternal(string cstructDefinition, byte[] binaryData, string optionsJson)
    {
        try
        {
            InteropOptionsDto options = ParseOptions(optionsJson);
            byte[] ownedBinaryData = ValidateBinaryData(binaryData);
            using var stream = new MemoryStream(ownedBinaryData);
            return ParseStreamResult(cstructDefinition, stream, options, true);
        }
        catch (Exception exception)
        {
            return SerializeInteropResult(CreateFailure("parse", exception));
        }
    }

    /// <summary>Projects either a values-only or debug stream read into the common result envelope.</summary>
    private static string ParseStreamResult(string definition, Stream stream, InteropOptionsDto options, bool debug)
    {
        return ParseStreamResult(CreateCStruct(definition, options), stream, options, debug);
    }

    private static string ParseStreamResult(CStruct cstruct, Stream stream, InteropOptionsDto options, bool debug)
    {
        string root = string.IsNullOrWhiteSpace(options.RootTypeName)
                          ? ResolveDefaultRootTypeName(cstruct)
                          : options.RootTypeName;
        List<DebugData> debugData = [];
        dynamic result;
        if (debug)
        {
            (debugData, result) = cstruct.ParseStreamWithDebug(stream, root, CreateReadOptions(options));
        }
        else
        {
            result = new Dictionary<string, object?>
            {
                [root] = cstruct.ParseStream(stream, root, options: CreateReadOptions(options)),
            };
        }

        var debugDataDtos = new List<DebugDataDto>(debugData.Count);
        foreach (DebugData item in debugData)
        {
            debugDataDtos.Add(
                new DebugDataDto
                {
                    CurPos = item.CurPos,
                    EndPos = item.EndPos,
                    DebugStackString = item.DebugStackString,
                    Type = item.TypeName ?? "unknown",
                    Value = item.Value is IFormattable formattable
                                ? formattable.ToString(null, CultureInfo.InvariantCulture)
                                : item.Value?.ToString(),
                    Buffer = item.Buffer is null ? null : string.Join(",", item.Buffer),
                });
        }

        InteropResultDto response = CreateSuccess("parse", SerializeParsedValue(result));
        response.DebugData = debugDataDtos;
        return SerializeInteropResult(response);
    }
}
