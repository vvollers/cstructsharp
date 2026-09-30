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

    /// <summary>
    ///     Splits a path such as <c>root.items[2].name</c> into its segments, reusing the cached segments of a path
    ///     parsed earlier in the process.
    /// </summary>
    /// <param name="path">The case-sensitive path; surrounding white space is ignored.</param>
    /// <returns>The segments in order; the shared list must not be modified.</returns>
    /// <exception cref="CStructPathException">The path is empty or malformed.</exception>
    public static IReadOnlyList<PathSegment> Parse(string path)
    {
        // The cache is looked up here rather than through a shared helper: every read of a path parses it, so a hit costs
        // no extra call.
        if (path is not null && Cache.TryGetValue(path, out PathSegment[]? cached))
        {
            return cached;
        }

        PathSegment[] segments = ParseUncached(path!, relative: false);
        if (Cache.Count < CacheCapacity)
        {
            Cache.TryAdd(path!, segments);
        }

        return segments;
    }

    /// <summary>
    ///     Splits a path that starts inside a value rather than at a layout root, such as a memory session's path relative to
    ///     a type: the layout grammar with one extension. An empty (or white-space) path selects the value itself and has
    ///     no segments, and a path may start with indexes (<c>[2].next</c>), which form a first segment with an empty name.
    /// </summary>
    /// <param name="path">The case-sensitive relative path; surrounding white space is ignored.</param>
    /// <returns>The segments in order; the shared list must not be modified.</returns>
    /// <exception cref="CStructPathException">The path is malformed.</exception>
    public static IReadOnlyList<PathSegment> ParseRelative(string path)
        => string.IsNullOrWhiteSpace(path) ? Array.Empty<PathSegment>() : ParseRelativeCached(path);

    /// <summary>Returns whether a name is a path identifier: a letter or underscore, then letters, digits and underscores.</summary>
    /// <param name="name">The name.</param>
    /// <returns>Whether a path can name it.</returns>
    public static bool IsIdentifier(string name)
    {
        bool valid = name.Length > 0 && (char.IsLetter(name[0]) || name[0] == '_');
        for (int index = 1; valid && index < name.Length; index++)
        {
            valid = char.IsLetterOrDigit(name[index]) || name[index] == '_';
        }

        return valid;
    }

    /// <summary>Parses a relative path through the relative cache, adding the result while the cache has room.</summary>
    /// <param name="path">The relative path, not empty or white space.</param>
    /// <returns>The segments.</returns>
    /// <exception cref="CStructPathException">The path is malformed.</exception>
    private static PathSegment[] ParseRelativeCached(string path)
    {
        if (RelativeCache.Paths.TryGetValue(path, out PathSegment[]? cached))
        {
            return cached;
        }

        PathSegment[] segments = ParseUncached(path, relative: true);
        if (RelativeCache.Paths.Count < CacheCapacity)
        {
            RelativeCache.Paths.TryAdd(path, segments);
        }

        return segments;
    }

    /// <summary>
    ///     Splits a path at its dots into segments, each a name followed by any number of <c>[n]</c> indexes, after trimming
    ///     surrounding white space.
    /// </summary>
    /// <param name="path">The path.</param>
    /// <param name="relative">Whether the first segment may have an empty name when it starts with an index.</param>
    /// <returns>The segments.</returns>
    /// <exception cref="CStructPathException">The path is empty or malformed.</exception>
    private static PathSegment[] ParseUncached(string path, bool relative)
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
                if (!(relative && segmentIndex == 0 && name.Length == 0))
                {
                    ValidateIdentifier(name, path);
                }

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

                    // Only non-negative decimal indexes that fit Int32 are part of the public path grammar; leading
                    // zeros are allowed (`[01]` selects element 1).
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

    /// <summary>Returns whether every character is an ASCII decimal digit.</summary>
    /// <param name="text">The index text between the brackets.</param>
    /// <returns>Whether the text holds digits only.</returns>
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

    /// <summary>Rejects a segment name that is not an identifier (<see cref="IsIdentifier"/>).</summary>
    /// <param name="name">The segment's name.</param>
    /// <param name="completePath">The whole path, named in the failure.</param>
    /// <exception cref="CStructPathException">The name is not an identifier.</exception>
    private static void ValidateIdentifier(string name, string completePath)
    {
        if (!IsIdentifier(name))
        {
            throw new CStructPathException($"Invalid path name '{name}' in '{completePath}'.");
        }
    }

    /// <summary>
    ///     The cache of relative paths, created on first use so a process that never parses one does not allocate it. It
    ///     is separate from the layout cache: <c>[1]</c> is a relative path but not a layout path, so one shared cache could
    ///     hand a layout operation a path its grammar rejects.
    /// </summary>
    private static class RelativeCache
    {
        /// <summary>The parsed relative paths, by exact text.</summary>
        public static readonly ConcurrentDictionary<string, PathSegment[]> Paths = new(StringComparer.Ordinal);
    }
}
