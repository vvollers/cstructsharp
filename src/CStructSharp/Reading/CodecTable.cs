namespace CStructSharp.Reading;

using System;
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

    /// <summary>The reader of <paramref name="field"/>'s element codec, or <see langword="null"/> when no primitive delegate reads it.</summary>
    public Func<Stream, object>? ReaderOf(CompiledField field)
    {
        return field.CodecId < 0 ? null : this.readers[field.CodecId];
    }

    /// <summary>The writer of <paramref name="field"/>'s element codec, or <see langword="null"/>.</summary>
    public Action<Stream, object>? WriterOf(CompiledField field)
    {
        return field.CodecId < 0 ? null : this.writers[field.CodecId];
    }

    /// <summary>The terminated-string reader behind a <c>char *</c>-style pointer field, or <see langword="null"/>.</summary>
    public Func<Stream, object>? TerminatedReaderOf(CompiledField field)
    {
        return field.TerminatedCodecId < 0 ? null : this.readers[field.TerminatedCodecId];
    }

    /// <summary>The terminated-string writer behind a <c>char *</c>-style pointer field, or <see langword="null"/>.</summary>
    public Action<Stream, object>? TerminatedWriterOf(CompiledField field)
    {
        return field.TerminatedCodecId < 0 ? null : this.writers[field.TerminatedCodecId];
    }

    /// <summary>The reader registered for a readable name (canonical, neutral, alias, or custom), or <see langword="null"/>.</summary>
    public Func<Stream, object>? ReaderOf(string name)
    {
        int id = this.Catalog.CodecIdOf(name);
        return id < 0 ? null : this.readers[id];
    }

    /// <summary>The writer registered for a readable name, or <see langword="null"/>.</summary>
    public Action<Stream, object>? WriterOf(string name)
    {
        int id = this.Catalog.CodecIdOf(name);
        return id < 0 ? null : this.writers[id];
    }
}
