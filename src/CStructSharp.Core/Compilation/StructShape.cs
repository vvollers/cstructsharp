namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using CStructSharp.Values;

/// <summary>
///     The member layout shared by every <see cref="StructValue"/> produced for one compiled composite: names in
///     declaration order (with anonymous promoted members spliced in) and an ordinal index. Computed once per
///     composite, so a parsed struct only allocates its slot array.
/// </summary>
internal sealed class StructShape
{
    public static readonly StructShape Empty = new(Array.Empty<string>());

    private const int ReferenceScanLimit = 16;

    private readonly Dictionary<string, int> indexes;

    public StructShape(string[] names)
    {
        this.Names = names;
        this.indexes = new Dictionary<string, int>(names.Length, StringComparer.Ordinal);
        for (int index = 0; index < names.Length; index++)
        {
            // Duplicate names cannot occur in a compiled composite (validated at compile time); keep the first.
            this.indexes.TryAdd(names[index], index);
        }
    }

    public string[] Names { get; }

    public int Count => this.Names.Length;

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
        }

        return this.indexes.TryGetValue(name, out index);
    }
}
