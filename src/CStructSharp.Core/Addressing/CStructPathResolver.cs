namespace CStructSharp.Addressing;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using CStructSharp.Diagnostics;

/// <summary>
///     Parses and traverses the public path syntax used by read, debug, and address operations.
/// </summary>
internal static class CStructPathResolver
{
    // Paths are almost always string literals repeated per operation; a small per-process cache keyed by the exact
    // string skips re-parsing them. Bounded so unbounded generated paths cannot grow it without limit.
    private const int CacheCapacity = 256;
    private static readonly ConcurrentDictionary<string, PathSegment[]> Cache = new(StringComparer.Ordinal);

    public static IReadOnlyList<PathSegment> Parse(string path)
    {
        if (path is not null && Cache.TryGetValue(path, out PathSegment[]? cached))
        {
            return cached;
        }

        PathSegment[] segments = ParseUncached(path!);
        if (Cache.Count < CacheCapacity)
        {
            Cache.TryAdd(path!, segments);
        }

        return segments;
    }

    private static PathSegment[] ParseUncached(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new CStructPathException("Path is empty.");
        }

        string normalized = path.Trim();
        int segmentCount = 1;
        foreach (char character in normalized)
        {
            if (character == '.')
            {
                segmentCount++;
            }
        }

        var segments = new PathSegment[segmentCount];
        int segmentIndex = 0;
        while (true)
        {
            int dot = normalized.IndexOf('.');
            string raw = dot < 0 ? normalized : normalized.Substring(0, dot);
            if (raw.Length == 0)
            {
                throw new CStructPathException("Path contains an empty segment: " + path);
            }

            // A segment without brackets is simply a member name.
            int bracketStart = raw.IndexOf('[');
            if (bracketStart < 0)
            {
                ValidateIdentifier(raw, path);
                segments[segmentIndex++] = new PathSegment(raw, Array.Empty<int>());
            }
            else
            {
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

                    int bracketEnd = raw.IndexOf(']', position + 1) - (position + 1);
                    if (bracketEnd < 0)
                    {
                        throw new CStructPathException("Invalid path segment: " + raw);
                    }

                    string indexText = raw.Substring(position + 1, bracketEnd);

                    // Only non-negative decimal indexes that fit Int32 are part of the public path grammar.
                    if (indexText.Length == 0 ||
                        !AllDecimalDigits(indexText) ||
                        !int.TryParse(indexText, NumberStyles.None, CultureInfo.InvariantCulture, out int index))
                    {
                        throw new CStructPathException("Invalid array index: " + raw);
                    }

                    indexes.Add(index);
                    position += bracketEnd + 2;
                }

                segments[segmentIndex++] = new PathSegment(name, indexes);
            }

            if (dot < 0)
            {
                break;
            }

            normalized = normalized.Substring(dot + 1);
        }

        return segments;
    }

    private static bool AllDecimalDigits(string text)
    {
        foreach (char character in text)
        {
            if (character is < '0' or > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static void ValidateIdentifier(string name, string completePath)
    {
        bool valid = name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_');
        for (int index = 1; valid && index < name.Length; index++)
        {
            valid = char.IsLetterOrDigit(name[index]) || name[index] == '_';
        }

        if (!valid)
        {
            throw new CStructPathException($"Invalid path name '{name}' in '{completePath}'.");
        }
    }
}
