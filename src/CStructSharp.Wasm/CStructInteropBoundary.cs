namespace CStructSharpWeb.Wasm;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;
using Enum = System.Enum;

/// <summary>Validates untrusted browser inputs and creates the stable transport envelope.</summary>
public partial class CStructExports
{
    private const int InteropContractVersion = 8;
    private const int MaximumBinaryInputLength = 4 * 1024 * 1024;
    private const int MaximumDefinitionLength = 128 * 1024;
    private const int MaximumExpressionNestingDepth = 256;
    private const int MaximumExpressionTokens = 100_000;
    private const int MaximumJsonInputLength = 1024 * 1024;
    private const int MaximumLayoutNestingDepth = 256;
    private const int MaximumNestingDepth = 256;
    private const int DefaultArrayElements = 1_000_000;
    private const int MaximumArrayElements = int.MaxValue;
    private const int MaximumPathLength = 4096;
    private const int MaximumPointerDepth = 64;
    private const long DefaultStringBytes = 16 * 1024 * 1024;
    private const long MaximumStringBytes = int.MaxValue;
    private const long DefaultTotalBytes = 64 * 1024 * 1024;
    private const long MaximumTotalBytes = 9_007_199_254_740_991;

    /// <summary>Deserializes the one browser options object accepted by every operation.</summary>
    private static InteropOptionsDto ParseOptions(string optionsJson)
    {
        ValidateJson(optionsJson);
        return JsonSerializer.Deserialize(optionsJson, CStructJsonContext.Default.InteropOptionsDto) ??
               new InteropOptionsDto();
    }

    /// <summary>Creates a layout using only bounded browser-configurable compilation choices.</summary>
    private static CStruct CreateCStruct(string definition, InteropOptionsDto options)
    {
        ValidateDefinition(definition);
        var compilationOptions = new CStructCompilationOptions
        {
            MaxDefinitionLength = Bounded(
                options.MaxDefinitionLength,
                MaximumDefinitionLength,
                MaximumDefinitionLength,
                nameof(options.MaxDefinitionLength)),
            MaxLayoutNestingDepth = Bounded(
                options.MaxLayoutNestingDepth,
                MaximumLayoutNestingDepth,
                MaximumLayoutNestingDepth,
                nameof(options.MaxLayoutNestingDepth)),
            MaxExpressionNestingDepth = Bounded(
                options.MaxExpressionNestingDepth,
                MaximumExpressionNestingDepth,
                MaximumExpressionNestingDepth,
                nameof(options.MaxExpressionNestingDepth)),
            MaxExpressionTokens = Bounded(
                options.MaxExpressionTokens,
                MaximumExpressionTokens,
                MaximumExpressionTokens,
                nameof(options.MaxExpressionTokens)),
            BitfieldPacking = ParseEnumOption<BitfieldPacking>(options.BitfieldPacking, "bitfieldPacking"),
            BitfieldAllocation = ParseEnumOption<BitfieldAllocation>(options.BitfieldAllocation, "bitfieldAllocation"),
            CLongWidth = options.CLongWidth is null or 64 ? 64 : options.CLongWidth == 32 ? 32 : throw new BrowserInputException($"Option cLongWidth must be 32 or 64; received {options.CLongWidth}."),
        };

        int pointerSize = options.PointerSize ?? 8;
        if (pointerSize is not (1 or 2 or 4 or 8))
        {
            throw new BrowserInputException($"PointerSize must be 1, 2, 4, or 8 bytes; received {pointerSize}.");
        }

        // Every public export used to recompile the definition on each call, which was 70-85 % of a small public
        // parse (E3.2). The bounded process-wide cache keys on the definition text and every option above, so a
        // changed pointer size, byte order, or limit still compiles afresh; a compiled layout is immutable.
        return CStruct.GetOrCompile(
            definition,
            (byte)pointerSize,
            options.Aligned ?? false,
            options.LittleEndian ?? true,
            compilationOptions);
    }

    /// <summary>Creates bounded parse choices from the browser options object.</summary>
    private static ReadOptions CreateReadOptions(InteropOptionsDto options)
    {
        return new ReadOptions
        {
            AddressingMode = ParseAddressingMode(options.AddressingMode),
            DereferencePointers = options.DereferencePointers ?? true,
            MaxPointerDepth = Bounded(
                options.MaxPointerDepth,
                MaximumPointerDepth,
                MaximumPointerDepth,
                nameof(options.MaxPointerDepth)),
            MaxPointerTargetBytes = BoundedNullable(
                options.MaxPointerTargetBytes,
                MaximumTotalBytes,
                nameof(options.MaxPointerTargetBytes)),
            MaxArrayElements = Bounded(
                options.MaxArrayElements,
                DefaultArrayElements,
                MaximumArrayElements,
                nameof(options.MaxArrayElements)),
            MaxStringBytes = Bounded(
                options.MaxStringBytes,
                DefaultStringBytes,
                MaximumStringBytes,
                nameof(options.MaxStringBytes)),
            MaxTotalBytesRead = Bounded(
                options.MaxTotalBytesRead,
                DefaultTotalBytes,
                MaximumTotalBytes,
                nameof(options.MaxTotalBytesRead)),
            MaxNestingDepth = Bounded(
                options.MaxNestingDepth,
                MaximumNestingDepth,
                MaximumNestingDepth,
                nameof(options.MaxNestingDepth)),
            Origin = ParseOrigin(options.Origin),
            TrimFixedText = options.TrimFixedText ?? false,
        };
    }

    /// <summary>Creates bounded serialization choices from the browser options object.</summary>
    private static WriteOptions CreateWriteOptions(InteropOptionsDto options)
    {
        return new WriteOptions
        {
            AddressingMode = ParseAddressingMode(options.AddressingMode),
            BindingMode = ParseBindingMode(options.BindingMode),
            UnknownMembers = ParseEnumOption<UnknownMemberPolicy>(options.UnknownMembers, "unknownMembers"),
            MaxArrayElements = Bounded(
                options.MaxArrayElements,
                DefaultArrayElements,
                MaximumArrayElements,
                nameof(options.MaxArrayElements)),
            MaxStringBytes = Bounded(
                options.MaxStringBytes,
                DefaultStringBytes,
                MaximumStringBytes,
                nameof(options.MaxStringBytes)),
            MaxTotalBytesWritten = Bounded(
                options.MaxTotalBytesWritten,
                DefaultTotalBytes,
                MaximumTotalBytes,
                nameof(options.MaxTotalBytesWritten)),
            MaxNestingDepth = Bounded(
                options.MaxNestingDepth,
                MaximumNestingDepth,
                MaximumNestingDepth,
                nameof(options.MaxNestingDepth)),
            Origin = ParseOrigin(options.Origin),
        };
    }

    /// <summary>Creates bounded update and traversal choices from the browser options object.</summary>
    private static UpdateOptions CreateUpdateOptions(InteropOptionsDto options)
    {
        WriteOptions write = CreateWriteOptions(options);
        return new UpdateOptions
        {
            AddressingMode = write.AddressingMode,
            BindingMode = write.BindingMode,
            UnknownMembers = write.UnknownMembers,
            MaxArrayElements = write.MaxArrayElements,
            MaxStringBytes = write.MaxStringBytes,
            MaxTotalBytesWritten = write.MaxTotalBytesWritten,
            MaxNestingDepth = write.MaxNestingDepth,
            Origin = write.Origin,
            DereferencePointers = options.DereferencePointers ?? true,
            RequireExistingPointerTarget = options.RequireExistingPointerTarget ?? true,
            ClearUnionStorage = options.ClearUnionStorage ?? true,
            MaxTraversalPointerDepth = Bounded(
                options.MaxTraversalPointerDepth,
                MaximumPointerDepth,
                MaximumPointerDepth,
                nameof(options.MaxTraversalPointerDepth)),
            MaxTraversalPointerTargetBytes = BoundedNullable(
                options.MaxTraversalPointerTargetBytes,
                MaximumTotalBytes,
                nameof(options.MaxTraversalPointerTargetBytes)),
            MaxTraversalStringBytes = Bounded(
                options.MaxTraversalStringBytes,
                DefaultStringBytes,
                MaximumStringBytes,
                nameof(options.MaxTraversalStringBytes)),
            MaxTraversalBytesRead = Bounded(
                options.MaxTraversalBytesRead,
                DefaultTotalBytes,
                MaximumTotalBytes,
                nameof(options.MaxTraversalBytesRead)),
            MaxTraversalNestingDepth = Bounded(
                options.MaxTraversalNestingDepth,
                MaximumNestingDepth,
                MaximumNestingDepth,
                nameof(options.MaxTraversalNestingDepth)),
        };
    }

    private static int Bounded(int? value, int fallback, int maximum, string name)
    {
        int result = value ?? fallback;
        return result > 0 && result <= maximum
                   ? result
                   : throw new BrowserInputException($"Option {name} must be between 1 and {maximum}; received {result}.");
    }

    private static long Bounded(long? value, long fallback, long maximum, string name)
    {
        long result = value ?? fallback;
        return result > 0 && result <= maximum
                   ? result
                   : throw new BrowserInputException($"Option {name} must be between 1 and {maximum}; received {result}.");
    }

    private static long? BoundedNullable(long? value, long maximum, string name)
    {
        return value is null ? null : Bounded(value, maximum, maximum, name);
    }

    /// <summary>Reads an addressing-mode name and rejects unknown enum values.</summary>
    private static PointerAddressingMode ParseAddressingMode(string? mode)
    {
        mode ??= nameof(PointerAddressingMode.Absolute);
        if (Enum.TryParse(mode, true, out PointerAddressingMode parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ArgumentException("Unknown pointer addressing mode: " + mode, nameof(mode));
    }

    /// <summary>Reads an option spelled as an enum member name (case-insensitive); an omitted option is the default.</summary>
    private static TEnum ParseEnumOption<TEnum>(string? value, string name)
        where TEnum : struct, Enum
    {
        if (value is null)
        {
            return default;
        }

        if (Enum.TryParse(value, true, out TEnum parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new BrowserInputException($"Option {name} must be one of {string.Join(", ", Enum.GetNames<TEnum>())}; received '{value}'.");
    }

    private static PocoBindingMode ParseBindingMode(string? mode)
    {
        mode ??= nameof(PocoBindingMode.PublicReadable);
        if (Enum.TryParse(mode, true, out PocoBindingMode parsed) && Enum.IsDefined(parsed))
        {
            return parsed;
        }

        throw new ArgumentException("Unknown binding mode: " + mode, nameof(mode));
    }

    private static long ParseOrigin(string? origin)
    {
        return long.Parse(
            origin ?? "0",
            NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    ///     Picks the first declared struct when the caller does not name a root type. Prefers a directly declared
    ///     <see cref="Struct"/>; a <see cref="Typedef"/> wrapping a <see cref="Struct"/> is not itself a usable root
    ///     path when the underlying struct is also reachable by its own tag (confirmed by
    ///     <c>CStructPathException: The selected path does not resolve to a composite object.</c>), so it is only
    ///     used as a fallback for an anonymous <c>typedef struct { ... } Name;</c> alias (LANG-02), which has no
    ///     separate tagged entry to prefer.
    /// </summary>
    private static string ResolveRoot(CStruct cstruct, InteropOptionsDto options)
    {
        return string.IsNullOrWhiteSpace(options.Root) ? ResolveDefaultRootTypeName(cstruct) : options.Root;
    }

    private static string ResolveDefaultRootTypeName(CStruct cstruct)
    {
        foreach (KeyValuePair<string, CStructElement> element in cstruct.CStructElements)
        {
            if (element.Value is Struct)
            {
                return element.Key;
            }
        }

        return cstruct.CStructElements.First(element => element.Value is Typedef { Struct: not null }).Key;
    }

    /// <summary>Checks the browser input limit on the caller-supplied binary data.</summary>
    private static byte[] ValidateBinaryData(byte[] binaryData)
    {
        if (binaryData.Length == 0 || binaryData.Length > MaximumBinaryInputLength)
        {
            throw new BrowserInputException(binaryData.Length == 0
                ? "No binary data was supplied. Load a file or enter bytes before parsing."
                : $"Binary input contains {binaryData.Length} bytes; the browser limit is {MaximumBinaryInputLength} bytes (4 MiB). Load a header slice or use the C# stream API for the full file. Read safety settings do not raise this input limit.");
        }

        return binaryData;
    }

    /// <summary>
    ///     Wraps a caught exception as a release-safe categorized error - the same shape <see cref="CreateFailure"/>
    ///     builds for the JSON-envelope operations - for exports that report failure by throwing instead, so JS
    ///     can catch it, JSON.parse the message, and reconstruct the exact same structured error.
    /// </summary>
    private static Exception CreateBridgeException(Exception exception, InteropOptionsDto? options)
    {
        return new InvalidOperationException(
            JsonSerializer.Serialize(DescribeError(exception, options), CStructJsonContext.Default.ErrorDetailsDto));
    }

    /// <summary>
    ///     The error details a failure produces. By default the library's own message travels verbatim with every
    ///     location fact it carries; <c>redactDiagnostics</c> keeps only the category code and its curated text (for
    ///     pages that must not echo layout text, values, or paths).
    /// </summary>
    private static ErrorDetailsDto DescribeError(Exception exception, InteropOptionsDto? options)
    {
        (string code, string curated) = GetBrowserError(exception);
        bool redact = options?.RedactDiagnostics ?? false;
        CStructException? domainException = exception as CStructException;
        CStructLayoutException? layoutException = exception as CStructLayoutException;
        return new ErrorDetailsDto
        {
            Code = code,
            Message = redact || exception is not (CStructException or BrowserInputException) ? curated : exception.Message,
            Path = redact ? null : domainException?.Path,
            Offset = domainException?.Offset,
            Member = redact ? null : domainException?.Member,
            MemberType = redact ? null : domainException?.MemberType,
            Line = layoutException?.Line,
            Column = layoutException?.Column,
        };
    }

    /// <summary>Rejects empty or overly large layout text before invoking the parser.</summary>
    private static void ValidateDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition) || definition.Length > MaximumDefinitionLength)
        {
            throw new BrowserInputException(string.IsNullOrWhiteSpace(definition)
                ? "The layout definition is empty. Enter at least one struct declaration."
                : $"The layout contains {definition.Length} characters; the browser limit is {MaximumDefinitionLength} characters.");
        }
    }

    /// <summary>Rejects JSON text that exceeds the browser bridge's allocation limit.</summary>
    private static void ValidateJson(string json)
    {
        if (json.Length > MaximumJsonInputLength)
        {
            throw new BrowserInputException($"JSON input contains {json.Length} characters; the browser limit is {MaximumJsonInputLength} characters.");
        }
    }

    /// <summary>Rejects empty or overly large update paths before starting stream work.</summary>
    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumPathLength)
        {
            throw new BrowserInputException(string.IsNullOrWhiteSpace(path)
                ? "The update path is empty. Supply a root and field path."
                : $"The update path contains {path.Length} characters; the browser limit is {MaximumPathLength} characters.");
        }
    }

    /// <summary>Creates a successful result whose Data is an already-parsed JSON value (the compiled-layout handshake).</summary>
    private static InteropResultDto CreateSuccess(string operation, JsonElement data, string? root = null)
    {
        return new InteropResultDto
        {
            ContractVersion = InteropContractVersion,
            Operation = operation,
            Success = true,
            Root = root,
            Data = data,
            Debug = [],
            Error = null,
        };
    }

    /// <summary>An empty JSON object value for envelopes that carry no data.</summary>
    private static JsonElement EmptyObject()
    {
        using JsonDocument document = JsonDocument.Parse("{}");
        return document.RootElement.Clone();
    }

    /// <summary>Creates a release-safe categorized error without echoing raw caller input.</summary>
    private static InteropResultDto CreateFailure(string operation, Exception exception, InteropOptionsDto? options)
    {
        return new InteropResultDto
        {
            ContractVersion = InteropContractVersion,
            Operation = operation,
            Success = false,

            // The root the caller asked for; the default root is unknown until the layout compiles.
            Root = options?.Root,
            Data = null,
            Debug = [],
            Error = DescribeError(exception, options),
        };
    }

    /// <summary>The options of a call, or none when the options text itself could not be read.</summary>
    private static InteropOptionsDto? TryParseOptions(string optionsJson)
    {
        try
        {
            return ParseOptions(optionsJson);
        }
        catch (Exception exception) when (exception is JsonException or BrowserInputException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>Maps failures to stable categories, exposing only controlled diagnostics, never raw exception text.</summary>
    private static (string Code, string Message) GetBrowserError(Exception exception)
    {
        return exception switch
        {
            BrowserInputException inputException => ("invalid-input", inputException.Message),
            CStructException cstructException => GetDomainBrowserError(cstructException),
            JsonException => ("invalid-json", "The JSON input is invalid."),
            FormatException => ("invalid-input", "An input value has an invalid format."),
            ArgumentException => ("invalid-input", "An input argument or option is invalid."),
            _ => ("operation-failed", "The operation failed unexpectedly."),
        };
    }

    /// <summary>Projects the public CLR code model directly into the browser wire vocabulary.</summary>
    private static (string Code, string Message) GetDomainBrowserError(CStructException exception)
    {
        // Known diagnostics preserve useful causes without echoing layout text, values, or stack traces. Library
        // messages carry a trailing "(field ..., offset ...)" clause, so every match is a prefix match.
        string? detail = exception.Message switch
        {
            var message when message.StartsWith("Not enough bytes", StringComparison.Ordinal) => "Unexpected end of binary input. The field needs more bytes, or a terminated string is missing its terminator. Check the field length and the loaded data range.",
            var message when message.StartsWith("String field contains bytes that are invalid for its encoding", StringComparison.Ordinal) => "The string contains invalid bytes for its declared encoding. Check whether the format uses ASCII, UTF-8, UTF-16, or a raw character buffer.",
            var message when message.StartsWith("String field exceeded the configured encoded-byte limit", StringComparison.Ordinal) => "The string exceeds MaxStringBytes. Check its terminator and encoding, or raise that safety limit within the browser maximum.",
            var message when message.StartsWith("Read operation exceeded the configured total read-byte limit", StringComparison.Ordinal) => "Reading the layout exceeds MaxTotalBytesRead. Check array lengths and pointer traversal, or raise that safety limit within the browser maximum.",
            var message when message.StartsWith("Maximum nested struct depth exceeded", StringComparison.Ordinal) => "The structure exceeds MaxNestingDepth. Check nested records or raise that safety limit within the browser maximum.",
            var message when message.StartsWith("Maximum pointer dereference depth exceeded", StringComparison.Ordinal) => "Pointer traversal exceeds MaxPointerDepth. Check pointer chains or disable pointer dereferencing.",
            var message when message.StartsWith("Pointer target exceeds the configured size limit", StringComparison.Ordinal) => "The pointer target exceeds MaxPointerTargetBytes. Check its type and address, or raise that safety limit within the browser maximum.",
            var message when message.StartsWith("Array length ", StringComparison.Ordinal) && message.Contains("exceeds MaxArrayElements", StringComparison.Ordinal) => "The array length exceeds MaxArrayElements. Check the count field and byte order, or raise that safety limit within the browser maximum.",
            var message when message.StartsWith("Pointer target is outside the readable stream range:", StringComparison.Ordinal) => "The pointer target is outside the loaded data. Check pointer width, byte order, addressing mode, and origin. A header preview may not include the target.",
            var message when message.StartsWith("Cyclic pointer target detected at stream address ", StringComparison.Ordinal) => "Pointer traversal encountered a cycle. Check the pointer layout or disable pointer dereferencing to inspect stored addresses.",
            _ => null,
        };
        (string Code, string Message) category = exception.Code switch
        {
            CStructErrorCode.InvalidLayout => ("invalid-layout", "The CStruct layout is invalid."),
            CStructErrorCode.InvalidPath => ("invalid-path", "The requested layout path is invalid."),
            CStructErrorCode.ReadFailed => ("read-failed", "The binary input could not be read."),
            CStructErrorCode.ReadLimitExceeded => ("read-budget", "A binary read safety limit was exceeded."),
            CStructErrorCode.WriteFailed => ("write-failed", "The supplied value could not be written."),
            CStructErrorCode.WriteLimitExceeded => ("write-budget", "A binary write safety limit was exceeded."),
            _ => ("operation-failed", "The operation failed unexpectedly."),
        };
        return (category.Code, detail ?? category.Message);
    }

    /// <summary>Serializes the shared envelope through source-generated JSON metadata.</summary>
    private static string SerializeInteropResult(InteropResultDto result)
    {
        return JsonSerializer.Serialize(result, CStructJsonContext.Default.InteropResultDto);
    }

    /// <summary>A controlled bridge diagnostic containing only fixed text and numeric limits.</summary>
    private sealed class BrowserInputException(string message) : Exception(message);
}
