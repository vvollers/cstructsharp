namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

/// <summary>Builds safe diagnostic context from already validated semantic paths and caller-owned streams.</summary>
public partial class CStruct
{
    /// <summary>Attaches normalized operation context without allowing a diagnostic lookup to hide the primary failure.</summary>
    private static void AttachExceptionContext(
        CStructException exception,
        IReadOnlyList<PathSegment> segments,
        Stream stream)
    {
        exception.AttachContext(FormatPath(segments), TryGetDiagnosticPosition(stream));
    }

    /// <summary>Formats only parser-validated identifiers and indexes, never arbitrary caller input.</summary>
    private static string? FormatPath(IReadOnlyList<PathSegment> segments)
    {
        return string.Join(
            ".",
            segments.Select(
                segment => segment.Index is int index
                               ? segment.Name + "[" + index.ToString(CultureInfo.InvariantCulture) + "]"
                               : segment.Name));
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
