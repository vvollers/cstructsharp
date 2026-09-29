namespace CStructSharp.Diagnostics;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using CStructSharp.Addressing;

/// <summary>Builds safe diagnostic context from already validated semantic paths and caller-owned streams.</summary>
internal static class ExceptionContext
{
    /// <summary>Attaches normalized operation context without allowing a diagnostic lookup to hide the primary failure.</summary>
    /// <param name="exception">The failure that receives the context; context it already carries is kept.</param>
    /// <param name="segments">The operation's parsed path, recorded as dotted text.</param>
    /// <param name="stream">
    ///     The operation's stream, whose current position is recorded as the failure offset when it can be read.
    /// </param>
    public static void Attach(
        CStructException exception,
        IReadOnlyList<PathSegment> segments,
        Stream stream)
    {
        exception.AttachContext(FormatPath(segments), TryGetDiagnosticPosition(stream));
    }

    /// <summary>Attaches the path and a known position to a failure, for an operation over memory that has no stream.</summary>
    /// <param name="exception">The failure; context already attached is kept.</param>
    /// <param name="segments">The operation's parsed path.</param>
    /// <param name="position">The position the operation reached, in bytes from the input's start.</param>
    public static void Attach(CStructException exception, IReadOnlyList<PathSegment> segments, long position)
    {
        exception.AttachContext(FormatPath(segments), position);
    }

    /// <summary>Formats only parser-validated identifiers and indexes, never arbitrary caller input.</summary>
    /// <param name="segments">The parsed path segments, each a name with optional element indexes.</param>
    /// <returns>The path as text, such as <c>header.entries[2].name</c>; an empty path gives an empty string.</returns>
    public static string? FormatPath(IReadOnlyList<PathSegment> segments)
    {
        return string.Join(
            ".",
            segments.Select(
                segment => segment.Name + string.Concat(
                    segment.Indexes.Select(index => "[" + index.ToString(CultureInfo.InvariantCulture) + "]"))));
    }

    /// <summary>Reads an optional stream offset while preserving the original operation exception.</summary>
    private static long? TryGetDiagnosticPosition(Stream stream)
    {
        try
        {
            return stream.Position;
        }
        catch (Exception)
        {
            // A secondary diagnostic failure must never replace the already-classified primary operation failure.
            return null;
        }
    }
}
