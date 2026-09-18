namespace CStructSharp.Diagnostics;

using System;

/// <summary>Describes the byte range, path, type, and decoded value of one item read in debug mode.</summary>
/// <remarks>
///     Records are produced by <see cref="CStruct.ParseWithDebug(System.IO.Stream, string?, System.Collections.Generic.IReadOnlyDictionary{string, int}?, ReadOptions?)"/>, its overloads, and <c>ReadValueWithDebug</c>.
///     <see cref="Start"/> is inclusive and <see cref="End"/> exclusive, both relative to the operation's origin
///     (the stream position or region start when the operation began). The record is immutable; a caller that
///     needs the bytes selects <c>Start..End</c> from its own input, except for a union read as raw storage, whose
///     bytes <see cref="Bytes"/> carries because they are not addressable through a single field.
/// </remarks>
public readonly record struct DebugData
{
    /// <summary>Gets the inclusive zero-based start offset of the captured item.</summary>
    public long Start { get; init; }

    /// <summary>Gets the exclusive zero-based end offset of the captured item.</summary>
    public long End { get; init; }

    /// <summary>Gets the declaration path of the item, such as <c>header.samples[2].value</c>.</summary>
    public string Path => this.DebugStack?.ToString() ?? string.Empty;

    /// <summary>Gets the layout type spelling of the captured item, or <see langword="null"/> for a composite.</summary>
    public string? TypeName { get; init; }

    /// <summary>Gets the decoded value of the captured item.</summary>
    public object? Value { get; init; }

    /// <summary>Gets the raw bytes of a union captured as storage; empty for every other item.</summary>
    public ReadOnlyMemory<byte> Bytes { get; init; }

    /// <summary>Gets the linked path segments behind <see cref="Path"/>.</summary>
    internal DebugPath? DebugStack { get; init; }

    /// <summary>Gets the number of bytes the item occupies.</summary>
    public long Length => this.End - this.Start;

    /// <inheritdoc/>
    public override string ToString()
    {
        return $"{this.Path} [{this.Start}, {this.End}) {this.TypeName} = {this.Value}";
    }
}
