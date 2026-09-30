namespace CStructSharp.Codecs;

using System;
using System.Collections.Immutable;
using System.IO;
using CStructSharp.Compilation;

/// <summary>
///     The runtime half of the primitive vocabulary: the stream writer per codec id of a <see cref="PrimitiveCatalog"/>
///     that the compiled write engine calls through a delegate, and the caller's custom codec instances. The compiled
///     model carries ids (it must compile without any I/O, inside the source generator as well); the engine indexes
///     this table with them, so a lookup costs one array access and no string comparison.
/// </summary>
internal sealed class CodecTable
{
    private readonly Action<Stream, object>?[] writers;

    /// <summary>Creates a table whose array is indexed by codec id; it must have the catalog's <see cref="PrimitiveCatalog.CodecCount"/> entries.</summary>
    /// <param name="catalog">The catalog whose codec ids index the array.</param>
    /// <param name="writers">The write delegate per codec id, or null where the engine encodes the codec itself; kept, not copied.</param>
    /// <exception cref="InvalidOperationException">The array length differs from the catalog's codec count.</exception>
    public CodecTable(PrimitiveCatalog catalog, Action<Stream, object>?[] writers)
    {
        if (writers.Length != catalog.CodecCount)
        {
            throw new InvalidOperationException("The codec table does not cover every codec id of its catalog.");
        }

        this.Catalog = catalog;
        this.writers = writers;
    }

    /// <summary>The catalog whose ids index this table.</summary>
    public PrimitiveCatalog Catalog { get; }

    /// <summary>
    ///     Gets the caller's codec instances, in the catalog's custom-codec order (the ids after the canonical names); empty
    ///     for a table without custom codecs.
    /// </summary>
    public ImmutableArray<ICustomCodec> CustomCodecs { get; init; } = ImmutableArray<ICustomCodec>.Empty;

    /// <summary>The writer of a catalog codec id, as the compiled engine's write programs name codecs, or <see langword="null"/>.</summary>
    /// <param name="codecId">A codec id of this table's catalog, or a negative id for none.</param>
    /// <returns>A delegate that writes one boxed element to a stream, or null for a codec the engine encodes itself.</returns>
    public Action<Stream, object>? WriterOfCodec(int codecId)
    {
        return codecId < 0 ? null : this.writers[codecId];
    }

    /// <summary>
    ///     The caller's codec a custom codec id names, which the compiled engine runs through
    ///     <see cref="CustomCodecAdapter"/>.
    /// </summary>
    /// <param name="codecId">A custom codec's id (at or after the canonical names).</param>
    /// <returns>The codec instance.</returns>
    public ICustomCodec CustomCodecOf(int codecId) => this.CustomCodecs[codecId - PrimitiveCatalog.CanonicalNames.Length];
}
