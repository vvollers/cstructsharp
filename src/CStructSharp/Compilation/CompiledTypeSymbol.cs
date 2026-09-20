namespace CStructSharp.Compilation;

using System;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>Provides a stable recursive identity that is bound exactly once before the model becomes observable.</summary>
internal sealed class CompiledTypeSymbol
{
    private int alignment;
    private CompiledType? definition;
    private int? fixedSize;
    private bool frozen;
    private bool layoutComplete;

    public CompiledTypeSymbol(
        string name,
        CompiledTypeKind kind,
        CStructElement? declaration,
        int alignment,
        int? fixedSize,
        int codecId,
        bool isCustomCodec = false)
    {
        this.Name = name;
        this.Kind = kind;
        this.Declaration = declaration;
        this.alignment = alignment;
        this.fixedSize = fixedSize;
        this.layoutComplete = true;
        this.CodecId = codecId;
        this.IsCustomCodec = isCustomCodec;
    }

    private CompiledTypeSymbol(string name, CompiledTypeKind kind, Struct declaration)
    {
        this.Name = name;
        this.Kind = kind;
        this.Declaration = declaration;
        this.CodecId = PrimitiveCatalog.NoCodec;
    }

    public int Alignment =>
        this.layoutComplete
            ? this.alignment
            : throw new InvalidOperationException("Compiled type layout is incomplete: " + this.Name);

    public CStructElement? Declaration { get; }

    /// <summary>Whether the symbol is a caller-supplied <see cref="ICustomCodec"/> rather than a built-in primitive.</summary>
    public bool IsCustomCodec { get; }

    public CompiledType? Definition => this.definition;

    public int? FixedSize =>
        this.layoutComplete
            ? this.fixedSize
            : throw new InvalidOperationException("Compiled type layout is incomplete: " + this.Name);

    public bool IsFrozen => this.frozen;

    public bool IsBound => this.definition is not null;

    public CompiledTypeKind Kind { get; }

    public string Name { get; }

    /// <summary>
    ///     The codec id the runtime's delegate table is indexed by (a primitive's reader and writer), or
    ///     <see cref="PrimitiveCatalog.NoCodec"/> for a composite, an enum (which reads through its underlying
    ///     primitive), <c>void</c>, and a type only reachable through a pointer.
    /// </summary>
    public int CodecId { get; }

    internal static CompiledTypeSymbol PredeclareComposite(Struct declaration)
    {
        return new CompiledTypeSymbol(
            declaration.Name.Name,
            declaration.IsUnion ? CompiledTypeKind.Union : CompiledTypeKind.Struct,
            declaration);
    }

    internal void Bind(CompiledType value)
    {
        if (this.frozen || !this.layoutComplete || this.definition is not null)
        {
            throw new CStructLayoutException("Compiled type symbol was bound more than once: " + this.Name);
        }

        this.definition = value;
    }

    /// <summary>Publishes the layout calculated while recursively binding this composite exactly once.</summary>
    internal void CompleteLayout(int valueAlignment, int? valueFixedSize)
    {
        if (this.frozen || this.layoutComplete)
        {
            throw new CStructLayoutException("Compiled type layout was completed more than once: " + this.Name);
        }

        this.alignment = valueAlignment;
        this.fixedSize = valueFixedSize;
        this.layoutComplete = true;
    }

    /// <summary>Prevents any later binding after the recursive construction graph has been validated.</summary>
    internal void Freeze()
    {
        if (!this.layoutComplete || this.definition is null)
        {
            throw new CStructLayoutException("Compiled type symbol was frozen before binding: " + this.Name);
        }

        this.frozen = true;
    }
}
