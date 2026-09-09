namespace CStructSharpWeb.Wasm;

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using CStructSharp;
using CStructSharp.Structure;

/// <summary>
///     Exposes CStructSharp read, write, and debug operations to the browser.
///     Each export accepts browser-friendly strings and returns the shared versioned JSON envelope.
/// </summary>
[SupportedOSPlatform("browser")]
public partial class CStructExports
{
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
    ///     <paramref name="binaryData"/> crosses the interop boundary as a zero-copy view over the caller's
    ///     Uint8Array (a JS MemoryView), not a Base64-encoded string.
    /// </summary>
    [JSExport]
    public static string ParseWithDebug(
        string cstructDefinition,
        [JSMarshalAs<JSType.MemoryView>] Span<byte> binaryData,
        string optionsJson)
    {
        return ParseWithDebugInternal(cstructDefinition, binaryData, optionsJson);
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
                              ? cstruct.CStructElements.First(element => element.Value is Struct).Key
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
        [JSMarshalAs<JSType.MemoryView>] Span<byte> binaryData,
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
    private static string ParseWithDebugInternal(
        string cstructDefinition,
        Span<byte> binaryData,
        string optionsJson)
    {
        try
        {
            InteropOptionsDto options = ParseOptions(optionsJson);
            byte[] ownedBinaryData = ValidateBinaryData(binaryData);
            CStruct cstruct = CreateCStruct(cstructDefinition, options);
            using var stream = new MemoryStream(ownedBinaryData);

            string root = string.IsNullOrWhiteSpace(options.RootTypeName)
                              ? cstruct.CStructElements.First(element => element.Value is Struct).Key
                              : options.RootTypeName;
            (List<DebugData> debugData, dynamic result)
                = cstruct.ParseStreamWithDebug(stream, root, CreateReadOptions(options));

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
        catch (Exception exception)
        {
            return SerializeInteropResult(CreateFailure("parse", exception));
        }
    }
}
