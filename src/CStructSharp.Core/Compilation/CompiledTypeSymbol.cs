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

    /// <summary>Creates a symbol whose layout is already known, such as a primitive or an enum.</summary>
    /// <param name="name">The type name the layout uses.</param>
    /// <param name="kind">The executor category.</param>
    /// <param name="declaration">
    ///     The layout declaration that introduced the type, or <see langword="null"/> for a built-in.
    /// </param>
    /// <param name="alignment">The type's alignment in bytes.</param>
    /// <param name="fixedSize">The type's size in bytes, or <see langword="null"/> when data decides it.</param>
    /// <param name="codecId">The runtime codec id, or <see cref="PrimitiveCatalog.NoCodec"/>.</param>
    /// <param name="isCustomCodec">Whether the type is a caller-supplied <see cref="ICustomCodec"/>.</param>
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

    /// <summary>Gets the type's alignment in bytes.</summary>
    /// <exception cref="InvalidOperationException">The composite's layout has not been completed yet.</exception>
    public int Alignment =>
        this.layoutComplete
            ? this.alignment
            : throw new InvalidOperationException("Compiled type layout is incomplete: " + this.Name);

    /// <summary>
    ///     Gets the layout declaration that introduced the type (a struct, union, or enum), or <see langword="null"/>
    ///     for a built-in type.
    /// </summary>
    public CStructElement? Declaration { get; }

    /// <summary>Whether the symbol is a caller-supplied <see cref="ICustomCodec"/> rather than a built-in primitive.</summary>
    public bool IsCustomCodec { get; }

    /// <summary>
    ///     Gets the compiled definition bound to this symbol, or <see langword="null"/> before <see cref="Bind"/>.
    /// </summary>
    public CompiledType? Definition => this.definition;

    /// <summary>Gets the type's size in bytes, or <see langword="null"/> when the data decides it.</summary>
    /// <exception cref="InvalidOperationException">The composite's layout has not been completed yet.</exception>
    public int? FixedSize =>
        this.layoutComplete
            ? this.fixedSize
            : throw new InvalidOperationException("Compiled type layout is incomplete: " + this.Name);

    /// <summary>Gets whether <see cref="Freeze"/> has run, so the symbol can no longer change.</summary>
    public bool IsFrozen => this.frozen;

    /// <summary>Gets whether a compiled definition has been bound to the symbol.</summary>
    public bool IsBound => this.definition is not null;

    /// <summary>Gets the executor category: primitive, enum, struct, or union.</summary>
    public CompiledTypeKind Kind { get; }

    /// <summary>Gets the type name the layout uses.</summary>
    public string Name { get; }

    /// <summary>
    ///     The codec id the runtime's delegate table is indexed by (a primitive's reader and writer), or
    ///     <see cref="PrimitiveCatalog.NoCodec"/> for a composite, an enum (which reads through its underlying
    ///     primitive), <c>void</c>, and a type only reachable through a pointer.
    /// </summary>
    public int CodecId { get; }

    /// <summary>
    ///     Creates the symbol of a struct or union before its members are compiled, so recursive references (a pointer
    ///     to the struct inside itself) resolve to it; its layout is incomplete until <see cref="CompleteLayout"/>.
    /// </summary>
    /// <param name="declaration">The struct or union declaration.</param>
    /// <returns>An unbound symbol of kind struct or union.</returns>
    internal static CompiledTypeSymbol PredeclareComposite(Struct declaration)
    {
        return new CompiledTypeSymbol(
            declaration.Name.Name,
            declaration.IsUnion ? CompiledTypeKind.Union : CompiledTypeKind.Struct,
            declaration);
    }

    /// <summary>
    ///     Attaches the compiled definition; a symbol is bound exactly once, after its layout is complete.
    /// </summary>
    /// <param name="value">The compiled definition.</param>
    /// <exception cref="CStructLayoutException">
    ///     The symbol is frozen, its layout is incomplete, or it was already bound.
    /// </exception>
    internal void Bind(CompiledType value)
    {
        if (this.frozen || !this.layoutComplete || this.definition is not null)
        {
            throw new CStructLayoutException("Compiled type symbol was bound more than once: " + this.Name);
        }

        this.definition = value;
    }

    /// <summary>Publishes the layout calculated while recursively binding this composite exactly once.</summary>
    /// <param name="valueAlignment">The composite's alignment in bytes.</param>
    /// <param name="valueFixedSize">
    ///     The composite's size in bytes, or <see langword="null"/> when the data decides it.
    /// </param>
    /// <exception cref="CStructLayoutException">The symbol is frozen or its layout was already completed.</exception>
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
