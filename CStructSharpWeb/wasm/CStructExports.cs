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

    /// <summary>Parses binary data with bounded layout and read options and returns values plus byte mappings.</summary>
    [JSExport]
    public static string ParseWithDebug(
        string cstructDefinition,
        string binaryDataBase64,
        string optionsJson)
    {
        return ParseWithDebugInternal(cstructDefinition, binaryDataBase64, optionsJson);
    }

    /// <summary>Serializes browser JSON with a CStruct definition and returns Base64 in the common envelope.</summary>
    [JSExport]
    public static string SerializeToBase64(
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
            byte[] bytes = cstruct.Serialize(root, data!, options: CreateWriteOptions(options));
            return SerializeInteropResult(CreateSuccess("serialize", Convert.ToBase64String(bytes)));
        }
        catch (Exception exception)
        {
            return SerializeInteropResult(CreateFailure("serialize", exception));
        }
    }

    /// <summary>Updates one public path in existing bytes and returns the complete updated Base64 payload.</summary>
    [JSExport]
    public static string UpdateStreamToBase64(
        string cstructDefinition,
        string binaryDataBase64,
        string elementNameOrPath,
        string valueJson,
        string optionsJson)
    {
        try
        {
            ValidatePath(elementNameOrPath);
            ValidateJson(valueJson);
            InteropOptionsDto options = ParseOptions(optionsJson);
            byte[] binaryData = DecodeBinaryData(binaryDataBase64);
            object? value = ParseJsonValue(valueJson);
            CStruct cstruct = CreateCStruct(cstructDefinition, options);
            using var stream = new MemoryStream(binaryData);

            cstruct.UpdateStream(stream, elementNameOrPath, value!, options: CreateUpdateOptions(options));
            return SerializeInteropResult(CreateSuccess("update", Convert.ToBase64String(stream.ToArray())));
        }
        catch (Exception exception)
        {
            return SerializeInteropResult(CreateFailure("update", exception));
        }
    }

    /// <summary>Performs the shared parse operation and projects internal debug records into transport DTOs.</summary>
    private static string ParseWithDebugInternal(
        string cstructDefinition,
        string binaryDataBase64,
        string optionsJson)
    {
        try
        {
            InteropOptionsDto options = ParseOptions(optionsJson);
            byte[] binaryData = DecodeBinaryData(binaryDataBase64);
            CStruct cstruct = CreateCStruct(cstructDefinition, options);
            using var stream = new MemoryStream(binaryData);

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
