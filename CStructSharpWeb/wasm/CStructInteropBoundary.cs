namespace CStructSharpWeb.Wasm;

using System;
using System.Globalization;
using System.Text.Json;
using CStructSharp;
using Enum = System.Enum;

/// <summary>Validates untrusted browser inputs and creates the stable transport envelope.</summary>
public partial class CStructExports
{
    private const int InteropContractVersion = 4;
    private const int MaximumBase64InputLength = 4 * 1024 * 1024;
    private const int MaximumDefinitionLength = 128 * 1024;
    private const int MaximumExpressionNestingDepth = 256;
    private const int MaximumExpressionTokens = 100_000;
    private const int MaximumJsonInputLength = 1024 * 1024;
    private const int MaximumLayoutNestingDepth = 256;
    private const int MaximumNestingDepth = 256;
    private const int MaximumArrayElements = 1_000_000;
    private const int MaximumPathLength = 4096;
    private const int MaximumPointerDepth = 64;
    private const long MaximumStringBytes = 16 * 1024 * 1024;
    private const long MaximumTotalBytes = 64 * 1024 * 1024;

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
        };

        int pointerSize = options.PointerSize ?? 8;
        if (pointerSize is not (1 or 2 or 4 or 8))
        {
            throw new ArgumentOutOfRangeException(nameof(options.PointerSize), "Pointer size is invalid.");
        }

        return new CStruct(
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
                MaximumArrayElements,
                MaximumArrayElements,
                nameof(options.MaxArrayElements)),
            MaxStringBytes = Bounded(
                options.MaxStringBytes,
                MaximumStringBytes,
                MaximumStringBytes,
                nameof(options.MaxStringBytes)),
            MaxTotalBytesRead = Bounded(
                options.MaxTotalBytesRead,
                MaximumTotalBytes,
                MaximumTotalBytes,
                nameof(options.MaxTotalBytesRead)),
            MaxNestingDepth = Bounded(
                options.MaxNestingDepth,
                MaximumNestingDepth,
                MaximumNestingDepth,
                nameof(options.MaxNestingDepth)),
            Origin = ParseOrigin(options.Origin),
        };
    }

    /// <summary>Creates bounded serialization choices from the browser options object.</summary>
    private static WriteOptions CreateWriteOptions(InteropOptionsDto options)
    {
        return new WriteOptions
        {
            AddressingMode = ParseAddressingMode(options.AddressingMode),
            BindingMode = ParseBindingMode(options.BindingMode),
            MaxArrayElements = Bounded(
                options.MaxArrayElements,
                MaximumArrayElements,
                MaximumArrayElements,
                nameof(options.MaxArrayElements)),
            MaxStringBytes = Bounded(
                options.MaxStringBytes,
                MaximumStringBytes,
                MaximumStringBytes,
                nameof(options.MaxStringBytes)),
            MaxTotalBytesWritten = Bounded(
                options.MaxTotalBytesWritten,
                MaximumTotalBytes,
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
            MaxArrayElements = write.MaxArrayElements,
            MaxStringBytes = write.MaxStringBytes,
            MaxTotalBytesWritten = write.MaxTotalBytesWritten,
            MaxNestingDepth = write.MaxNestingDepth,
            Origin = write.Origin,
            AllowPointerDereference = options.AllowPointerDereference ?? true,
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
                MaximumStringBytes,
                MaximumStringBytes,
                nameof(options.MaxTraversalStringBytes)),
            MaxTraversalBytesRead = Bounded(
                options.MaxTraversalBytesRead,
                MaximumTotalBytes,
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
                   : throw new ArgumentOutOfRangeException(name, "Option is outside the browser safety limit.");
    }

    private static long Bounded(long? value, long fallback, long maximum, string name)
    {
        long result = value ?? fallback;
        return result > 0 && result <= maximum
                   ? result
                   : throw new ArgumentOutOfRangeException(name, "Option is outside the browser safety limit.");
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

    /// <summary>Checks the browser input limit and decodes Base64 text into bytes.</summary>
    private static byte[] DecodeBinaryData(string binaryDataBase64)
    {
        if (string.IsNullOrWhiteSpace(binaryDataBase64) ||
            binaryDataBase64.Length > MaximumBase64InputLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(binaryDataBase64),
                "Binary input exceeds the supported limit.");
        }

        return Convert.FromBase64String(binaryDataBase64);
    }

    /// <summary>Rejects empty or overly large layout text before invoking the parser.</summary>
    private static void ValidateDefinition(string definition)
    {
        if (string.IsNullOrWhiteSpace(definition) || definition.Length > MaximumDefinitionLength)
        {
            throw new ArgumentOutOfRangeException(
                nameof(definition),
                "Definition input exceeds the supported limit.");
        }
    }

    /// <summary>Rejects JSON text that exceeds the browser bridge's allocation limit.</summary>
    private static void ValidateJson(string json)
    {
        if (json.Length > MaximumJsonInputLength)
        {
            throw new ArgumentOutOfRangeException(nameof(json), "JSON input exceeds the supported limit.");
        }
    }

    /// <summary>Rejects empty or overly large update paths before starting stream work.</summary>
    private static void ValidatePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Length > MaximumPathLength)
        {
            throw new ArgumentOutOfRangeException(nameof(path), "Path input exceeds the supported limit.");
        }
    }

    /// <summary>Creates the common successful result used by all browser operations.</summary>
    private static InteropResultDto CreateSuccess(string operation, string data)
    {
        return new InteropResultDto
        {
            ContractVersion = InteropContractVersion,
            Operation = operation,
            Success = true,
            Data = data,
            DebugData = [],
            Error = null,
        };
    }

    /// <summary>Creates a release-safe categorized error without echoing raw caller input.</summary>
    private static InteropResultDto CreateFailure(string operation, Exception exception)
    {
        (string code, string message) = GetBrowserError(exception);
        CStructException? domainException = exception as CStructException;
        return new InteropResultDto
        {
            ContractVersion = InteropContractVersion,
            Operation = operation,
            Success = false,
            Data = null,
            DebugData = [],
            Error = new ErrorDetailsDto
            {
                Code = code,
                Message = message,
                Offset = domainException?.Offset,
                Path = domainException?.Path,
            },
        };
    }

    /// <summary>Maps implementation exceptions to stable browser categories and input-independent messages.</summary>
    private static (string Code, string Message) GetBrowserError(Exception exception)
    {
        return exception switch
        {
            CStructException cstructException => GetDomainBrowserError(cstructException.Code),
            JsonException => ("invalid-json", "The JSON input is invalid."),
            FormatException => ("invalid-input", "An input value has an invalid format."),
            ArgumentException => ("invalid-input", "An input argument or option is invalid."),
            _ => ("operation-failed", "The operation failed unexpectedly."),
        };
    }

    /// <summary>Projects the public CLR code model directly into the browser wire vocabulary.</summary>
    private static (string Code, string Message) GetDomainBrowserError(CStructErrorCode code)
    {
        return code switch
        {
            CStructErrorCode.InvalidLayout => ("invalid-layout", "The CStruct layout is invalid."),
            CStructErrorCode.InvalidPath => ("invalid-path", "The requested layout path is invalid."),
            CStructErrorCode.ReadFailed => ("read-failed", "The binary input could not be read."),
            CStructErrorCode.ReadLimitExceeded => ("read-budget", "A binary read safety limit was exceeded."),
            CStructErrorCode.WriteFailed => ("write-failed", "The supplied value could not be written."),
            CStructErrorCode.WriteLimitExceeded => ("write-budget", "A binary write safety limit was exceeded."),
            _ => ("operation-failed", "The operation failed unexpectedly."),
        };
    }

    /// <summary>Serializes the shared envelope through source-generated JSON metadata.</summary>
    private static string SerializeInteropResult(InteropResultDto result)
    {
        return JsonSerializer.Serialize(result, CStructJsonContext.Default.InteropResultDto);
    }
}
