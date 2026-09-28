namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Threading;

/// <summary>
///     The member layout shared by every <see cref="CStructSharp.Values.StructValue"/> produced for one compiled composite: names in
///     declaration order (with anonymous promoted members spliced in) and an ordinal index. Computed once per
///     composite, so a parsed struct only allocates its slot array.
/// </summary>
internal sealed class StructShape
{
    /// <summary>The shape of a composite without members, shared by every such composite.</summary>
    public static readonly StructShape Empty = new(Array.Empty<string>());

    private const int ReferenceScanLimit = 16;

    private const int MaximumMappedNames = 256;

    private readonly Dictionary<string, int> indexes;

    // Open-addressed table over the names for lookups by a section of a longer string (a path segment), so a
    // path walk never allocates the segment. Each entry is a slot index plus one; zero marks an empty bucket.
    // Only built for shapes too large for the linear scan.
    private readonly int[]? segmentBuckets;

    private readonly object mappedNamesLock = new();

    private MappedName[] mappedNames = Array.Empty<MappedName>();

    /// <summary>Builds the member table: the ordinal index, and the section lookup table for a shape too large for a linear scan.</summary>
    /// <param name="names">The member names in declaration order.</param>
    public StructShape(string[] names)
    {
        this.Names = names;
        this.indexes = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        for (int index = 0; index < names.Length; index++)
        {
            // Duplicate names cannot occur in a compiled composite (validated at compile time); keep the first.
            this.indexes.TryAdd(names[index], index);
        }

        if (names.Length > ReferenceScanLimit)
        {
            // A power-of-two capacity of at least twice the name count keeps probe chains short.
            int capacity = 1;
            while (capacity < names.Length * 2)
            {
                capacity <<= 1;
            }

            this.segmentBuckets = new int[capacity];
            for (int index = 0; index < names.Length; index++)
            {
                if (this.indexes[names[index]] != index)
                {
                    continue;
                }

                int bucket = SegmentHash(names[index], 0, names[index].Length) & (capacity - 1);
                while (this.segmentBuckets[bucket] != 0)
                {
                    bucket = (bucket + 1) & (capacity - 1);
                }

                this.segmentBuckets[bucket] = index + 1;
            }
        }
    }

    /// <summary>Gets the member names in slot order; callers must not modify the array.</summary>
    public string[] Names { get; }

    /// <summary>Gets the number of members, which is also the slot count of each struct value.</summary>
    public int Count => this.Names.Length;

    /// <summary>
    ///     Finds the layout member a mapped property name resolved to earlier (see <c>MappedTypes.MemberName</c>), so a
    ///     mapper resolves each property once per shape rather than on every read and write. A mapper passes the same
    ///     string literal every time, so the entries are compared by reference first.
    /// </summary>
    /// <param name="propertyName">The mapped property's name.</param>
    /// <returns>The member name, or <see langword="null"/> when the property was not resolved yet.</returns>
    public string? FindMappedName(string propertyName)
    {
        MappedName[] entries = Volatile.Read(ref this.mappedNames);
        foreach (MappedName entry in entries)
        {
            if (ReferenceEquals(entry.Property, propertyName))
            {
                return entry.Member;
            }
        }

        foreach (MappedName entry in entries)
        {
            if (string.Equals(entry.Property, propertyName, StringComparison.Ordinal))
            {
                return entry.Member;
            }
        }

        return null;
    }

    /// <summary>Records a resolved mapped property name; the table stops growing at a size no generated mapper reaches.</summary>
    /// <param name="propertyName">The mapped property's name.</param>
    /// <param name="memberName">The layout member it resolved to.</param>
    public void AddMappedName(string propertyName, string memberName)
    {
        lock (this.mappedNamesLock)
        {
            MappedName[] entries = this.mappedNames;
            if (entries.Length >= MaximumMappedNames || this.FindMappedName(propertyName) is not null)
            {
                return;
            }

            // Copy on write: readers see either the old table or the new one, never a partly filled array.
            var extended = new MappedName[entries.Length + 1];
            Array.Copy(entries, extended, entries.Length);
            extended[entries.Length] = new MappedName(propertyName, memberName);
            Volatile.Write(ref this.mappedNames, extended);
        }
    }

    /// <summary>Finds the slot of the member named <paramref name="name"/>, ordinally.</summary>
    /// <param name="name">The member name.</param>
    /// <param name="index">The member's slot when found; otherwise zero.</param>
    /// <returns><see langword="true"/> when the shape has the member.</returns>
    public bool TryGetIndex(string name, out int index)
    {
        // The writer, the path resolver and the reader all hand over the compiled field's own name instance, so a
        // reference scan of a small shape beats hashing the key (the same trick ExpandoObject's class table uses).
        string[] names = this.Names;
        if (names.Length <= ReferenceScanLimit)
        {
            for (int slot = 0; slot < names.Length; slot++)
            {
                if (ReferenceEquals(names[slot], name))
                {
                    index = slot;
                    return true;
                }
            }

            // A caller's own string (a literal in application code) is equal but not the same instance; comparing a
            // handful of names by length and characters is still cheaper than hashing the key.
            return this.TryGetIndex(name, 0, name.Length, out index);
        }

        return this.indexes.TryGetValue(name, out index);
    }

    /// <summary>
    ///     Finds the member named by <paramref name="length"/> characters of <paramref name="text"/> starting at
    ///     <paramref name="start"/>, ordinally, without allocating the name: a path walk looks up each segment in place.
    /// </summary>
    /// <param name="text">The string holding the name, usually a whole path.</param>
    /// <param name="start">The name's first character within <paramref name="text"/>.</param>
    /// <param name="length">The name's length in characters.</param>
    /// <param name="index">The member's slot when found; otherwise zero.</param>
    /// <returns><see langword="true"/> when the shape has the member.</returns>
    public bool TryGetIndex(string text, int start, int length, out int index)
    {
        string[] names = this.Names;
        if (this.segmentBuckets is not int[] buckets)
        {
            for (int slot = 0; slot < names.Length; slot++)
            {
                string name = names[slot];
                if (name.Length == length && string.CompareOrdinal(name, 0, text, start, length) == 0)
                {
                    index = slot;
                    return true;
                }
            }

            index = 0;
            return false;
        }

        int mask = buckets.Length - 1;
        for (int bucket = SegmentHash(text, start, length) & mask; buckets[bucket] != 0; bucket = (bucket + 1) & mask)
        {
            string name = names[buckets[bucket] - 1];
            if (name.Length == length && string.CompareOrdinal(name, 0, text, start, length) == 0)
            {
                index = buckets[bucket] - 1;
                return true;
            }
        }

        index = 0;
        return false;
    }

    /// <summary>An ordinal FNV-1a hash of a string section, identical for a whole name and the same characters inside a path.</summary>
    private static int SegmentHash(string text, int start, int length)
    {
        uint hash = 2166136261;
        for (int offset = start; offset < start + length; offset++)
        {
            hash = (hash ^ text[offset]) * 16777619;
        }

        return (int)(hash & 0x7FFFFFFF);
    }

    /// <summary>One resolved mapped property: the property name a mapper asked for and the member it maps to.</summary>
    private readonly struct MappedName
    {
        /// <summary>Pairs a property name with its member.</summary>
        /// <param name="property">The mapped property's name.</param>
        /// <param name="member">The layout member it resolved to.</param>
        public MappedName(string property, string member)
        {
            this.Property = property;
            this.Member = member;
        }

        /// <summary>Gets the mapped property's name.</summary>
        public string Property { get; }

        /// <summary>Gets the layout member the property resolved to.</summary>
        public string Member { get; }
    }
}
