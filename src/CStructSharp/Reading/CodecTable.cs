namespace CStructSharp.Reading;

using System;
using System.Collections.Immutable;
using System.IO;
using CStructSharp.Codecs;
using CStructSharp.Compilation;

/// <summary>
///     The runtime half of the primitive vocabulary: one reader and one writer delegate per codec id of a
///     <see cref="PrimitiveCatalog"/>. The compiled model carries ids (it must compile without any I/O, inside the
///     source generator as well); the reader, writer, and address resolver index this table with them, so a field
///     read costs one array lookup and no string comparison.
/// </summary>
internal sealed class CodecTable
{
    private readonly Func<Stream, object>?[] readers;
    private readonly Action<Stream, object>?[] writers;

    /// <summary>Creates a table whose arrays are indexed by codec id; both must have the catalog's <see cref="PrimitiveCatalog.CodecCount"/> entries.</summary>
    /// <param name="catalog">The catalog whose codec ids index both arrays.</param>
    /// <param name="readers">The read delegate per codec id, or null where none exists; kept, not copied.</param>
    /// <param name="writers">The write delegate per codec id, or null where none exists; kept, not copied.</param>
    /// <exception cref="InvalidOperationException">An array length differs from the catalog's codec count.</exception>
    public CodecTable(PrimitiveCatalog catalog, Func<Stream, object>?[] readers, Action<Stream, object>?[] writers)
    {
        if (readers.Length != catalog.CodecCount || writers.Length != catalog.CodecCount)
        {
            throw new InvalidOperationException("The codec table does not cover every codec id of its catalog.");
        }

        this.Catalog = catalog;
        this.readers = readers;
        this.writers = writers;
    }

    /// <summary>The catalog whose ids index this table.</summary>
    public PrimitiveCatalog Catalog { get; }

    /// <summary>
    ///     Gets the caller's codec instances, in the catalog's custom-codec order (the ids after the canonical names); empty
    ///     for a table without custom codecs.
    /// </summary>
    public ImmutableArray<ICustomCodec> CustomCodecs { get; init; } = ImmutableArray<ICustomCodec>.Empty;

    /// <summary>The reader of <paramref name="field"/>'s element codec, or <see langword="null"/> when no primitive delegate reads it.</summary>
    /// <param name="field">The compiled field whose codec id selects the reader.</param>
    /// <returns>A delegate that reads one element from a stream and returns it boxed, or null.</returns>
    public Func<Stream, object>? ReaderOf(CompiledField field)
    {
        return field.CodecId < 0 ? null : this.readers[field.CodecId];
    }

    /// <summary>The writer of <paramref name="field"/>'s element codec, or <see langword="null"/>.</summary>
    /// <param name="field">The compiled field whose codec id selects the writer.</param>
    /// <returns>A delegate that writes one boxed element to a stream, or null without a primitive codec.</returns>
    public Action<Stream, object>? WriterOf(CompiledField field)
    {
        return field.CodecId < 0 ? null : this.writers[field.CodecId];
    }

    /// <summary>
    ///     The caller's codec a custom codec id names, which the compiled engine runs through
    ///     <see cref="CustomCodecAdapter"/> exactly as this table's adapter delegate does.
    /// </summary>
    /// <param name="codecId">A custom codec's id (at or after the canonical names).</param>
    /// <returns>The codec instance.</returns>
    public ICustomCodec CustomCodecOf(int codecId) => this.CustomCodecs[codecId - PrimitiveCatalog.CanonicalNames.Length];

    /// <summary>The terminated-string reader behind a <c>char *</c>-style pointer field, or <see langword="null"/>.</summary>
    /// <param name="field">The compiled pointer field whose terminated codec id selects the reader.</param>
    /// <returns>A delegate that reads the pointed-to terminated string, or null when the field has none.</returns>
    public Func<Stream, object>? TerminatedReaderOf(CompiledField field)
    {
        return field.TerminatedCodecId < 0 ? null : this.readers[field.TerminatedCodecId];
    }

    /// <summary>The reader registered for a readable name (canonical, neutral, alias, or custom), or <see langword="null"/>.</summary>
    /// <param name="name">The type spelling to look up in the catalog.</param>
    /// <returns>A delegate that reads one value of that type, or null when the name is unknown.</returns>
    public Func<Stream, object>? ReaderOf(string name)
    {
        int id = this.Catalog.CodecIdOf(name);
        return id < 0 ? null : this.readers[id];
    }

    /// <summary>The writer registered for a readable name, or <see langword="null"/>.</summary>
    /// <param name="name">The type spelling to look up in the catalog.</param>
    /// <returns>A delegate that writes one value of that type, or null when the name is unknown.</returns>
    public Action<Stream, object>? WriterOf(string name)
    {
        int id = this.Catalog.CodecIdOf(name);
        return id < 0 ? null : this.writers[id];
    }
}
