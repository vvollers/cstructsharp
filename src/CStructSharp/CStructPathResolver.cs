namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>
///     Parses and traverses the public path syntax used by read, debug, and address operations.
/// </summary>
internal static class CStructPathResolver
{
    /// <summary>Splits a dotted public path into names and zero or more array indexes per segment (LANG-05).</summary>
    public static IReadOnlyList<PathSegment> Parse(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CStructPathException("Path is empty.");
        }

        string normalized = path.Trim();
        string[] rawSegments = normalized.Split('.');
        var segments = new List<PathSegment>(rawSegments.Length);
        foreach (string raw in rawSegments)
        {
            if (raw.Length == 0)
            {
                throw new CStructPathException("Path contains an empty segment: " + path);
            }

            // A segment without brackets is simply a member name.
            int bracketStart = raw.IndexOf('[', StringComparison.Ordinal);
            if (bracketStart < 0)
            {
                ValidateIdentifier(raw, path);
                segments.Add(new PathSegment(raw, Array.Empty<int>()));
                continue;
            }

            string name = raw.Substring(0, bracketStart);
            ValidateIdentifier(name, path);

            // Repeated brackets - matrix[2][3] - mirror declaration syntax; each pair is its own dimension's index.
            var indexes = new List<int>();
            int position = bracketStart;
            while (position < raw.Length)
            {
                if (raw[position] != '[')
                {
                    throw new CStructPathException("Invalid path segment: " + raw);
                }

                int bracketEnd = raw.IndexOf(']', position + 1);
                if (bracketEnd < 0)
                {
                    throw new CStructPathException("Invalid path segment: " + raw);
                }

                string indexText = raw.Substring(position + 1, bracketEnd - position - 1);

                // Only non-negative decimal indexes that fit Int32 are part of the public path grammar.
                if (indexText.Length == 0 ||
                    indexText.Any(character => character is < '0' or > '9') ||
                    !int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                {
                    throw new CStructPathException("Invalid array index: " + raw);
                }

                indexes.Add(index);
                position = bracketEnd + 1;
            }

            segments.Add(new PathSegment(name, indexes));
        }

        return segments;
    }

    /// <summary>Requires the same identifier shape accepted for declared field and root names.</summary>
    private static void ValidateIdentifier(string name, string completePath)
    {
        if (name.Length == 0 ||
            !(char.IsLetter(name[0]) || name[0] == '_') ||
            name.Skip(1).Any(character => !(char.IsLetterOrDigit(character) || character == '_')))
        {
            throw new CStructPathException($"Invalid path name '{name}' in '{completePath}'.");
        }
    }
}
