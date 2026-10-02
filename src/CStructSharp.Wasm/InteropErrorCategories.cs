namespace CStructSharpWeb.Wasm;

using System;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     The browser bridge's failure categories: maps every exception an export catches to the stable error
///     <c>code</c> of the result envelope and its curated message. The mapping lives apart from the exports, which
///     need the WebAssembly runtime, so the managed tests compile it and check every category.
/// </summary>
internal static class InteropErrorCategories
{
    /// <summary>Maps failures to stable categories, exposing only controlled diagnostics, never raw exception text.</summary>
    /// <param name="exception">The failure.</param>
    /// <returns>The category code and its message.</returns>
    /// <remarks>
    ///     Every check of caller input throws <see cref="BrowserInputException"/> (options, sizes, paths),
    ///     <see cref="JsonException"/> (malformed JSON), or a <see cref="CStructException"/> (the library's own layout,
    ///     path, read, and write checks), so bad input always maps to a specific category. Running out of memory
    ///     (<see cref="OutOfMemoryException"/>, which includes <see cref="InsufficientMemoryException"/>) is
    ///     <c>resource-exhausted</c>: a WebAssembly memory never shrinks, so the advice is a fresh runtime or a smaller
    ///     result. Anything else - a failure of the bridge itself - takes the <c>operation-failed</c> fallback, whose
    ///     fixed text reveals nothing about the exception.
    /// </remarks>
    public static (string Code, string Message) GetBrowserError(Exception exception)
    {
        return exception switch
        {
            BrowserInputException inputException => ("invalid-input", inputException.Message),
            CStructException cstructException => GetDomainBrowserError(cstructException),
            JsonException => ("invalid-json", "The JSON input is invalid."),
            OutOfMemoryException => ("resource-exhausted", "WebAssembly memory is exhausted. Run very large debug parses in a fresh worker or process, or narrow the root."),
            _ => ("operation-failed", "The operation failed unexpectedly."),
        };
    }

    /// <summary>Projects the public CLR code model directly into the browser wire vocabulary.</summary>
    /// <param name="exception">The library failure.</param>
    /// <returns>The category of its <see cref="CStructException.Code"/> and a curated message for known causes.</returns>
    private static (string Code, string Message) GetDomainBrowserError(CStructException exception)
    {
        // Known diagnostics preserve useful causes without echoing layout text, values, or stack traces. The texts
        // come from the runtime's catalog; library messages carry a trailing "(field ..., offset ...)" clause, so
        // every match is a prefix match.
        string? detail = exception.Message switch
        {
            var message when message.StartsWith(ReadFailures.ShortReadPrefix, StringComparison.Ordinal) => "Unexpected end of binary input. The field needs more bytes, or a terminated string is missing its terminator. Check the field length and the loaded data range.",
            var message when message.StartsWith(ReadFailures.TerminatedStringInvalid, StringComparison.Ordinal) => "The string contains invalid bytes for its declared encoding. Check whether the format uses ASCII, UTF-8, UTF-16, or a raw character buffer.",
            var message when message.StartsWith(ReadFailures.TerminatedStringLimit, StringComparison.Ordinal) => "The string exceeds MaxStringBytes. Check its terminator and encoding, or raise that safety limit within the browser maximum.",
            var message when message.StartsWith(ReadFailures.TotalBytesLimit, StringComparison.Ordinal) => "Reading the layout exceeds MaxTotalBytesRead. Check array lengths and pointer traversal, or raise that safety limit within the browser maximum.",
            var message when message.StartsWith(ReadFailures.NestingLimit, StringComparison.Ordinal) => "The structure exceeds MaxNestingDepth. Check nested records or raise that safety limit within the browser maximum.",
            var message when message.StartsWith(ReadFailures.PointerDepthLimit, StringComparison.Ordinal) => "Pointer traversal exceeds MaxPointerDepth. Check pointer chains or disable pointer dereferencing.",
            var message when message.StartsWith(ReadFailures.PointerTargetLimit, StringComparison.Ordinal) => "The pointer target exceeds MaxPointerTargetBytes. Check its type and address, or raise that safety limit within the browser maximum.",
            var message when message.Contains(ReadFailures.ArrayLengthLimitMarker, StringComparison.Ordinal) => "The array length exceeds MaxArrayElements. Check the count field and byte order, or raise that safety limit within the browser maximum.",
            var message when message.StartsWith(ReadFailures.PointerTargetOutsidePrefix, StringComparison.Ordinal) => "The pointer target is outside the loaded data. Check pointer width, byte order, addressing mode, and origin. A header preview may not include the target.",
            var message when message.StartsWith(ReadFailures.CyclicPointerPrefix, StringComparison.Ordinal) => "Pointer traversal encountered a cycle. Check the pointer layout or disable pointer dereferencing to inspect stored addresses.",
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
}
